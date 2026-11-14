using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace RdpIconMonitor.Windows;

public readonly record struct WindowIdentity(nint Handle, int ProcessId, long ProcessStartTicks);
public sealed record WindowSnapshot(
    WindowIdentity Identity,
    string Title,
    int SmallSize,
    int LargeSize,
    Rectangle? Bounds = null,
    bool Visible = true,
    bool Minimized = false,
    bool Foreground = false,
    int ZOrder = int.MaxValue);

public interface IWindowSystem
{
    IReadOnlyList<WindowSnapshot> Enumerate();
    bool IsSame(WindowIdentity window);
    nint GetIcon(WindowIdentity window, int slot);
    nint SetIcon(WindowIdentity window, int slot, nint icon);
    bool TryActivate(WindowIdentity window) => false;
    bool TryMinimize(WindowIdentity window) => false;
}

internal sealed class Win32WindowSystem(Action<string> report, string processName = "msrdc") : IWindowSystem
{
    public IReadOnlyList<WindowSnapshot> Enumerate()
    {
        var processes = new Dictionary<int, long>();
        using var current = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                try
                {
                    if (process.SessionId != current.SessionId) continue;
                    processes.Add(process.Id, process.StartTime.ToUniversalTime().Ticks);
                }
                catch (InvalidOperationException) { /* The process exited during enumeration. */ }
                catch (Win32Exception error) { report($"Cannot inspect {processName} PID {process.Id}: {error.Message}"); }
            }
        }
        var windows = new List<WindowSnapshot>();
        nint foreground = NativeMethods.GetForegroundWindow();
        int zOrder = 0;
        NativeMethods.EnumCallback callback = (window, _) =>
        {
            int order = zOrder++;
            NativeMethods.GetWindowThreadProcessId(window, out var pid);
            if (!processes.TryGetValue((int)pid, out var start)) return true;
            var title = new StringBuilder(NativeMethods.GetWindowTextLength(window) + 1);
            NativeMethods.GetWindowText(window, title, title.Capacity);
            if (title.Length == 0) return true;
            uint dpi = NativeMethods.GetDpiForWindow(window);
            if (dpi == 0) return true;
            bool minimized = NativeMethods.IsIconic(window);
            var placement = new NativeMethods.WINDOWPLACEMENT
            {
                length = (uint)Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>()
            };
            bool hasBounds = minimized
                ? NativeMethods.GetWindowPlacement(window, ref placement)
                : false;
            NativeMethods.Rect rect = hasBounds
                ? placement.normalPosition
                : default;
            hasBounds = hasBounds || NativeMethods.GetWindowRect(window, out rect);
            Rectangle? bounds = hasBounds
                ? Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom)
                : null;
            windows.Add(new(new(window, (int)pid, start), title.ToString(),
                NativeMethods.GetSystemMetricsForDpi(49, dpi), NativeMethods.GetSystemMetricsForDpi(11, dpi),
                bounds, NativeMethods.IsWindowVisible(window), minimized,
                window == foreground, order));
            return true;
        };
        if (!NativeMethods.EnumWindows(callback, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot enumerate desktop windows.");
        return windows;
    }

    public bool IsSame(WindowIdentity window)
    {
        if (!NativeMethods.IsWindow(window.Handle)) return false;
        NativeMethods.GetWindowThreadProcessId(window.Handle, out var pid);
        if (pid != window.ProcessId) return false;
        try
        {
            using var process = Process.GetProcessById(window.ProcessId);
            return process.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase) &&
                process.StartTime.ToUniversalTime().Ticks == window.ProcessStartTicks;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    public nint GetIcon(WindowIdentity window, int slot)
    {
        EnsureSame(window);
        return NativeMethods.IconMessage(window.Handle, 0x7F, slot, 0);
    }

    public nint SetIcon(WindowIdentity window, int slot, nint icon)
    {
        EnsureSame(window);
        return NativeMethods.IconMessage(window.Handle, 0x80, slot, icon);
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
            report($"Cannot activate window 0x{window.Handle:X}: {error.Message}");
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
            report($"Cannot minimize window 0x{window.Handle:X}: {error.Message}");
            return false;
        }
    }

    private void EnsureSame(WindowIdentity window)
    {
        if (!IsSame(window)) throw new InvalidOperationException($"Target window no longer exists: 0x{window.Handle:X}");
    }
}

internal sealed class WindowEvents : IDisposable
{
    private readonly NativeMethods.EventCallback callback;
    private readonly List<nint> hooks = [];

    public WindowEvents(Action signal)
    {
        callback = (_, _, window, objectId, childId, _, _) =>
        {
            if (window != 0 && objectId == 0 && childId == 0) signal();
        };
        try
        {
            foreach (uint kind in new uint[] { 0x8000, 0x8001, 0x8002, 0x800C })
            {
                var hook = NativeMethods.SetWinEventHook(kind, kind, 0, callback, 0, 0, 0);
                if (hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot register window event hook.");
                hooks.Add(hook);
            }
        }
        catch (Win32Exception)
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        foreach (var hook in hooks.ToArray())
        {
            if (!NativeMethods.UnhookWinEvent(hook))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot remove window event hook.");
            hooks.Remove(hook);
        }
        GC.KeepAlive(callback);
    }
}
