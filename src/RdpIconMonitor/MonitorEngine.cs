using System.Text.Json;
using System.ComponentModel;
using RdpIconMonitor.Configuration;
using RdpIconMonitor.Rendering;
using RdpIconMonitor.Windows;

namespace RdpIconMonitor;

internal sealed class MonitorEngine : IDisposable
{
    private readonly string path;
    private readonly Action<string> report;
    private readonly IconRepository icons = new();
    private readonly WindowIconController controller;
    private readonly IHostWindowSystem hostWindows;
    private IconConfiguration current = new([]);
    private string? attemptedContent;
    private string? reloadError;
    private string? passWarning;
    private int consecutiveWarningPasses;
    public string? Error { get; private set; }
    public bool Loaded { get; private set; }
    public int ManagedCount => controller.ManagedCount;
    public IconConfiguration CurrentConfiguration => current;
    public IReadOnlyList<WindowSnapshot> Snapshots => controller.LastSnapshots;
    public IReadOnlyList<HostWindowSnapshot> HostWindows { get; private set; } = [];

    public MonitorEngine(
        string path,
        Action<string> report,
        IWindowSystem? windowSystem = null,
        IHostWindowSystem? hostWindowSystem = null)
    {
        this.path = path;
        this.report = report;
        controller = new(
            windowSystem ?? new Win32WindowSystem(RecordWarning),
            icons,
            RecordWarning,
            RecordDiagnostic);
        hostWindows = hostWindowSystem ?? new HostWindowSystem(RecordWarning);
    }

    public bool Run(bool paused, bool exit, bool forceReload)
    {
        Error = reloadError;
        passWarning = null;
        if (exit)
        {
            bool restored = controller.RestoreAll();
            UpdateError(immediate: true);
            return restored;
        }
        Reload(forceReload);
        controller.Reconcile(current, paused || !Loaded);
        var enabledHostApps = current.HostApps?.Where(app => app.Enabled).ToArray() ?? [];
        if (Loaded && enabledHostApps.Length > 0)
        {
            try
            {
                HostWindows = hostWindows.Enumerate(enabledHostApps);
            }
            catch (Exception error) when (error is Win32Exception or InvalidOperationException or ArgumentException)
            {
                RecordWarning($"Cannot enumerate host application windows; previous dock entries remain: {error.Message}");
            }
        }
        else
        {
            HostWindows = [];
        }
        UpdateError();
        return false;
    }

    private void UpdateError(bool immediate = false)
    {
        if (passWarning is null)
        {
            consecutiveWarningPasses = 0;
            Error = reloadError;
        }
        else
        {
            consecutiveWarningPasses++;
            Error = immediate || consecutiveWarningPasses >= 3 ? passWarning : reloadError;
        }
    }

    private void Reload(bool force)
    {
        try
        {
            string content = File.ReadAllText(path);
            if (!force && content == attemptedContent) return;
            attemptedContent = content;
            var candidate = IconConfiguration.Parse(content);
            icons.Prepare(candidate.Instances.Select(rule => rule.Style));
            current = candidate;
            Loaded = true;
            reloadError = null;
            Error = null;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            reloadError = $"Configuration reload rejected; {(Loaded ? "previous rules remain active" : "monitor is inactive")}: {error.Message}";
            Report(reloadError);
        }
    }

    private void Report(string message)
    {
        Error = message;
        report(message);
    }

    private void RecordWarning(string message)
    {
        passWarning = message;
        report(message);
    }

    private void RecordDiagnostic(string message) => report(message);

    public bool TryActivate(string title) => controller.TryActivate(title);
    public bool TryActivate(WindowIdentity identity) =>
        controller.TryActivate(identity) || (IsEnabledHost(identity) && hostWindows.TryActivate(identity));
    public bool TryMinimize(string title) => controller.TryMinimize(title);
    public bool TryMinimize(WindowIdentity identity) =>
        controller.TryMinimize(identity) || (IsEnabledHost(identity) && hostWindows.TryMinimize(identity));

    private bool IsEnabledHost(WindowIdentity identity) =>
        HostWindows.Any(snapshot =>
            snapshot.Window.Identity == identity &&
            snapshot.Configuration.Enabled &&
            (current.HostApps ?? []).Any(app =>
                app.Enabled && app == snapshot.Configuration));

    public void Dispose()
    {
        if (controller.ManagedCount != 0)
            throw new InvalidOperationException("Cannot dispose the monitor while windows may reference its icons.");
        icons.Dispose();
    }
}
