using System.Text.Json;
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
    private IconConfiguration current = new([]);
    private string? attemptedContent;
    private string? reloadError;
    public string? Error { get; private set; }
    public bool Loaded { get; private set; }
    public int ManagedCount => controller.ManagedCount;

    public MonitorEngine(string path, Action<string> report, IWindowSystem? windowSystem = null)
    {
        this.path = path;
        this.report = report;
        controller = new(windowSystem ?? new Win32WindowSystem(Report), icons, Report);
    }

    public bool Run(bool paused, bool exit, bool forceReload)
    {
        Error = reloadError;
        if (exit) return controller.RestoreAll();
        Reload(forceReload);
        controller.Reconcile(current, paused || !Loaded);
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

    public void Dispose()
    {
        if (controller.ManagedCount != 0)
            throw new InvalidOperationException("Cannot dispose the monitor while windows may reference its icons.");
        icons.Dispose();
    }
}
