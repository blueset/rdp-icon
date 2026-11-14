using System.Diagnostics;
using RdpIconMonitor.Configuration;
using RdpIconMonitor.Windows;

namespace RdpIconMonitor.Dock;

internal sealed class SessionDockManager : IDisposable
{
    private static readonly TimeSpan AttentionLifetime = TimeSpan.FromSeconds(3);
    private readonly Dictionary<string, SessionDockForm> forms = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<nint, long> attention = [];
    private readonly Func<WindowIdentity, bool> activate;
    private readonly Func<WindowIdentity, bool> minimize;
    private readonly Action<string> report;
    private readonly ShellHookWindow shellHook;
    private bool disposed;

    public SessionDockManager(
        Func<WindowIdentity, bool> activate,
        Func<WindowIdentity, bool> minimize,
        Action<string> report,
        Action requestWork)
    {
        this.activate = activate;
        this.minimize = minimize;
        this.report = report;
        shellHook = new(
            window =>
            {
                attention[window] = Stopwatch.GetTimestamp();
                requestWork();
            },
            window =>
            {
                if (attention.Remove(window)) requestWork();
            },
            report);
    }

    public SessionDockManager(
        Func<WindowIdentity, bool> activate,
        Func<WindowIdentity, bool> minimize,
        Action<string> report) : this(activate, minimize, report, () => { })
    {
    }

    public void Update(
        IconConfiguration config,
        IReadOnlyList<WindowSnapshot> snapshots,
        IReadOnlyList<HostWindowSnapshot>? hostSnapshots = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var hostWindows = hostSnapshots ?? [];
        var hostHandles = hostWindows.Select(snapshot => snapshot.Window.Identity.Handle).ToHashSet();
        foreach (var handle in attention.Keys.Where(handle => !hostHandles.Contains(handle)).ToArray())
            attention.Remove(handle);
        if (config.Instances.Length == 0 && !(config.HostApps ?? []).Any(app => app.Enabled))
        {
            foreach (var form in forms.Values) form.Dispose();
            forms.Clear();
            return;
        }
        var screens = Screen.AllScreens
            .Where(screen => config.Dock?.ShowOnAllMonitors == true || screen.Primary)
            .ToArray();
        var wanted = screens.Select(screen => screen.DeviceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in forms.Where(pair => !wanted.Contains(pair.Key)).ToArray())
        {
            pair.Value.Dispose();
            forms.Remove(pair.Key);
        }
        foreach (var screen in screens)
        {
            if (!forms.TryGetValue(screen.DeviceName, out var form))
            {
                form = new SessionDockForm(identity =>
                {
                    try { return activate(identity); }
                    catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException)
                    {
                        report($"Cannot activate window 0x{identity.Handle:X}: {error.Message}");
                        return false;
                    }
                }, identity =>
                {
                    try { return minimize(identity); }
                    catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException)
                    {
                        report($"Cannot minimize window 0x{identity.Handle:X}: {error.Message}");
                        return false;
                    }
                });
                forms.Add(screen.DeviceName, form);
            }
            form.Update(SessionDockState.Create(
                screen.DeviceName,
                screen.Bounds,
                screen.WorkingArea,
                config,
                snapshots,
                hostWindows,
                IsAlerting));
        }
    }

    private bool IsAlerting(HostWindowSnapshot snapshot)
    {
        if (!snapshot.Configuration.TaskbarAlerts) return false;
        if (!attention.TryGetValue(snapshot.Window.Identity.Handle, out var timestamp)) return false;
        return Stopwatch.GetElapsedTime(timestamp) < AttentionLifetime;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var form in forms.Values.ToArray())
        {
            try { form.Dispose(); }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                report($"Cannot close session dock: {error.Message}");
            }
        }
        forms.Clear();
        shellHook.Dispose();
    }
}
