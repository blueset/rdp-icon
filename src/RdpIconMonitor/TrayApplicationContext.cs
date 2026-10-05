using System.ComponentModel;
using System.Diagnostics;
using RdpIconMonitor.Configuration;
using RdpIconMonitor.Windows;

namespace RdpIconMonitor;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly string configPath;
    private readonly Diagnostics diagnostics;
    private readonly NotifyIcon tray;
    private readonly ToolStripMenuItem status;
    private readonly ToolStripMenuItem pause;
    private readonly ToolStripMenuItem startup;
    private readonly MonitorEngine engine;
    private readonly ConfigurationWatcher watcher;
    private readonly WindowEvents events;
    private readonly EventWaitHandle stop;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 250 };
    private readonly Control dispatcher = new();
    private long reloadAt;
    private long lastPass;
    private int workRequested = 1;
    private int reloadRequested = 1;
    private bool working;
    private bool paused;
    private bool exitRequested;
    private bool disposed;
    private string? lastBalloon;

    public TrayApplicationContext(string configPath, string userDirectory, EventWaitHandle stop)
    {
        this.configPath = configPath;
        this.stop = stop;
        diagnostics = new(userDirectory);
        engine = new(configPath, diagnostics.Report);
        _ = dispatcher.Handle;
        var menu = new ContextMenuStrip();
        status = new("Starting") { Enabled = false };
        pause = new("Pause", null, (_, _) =>
        {
            paused = !paused;
            pause!.Text = paused ? "Resume" : "Pause";
            RequestWork();
        });
        startup = new("Start at login") { Checked = StartupRegistration.Enabled };
        startup.Click += (_, _) => Guard(() =>
        {
            StartupRegistration.Set(!startup.Checked, configPath);
            startup.Checked = StartupRegistration.Enabled;
        });
        menu.Items.AddRange([
            status,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Open configuration", null, (_, _) => Guard(() => Open(configPath))),
            new ToolStripMenuItem("Reload configuration", null, (_, _) =>
            {
                Interlocked.Exchange(ref reloadRequested, 1);
                RequestWork();
            }),
            pause, startup,
            new ToolStripMenuItem("Open diagnostics", null, (_, _) => Guard(() => Open(userDirectory))),
            new ToolStripSeparator(),
            new ToolStripMenuItem("Exit and restore icons", null, (_, _) => RequestExit())
        ]);
        tray = new()
        {
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
            Text = "RDP icons - starting",
            Visible = true
        };
        tray.DoubleClick += (_, _) => Guard(() => Open(configPath));
        watcher = new(configPath, () =>
        {
            Interlocked.Exchange(ref reloadAt, Stopwatch.GetTimestamp());
            Interlocked.Exchange(ref reloadRequested, 1);
            RequestWork();
        }, message =>
        {
            diagnostics.Report(message);
            dispatcher.BeginInvoke(() => ShowError(message));
        });
        events = new(RequestWork);
        timer.Tick += (_, _) =>
        {
            if (stop.WaitOne(0)) RequestExit();
            long changed = Interlocked.Read(ref reloadAt);
            if (working || changed != 0 && Stopwatch.GetElapsedTime(changed) < TimeSpan.FromMilliseconds(350)) return;
            if (Volatile.Read(ref workRequested) != 0 || Stopwatch.GetElapsedTime(lastPass) >= TimeSpan.FromSeconds(2))
                RunPass();
        };
        timer.Start();
        diagnostics.Report($"Started with configuration {configPath}. Login startup is {(startup.Checked ? "enabled" : "disabled")}.");
    }

    private void RequestWork() => Interlocked.Exchange(ref workRequested, 1);
    private void RequestExit()
    {
        exitRequested = true;
        RequestWork();
    }

    private async void RunPass()
    {
        working = true;
        Interlocked.Exchange(ref workRequested, 0);
        bool force = Interlocked.Exchange(ref reloadRequested, 0) != 0;
        bool restoreOnly = exitRequested;
        bool pauseSnapshot = paused;
        try
        {
            bool restored = await Task.Run(() => engine.Run(pauseSnapshot, restoreOnly, force));
            if (restoreOnly && restored)
            {
                diagnostics.Report("All managed icon assignments restored; exiting.");
                events.Dispose();
                engine.Dispose();
                watcher.Dispose();
                timer.Stop();
                tray.Visible = false;
                disposed = true;
                ExitThread();
                return;
            }
            string state = exitRequested ? "Restoration pending - retrying" :
                engine.Error is not null ? $"Error - {engine.ManagedCount} managed" :
                !engine.Loaded ? "Inactive - invalid configuration" :
                paused ? "Paused - original icons restored" : $"Active - {engine.ManagedCount} managed";
            status.Text = state;
            tray.Text = ("RDP icons - " + state)[..Math.Min(63, state.Length + 12)];
            tray.Icon = engine.Error is null ? SystemIcons.Application : SystemIcons.Warning;
            if (engine.Error is { } error) ShowError(error);
            else lastBalloon = null;
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            diagnostics.Report(error.ToString());
            ShowError(error.Message);
        }
        finally
        {
            working = false;
            lastPass = Stopwatch.GetTimestamp();
        }
    }

    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            diagnostics.Report(error.ToString());
            ShowError(error.Message);
        }
    }

    private void ShowError(string message)
    {
        status.Text = "Error - see diagnostics";
        tray.Icon = SystemIcons.Warning;
        tray.Text = "RDP icons - error; see diagnostics";
        if (lastBalloon == message) return;
        lastBalloon = message;
        tray.ShowBalloonTip(5000, "RDP icon monitor", message, ToolTipIcon.Warning);
    }

    private static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            if (!disposed)
                throw new InvalidOperationException("Tray must restore icons before disposal.");
            tray.ContextMenuStrip?.Dispose();
            tray.Dispose();
            dispatcher.Dispose();
        }
        base.Dispose(disposing);
    }
}
