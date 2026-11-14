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
        controller = new(windowSystem ?? new Win32WindowSystem(Report), icons, Report);
        hostWindows = hostWindowSystem ?? new HostWindowSystem(Report);
    }

    public bool Run(bool paused, bool exit, bool forceReload)
    {
        Error = reloadError;
        if (exit) return controller.RestoreAll();
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
                Report($"Cannot enumerate host application windows; previous dock entries remain: {error.Message}");
            }
        }
        else
        {
            HostWindows = [];
        }
        return false;
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
