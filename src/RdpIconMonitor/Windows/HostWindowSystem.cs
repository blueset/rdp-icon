using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using RdpIconMonitor.Configuration;

namespace RdpIconMonitor.Windows;

internal sealed record HostWindowSnapshot(DockHostApp Configuration, WindowSnapshot Window);

internal interface IHostWindowSystem
{
    IReadOnlyList<HostWindowSnapshot> Enumerate(IReadOnlyList<DockHostApp> applications);
    bool IsSame(WindowIdentity window);
    bool TryActivate(WindowIdentity window);
    bool TryMinimize(WindowIdentity window);
}

internal sealed class HostWindowSystem(Action<string> report) : IHostWindowSystem
{
    private readonly object gate = new();
    private readonly Dictionary<WindowIdentity, DockHostApp> known = [];

    public IReadOnlyList<HostWindowSnapshot> Enumerate(IReadOnlyList<DockHostApp> applications)
    {
        var byProcess = applications
            .Where(app => app.Enabled)
            .GroupBy(app => app.Process, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var desired = new Dictionary<nint, List<(DockHostApp App, int ProcessId, long StartTicks)>>();
        using var current = Process.GetCurrentProcess();
        foreach (var group in byProcess)
        {
            foreach (var process in Process.GetProcessesByName(group.Key))
            {
                using (process)
                {
                    try
                    {
                        if (process.SessionId != current.SessionId) continue;
                        nint handle = process.MainWindowHandle;
                        if (handle == 0) continue;
                        foreach (var application in group.Value)
                        {
                            if (!desired.TryGetValue(handle, out var targets))
                                desired[handle] = targets = [];
                            targets.Add((
                                application,
                                process.Id,
                                process.StartTime.ToUniversalTime().Ticks));
                        }
                    }
                    catch (InvalidOperationException) { /* The process exited during enumeration. */ }
                    catch (Win32Exception error) { report($"Cannot inspect {group.Key} PID {process.Id}: {error.Message}"); }
                    catch (System.Security.SecurityException error) { report($"Cannot inspect {group.Key} PID {process.Id}: {error.Message}"); }
                }
            }
        }

        var snapshots = new List<HostWindowSnapshot>();
        var screenBounds = Screen.AllScreens.Select(screen => screen.Bounds).ToArray();
        Rectangle primaryBounds = Screen.PrimaryScreen?.Bounds ?? Rectangle.Empty;
        nint foreground = NativeMethods.GetForegroundWindow();
        int zOrder = 0;
        NativeMethods.EnumCallback callback = (window, _) =>
        {
            int order = zOrder++;
            if (!desired.TryGetValue(window, out var targets)) return true;
            var title = new StringBuilder(NativeMethods.GetWindowTextLength(window) + 1);
            NativeMethods.GetWindowText(window, title, title.Capacity);
            uint dpi = NativeMethods.GetDpiForWindow(window);
            if (dpi == 0) return true;
            bool minimized = NativeMethods.IsIconic(window);
            var placement = new NativeMethods.WINDOWPLACEMENT
            {
                length = (uint)Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>()
            };
            bool hasBounds = minimized && NativeMethods.GetWindowPlacement(window, ref placement);
            NativeMethods.Rect rect = hasBounds ? placement.normalPosition : default;
            hasBounds = hasBounds || NativeMethods.GetWindowRect(window, out rect);
            foreach (var target in targets)
            {
                var snapshot = new WindowSnapshot(
                    new(window, target.ProcessId, target.StartTicks),
                    title.ToString(),
                    NativeMethods.GetSystemMetricsForDpi(49, dpi),
                    NativeMethods.GetSystemMetricsForDpi(11, dpi),
                    hasBounds
                        ? ResolveScreenBounds(
                            Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom),
                            minimized,
                            screenBounds,
                            primaryBounds)
                        : null,
                    NativeMethods.IsWindowVisible(window),
                    minimized,
                    window == foreground,
                    order);
                snapshots.Add(new(target.App, snapshot));
            }
            return true;
        };
        if (!NativeMethods.EnumWindows(callback, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot enumerate host application windows.");

        lock (gate)
        {
            known.Clear();
            foreach (var snapshot in snapshots)
                known[snapshot.Window.Identity] = snapshot.Configuration;
        }
        return snapshots;
    }

    public bool IsSame(WindowIdentity window)
    {
        DockHostApp? application;
        lock (gate)
            if (!known.TryGetValue(window, out application) || application is null) return false;
        if (!NativeMethods.IsWindow(window.Handle)) return false;
        NativeMethods.GetWindowThreadProcessId(window.Handle, out var pid);
        if (pid != window.ProcessId) return false;
        try
        {
            using var process = Process.GetProcessById(window.ProcessId);
            return process.ProcessName.Equals(application.Process, StringComparison.OrdinalIgnoreCase) &&
                process.StartTime.ToUniversalTime().Ticks == window.ProcessStartTicks &&
                process.MainWindowHandle == window.Handle;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    public bool TryActivate(WindowIdentity window)
    {
        try
        {
            if (!IsSame(window)) return false;
            NativeMethods.AllowSetForegroundWindow(-1);
            if (NativeMethods.IsIconic(window.Handle))
                NativeMethods.ShowWindow(window.Handle, 9);
            NativeMethods.BringWindowToTop(window.Handle);
            return NativeMethods.SetForegroundWindow(window.Handle);
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            report($"Cannot activate host window 0x{window.Handle:X}: {error.Message}");
            return false;
        }
    }

    public bool TryMinimize(WindowIdentity window)
    {
        try
        {
            if (!IsSame(window)) return false;
            NativeMethods.ShowWindow(window.Handle, 6);
            return NativeMethods.IsIconic(window.Handle);
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            report($"Cannot minimize host window 0x{window.Handle:X}: {error.Message}");
            return false;
        }
    }

    internal static Rectangle ResolveScreenBounds(
        Rectangle bounds,
        bool minimized,
        IReadOnlyList<Rectangle> screens,
        Rectangle primary)
    {
        if (!minimized || primary.IsEmpty ||
            screens.Any(screen => Rectangle.Intersect(screen, bounds).Width >= 32 &&
                Rectangle.Intersect(screen, bounds).Height >= 32))
            return bounds;
        int width = Math.Max(32, Math.Min(bounds.Width, primary.Width - 32));
        int height = Math.Max(32, Math.Min(bounds.Height, primary.Height - 32));
        return new(
            primary.X + Math.Min(16, Math.Max(0, primary.Width - width)),
            primary.Y + Math.Min(16, Math.Max(0, primary.Height - height)),
            width,
            height);
    }
}
