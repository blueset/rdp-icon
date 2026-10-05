using RdpIconMonitor.Configuration;
using RdpIconMonitor.Windows;

namespace RdpIconMonitor.Tests;

public class ReloadTests
{
    private sealed class TestWindows : IWindowSystem
    {
        private static readonly WindowIdentity Id = new(1, 1, 1);
        public readonly nint[] Icons = [SystemIcons.Information.Handle, SystemIcons.Information.Handle];
        public IReadOnlyList<WindowSnapshot> Enumerate() => [new(Id, "test", 16, 32)];
        public bool IsSame(WindowIdentity identity) => identity == Id;
        public nint GetIcon(WindowIdentity identity, int slot) => Icons[slot];
        public nint SetIcon(WindowIdentity identity, int slot, nint icon)
        {
            nint previous = Icons[slot];
            Icons[slot] = icon;
            return previous;
        }
    }

    [Fact]
    public void InvalidReloadKeepsPreviousRulesAndValidRemovalRestoresIcons()
    {
        string folder = Path.Combine(Path.GetTempPath(), "RdpIconReload-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "instances.json");
        var windows = new TestWindows();
        var errors = new List<string>();
        using var engine = new MonitorEngine(path, errors.Add, windows);
        try
        {
            File.WriteAllText(path, """{"instances":[{"name":"test","hue":150,"smallText":"W","largeText":"1"}]}""");
            engine.Run(false, false, true);
            Assert.True(engine.Loaded);
            Assert.Equal(1, engine.ManagedCount);
            var originalOverride = windows.Icons.ToArray();
            File.WriteAllText(path, "{ invalid");
            engine.Run(false, false, false);
            Assert.NotNull(engine.Error);
            Assert.Equal(originalOverride, windows.Icons);
            Assert.Equal(1, engine.ManagedCount);
            File.WriteAllText(path, """{"instances":[{"name":"test","smallText":"W","largeText":"\udbff\udfff"}]}""");
            engine.Run(false, false, false);
            Assert.Contains("previous rules remain active", engine.Error);
            Assert.Equal(originalOverride, windows.Icons);
            File.WriteAllText(path, """{"instances":[]}""");
            engine.Run(false, false, false);
            Assert.Null(engine.Error);
            Assert.Equal(0, engine.ManagedCount);
            Assert.Equal(new[] { SystemIcons.Information.Handle, SystemIcons.Information.Handle }, windows.Icons);
            Assert.NotEmpty(errors);
        }
        finally
        {
            engine.Run(true, true, false);
            File.Delete(path);
            Directory.Delete(folder);
        }
    }

    [Fact]
    public void WatcherDetectsSaveByAtomicReplacement()
    {
        string folder = Path.Combine(Path.GetTempPath(), "RdpIconWatch-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "instances.json");
        string replacement = Path.Combine(folder, "replacement.json");
        File.WriteAllText(path, """{"instances":[]}""");
        using var signaled = new ManualResetEventSlim();
        try
        {
            using (var watcher = new ConfigurationWatcher(path, signaled.Set, Assert.Fail))
            {
                File.WriteAllText(replacement, """{"instances":[{"name":"test"}]}""");
                File.Move(replacement, path, true);
                Assert.True(signaled.Wait(TimeSpan.FromSeconds(10)));
                Assert.Single(IconConfiguration.Parse(File.ReadAllText(path)).Instances);
            }
        }
        finally
        {
            File.Delete(path);
            File.Delete(replacement);
            Directory.Delete(folder);
        }
    }
}
