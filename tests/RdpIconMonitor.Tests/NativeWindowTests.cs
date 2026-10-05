using System.Diagnostics;
using RdpIconMonitor.Configuration;
using RdpIconMonitor.Rendering;
using RdpIconMonitor.Windows;

namespace RdpIconMonitor.Tests;

public class NativeWindowTests
{
    [Fact]
    public void RealWin32WindowsAcceptGeneratedPngIconsAndRestoreAfterRetitleAndReset()
    {
        var forms = new List<Form>();
        using var ready = new ManualResetEventSlim();
        string title = "RdpIconTest-" + Guid.NewGuid();
        var thread = new Thread(() =>
        {
            for (int i = 0; i < 4; i++)
            {
                var form = new Form { Text = title };
                _ = form.Handle;
                NativeMethods.IconMessage(form.Handle, 0x80, 0, SystemIcons.Information.Handle);
                NativeMethods.IconMessage(form.Handle, 0x80, 1, SystemIcons.Information.Handle);
                forms.Add(form);
            }
            ready.Set();
            Application.Run();
            foreach (var form in forms) form.Dispose();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(ready.Wait(TimeSpan.FromSeconds(15)));
        using var repository = new IconRepository();
        using var process = Process.GetCurrentProcess();
        var windows = new Win32WindowSystem(Assert.Fail, process.ProcessName);
        var controller = new WindowIconController(windows, repository, Assert.Fail);
        var config = new IconConfiguration([new(title, 250, "W", "2")]);
        try
        {
            repository.Prepare(config.Instances.Select(rule => rule.Style));
            controller.Reconcile(config, false);
            Assert.Equal(4, controller.ManagedCount);
            var first = windows.Enumerate().Single(window => window.Identity.Handle == forms[0].Handle);
            Assert.True(repository.Owns(windows.GetIcon(first.Identity, 0)));
            forms[0].Invoke(() => forms[0].Text = title + "-unmatched");
            controller.Reconcile(config, false);
            Assert.Equal(3, controller.ManagedCount);
            Assert.Equal(SystemIcons.Information.Handle, windows.GetIcon(first.Identity, 0));
            var second = windows.Enumerate().Single(window => window.Identity.Handle == forms[1].Handle);
            windows.SetIcon(second.Identity, 0, SystemIcons.Warning.Handle);
            controller.Reconcile(config, false);
            Assert.True(repository.Owns(windows.GetIcon(second.Identity, 0)));
            Assert.True(controller.RestoreAll());
            Assert.Equal(SystemIcons.Warning.Handle, windows.GetIcon(second.Identity, 0));
            Assert.Equal(SystemIcons.Information.Handle, windows.GetIcon(second.Identity, 1));
            Assert.Equal(0, repository.HandleCount);
        }
        finally
        {
            controller.RestoreAll();
            forms[0].Invoke(Application.ExitThread);
            Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        }
    }
}
