using System.Runtime.InteropServices;
using RdpIconMonitor.Windows;

namespace RdpIconMonitor.Tests;

public class NativeMethodsTests
{
    [Fact]
    public void WindowPlacementMatchesWin32Layout()
    {
        Assert.Equal(44, Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>());
        Assert.Equal(0, Marshal.OffsetOf<NativeMethods.WINDOWPLACEMENT>(nameof(NativeMethods.WINDOWPLACEMENT.length)));
        Assert.Equal(4, Marshal.OffsetOf<NativeMethods.WINDOWPLACEMENT>(nameof(NativeMethods.WINDOWPLACEMENT.flags)));
        Assert.Equal(8, Marshal.OffsetOf<NativeMethods.WINDOWPLACEMENT>(nameof(NativeMethods.WINDOWPLACEMENT.showCmd)));
        Assert.Equal(12, Marshal.OffsetOf<NativeMethods.WINDOWPLACEMENT>(nameof(NativeMethods.WINDOWPLACEMENT.minPosition)));
        Assert.Equal(20, Marshal.OffsetOf<NativeMethods.WINDOWPLACEMENT>(nameof(NativeMethods.WINDOWPLACEMENT.maxPosition)));
        Assert.Equal(28, Marshal.OffsetOf<NativeMethods.WINDOWPLACEMENT>(nameof(NativeMethods.WINDOWPLACEMENT.normalPosition)));
    }
}
