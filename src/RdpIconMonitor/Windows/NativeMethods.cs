using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace RdpIconMonitor.Windows;

internal static class NativeMethods
{
    internal delegate bool EnumCallback(nint window, nint data);
    internal delegate void EventCallback(nint hook, uint kind, nint window, int objectId, int childId, uint thread, uint time);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool EnumWindows(EnumCallback callback, nint data);
    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(nint window, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextLength(nint window);
    [DllImport("user32.dll")]
    internal static extern bool IsWindow(nint window);
    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")]
    internal static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SendMessageTimeoutW(nint window, uint message, nuint kind, nint value,
        uint flags, uint timeout, out nuint result);
    [DllImport("kernel32.dll")]
    private static extern void SetLastError(uint value);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint CreateIconFromResourceEx(byte[] data, uint length, bool icon,
        uint version, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetWinEventHook(uint min, uint max, nint module, EventCallback callback,
        uint process, uint thread, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnhookWinEvent(nint hook);

    internal static nint IconMessage(nint window, uint message, int slot, nint value)
    {
        SetLastError(0);
        if (SendMessageTimeoutW(window, message, (nuint)slot, value, 0x22, 500, out var result) == 0)
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error == 0 ? 1460 : error,
                $"Icon message 0x{message:X} failed for HWND 0x{window:X}, slot {slot}.");
        }
        return unchecked((nint)result);
    }
}
