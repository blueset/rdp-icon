using RdpIconMonitor.Configuration;
using RdpIconMonitor.Windows;

namespace RdpIconMonitor.Dock;

internal sealed class SessionDockManager(
    Func<WindowIdentity, bool> activate,
    Func<WindowIdentity, bool> minimize,
    Action<string> report) : IDisposable
{
    private readonly Dictionary<string, SessionDockForm> forms = new(StringComparer.OrdinalIgnoreCase);
    private bool disposed;

    public void Update(IconConfiguration config, IReadOnlyList<WindowSnapshot> snapshots)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (config.Instances.Length == 0)
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
                snapshots));
        }
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
    }
}
