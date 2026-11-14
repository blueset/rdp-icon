using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RdpIconMonitor.Windows;

internal sealed class ShellHookWindow : Form
{
    private const int HshellFlash = 0x8006;
    private const int HshellWindowActivated = 0x0004;
    private readonly uint message;
    private readonly Action<nint> flashed;
    private readonly Action<nint> activated;
    private readonly bool registered;

    public ShellHookWindow(Action<nint> flashed, Action<nint> activated, Action<string> report)
    {
        this.flashed = flashed;
        this.activated = activated;
        ShowInTaskbar = false;
        WindowState = FormWindowState.Minimized;
        _ = Handle;
        message = NativeMethods.RegisterWindowMessage("SHELLHOOK");
        if (message == 0)
        {
            int error = Marshal.GetLastWin32Error();
            report($"Taskbar flash notifications unavailable: {new Win32Exception(error).Message}");
            return;
        }
        registered = NativeMethods.RegisterShellHookWindow(Handle);
        if (!registered)
        {
            int error = Marshal.GetLastWin32Error();
            report($"Taskbar flash notifications unavailable: {new Win32Exception(error).Message}");
        }
    }

    protected override void SetVisibleCore(bool value)
    {
        base.SetVisibleCore(false);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == this.message)
        {
            nint window = message.LParam;
            if (message.WParam == HshellFlash && window != 0)
                flashed(window);
            else if (message.WParam == HshellWindowActivated && window != 0)
                activated(window);
        }
        base.WndProc(ref message);
    }

    protected override void Dispose(bool disposing)
    {
        if (registered && IsHandleCreated)
            NativeMethods.DeregisterShellHookWindow(Handle);
        base.Dispose(disposing);
    }
}
