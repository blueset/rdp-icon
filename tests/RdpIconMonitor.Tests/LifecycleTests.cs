using System.ComponentModel;
using RdpIconMonitor.Configuration;
using RdpIconMonitor.Rendering;
using RdpIconMonitor.Windows;

namespace RdpIconMonitor.Tests;

public class LifecycleTests
{
    private sealed class FakeWindow(nint handle, string title)
    {
        public WindowIdentity Id = new(handle, 1, 10);
        public string Title = title;
        public bool Exists = true;
        public int SmallSize = 16;
        public int LargeSize = 32;
        public readonly nint[] Icons = [101, 102];
    }

    private sealed class FakeWindows : IWindowSystem
    {
        public readonly List<FakeWindow> Items = [];
        public int? FailSlot;
        public bool CompleteBeforeFailure;
        public IReadOnlyList<WindowSnapshot> Enumerate() => Items.Where(item => item.Exists)
            .Select(item => new WindowSnapshot(item.Id, item.Title, item.SmallSize, item.LargeSize)).ToArray();
        public bool IsSame(WindowIdentity window) => Items.Any(item => item.Exists && item.Id == window);
        public nint GetIcon(WindowIdentity window, int slot) => Items.Single(item => item.Exists && item.Id == window).Icons[slot];
        public nint SetIcon(WindowIdentity window, int slot, nint icon)
        {
            var item = Items.Single(item => item.Exists && item.Id == window);
            var previous = item.Icons[slot];
            if (FailSlot == slot)
            {
                if (CompleteBeforeFailure) item.Icons[slot] = icon;
                throw new Win32Exception(1460);
            }
            item.Icons[slot] = icon;
            return previous;
        }
    }

    private sealed class FakeIcons : IIconRepository
    {
        private int next = 1000;
        public readonly Dictionary<(IconStyle, int), nint> Values = [];
        public readonly HashSet<nint> Destroyed = [];
        public void Prepare(IEnumerable<IconStyle> styles) { }
        public nint Get(IconStyle style, int size)
        {
            if (!Values.TryGetValue((style, size), out var icon))
                Values.Add((style, size), icon = ++next);
            return icon;
        }
        public bool Owns(nint icon) => Values.ContainsValue(icon);
        public void Trim(IReadOnlySet<IconStyle> styles, IReadOnlySet<nint> protectedHandles)
        {
            foreach (var pair in Values.ToArray())
                if (!protectedHandles.Contains(pair.Value))
                {
                    Destroyed.Add(pair.Value);
                    Values.Remove(pair.Key);
                }
        }
    }

    private static IconConfiguration Config(string name = "SERVER", string text = "A") =>
        new([new(name, 100, null, text)]);

    [Fact]
    public void ExactCaseInsensitiveMatchesApplyToAllAndRestoreAfterRetitle()
    {
        var windows = new FakeWindows();
        windows.Items.AddRange([new(1, "server"), new(2, "Server"), new(3, "server-extra")]);
        var icons = new FakeIcons();
        var errors = new List<string>();
        var controller = new WindowIconController(windows, icons, errors.Add);
        controller.Reconcile(Config(), false);
        Assert.Equal(2, controller.ManagedCount);
        Assert.True(icons.Owns(windows.Items[0].Icons[0]));
        Assert.Equal((nint)101, windows.Items[2].Icons[0]);
        windows.Items[0].Title = "other";
        controller.Reconcile(Config(), false);
        Assert.Equal((nint)101, windows.Items[0].Icons[0]);
        Assert.Equal((nint)102, windows.Items[0].Icons[1]);
        Assert.Equal(1, controller.ManagedCount);
        Assert.True(controller.RestoreAll());
        Assert.Empty(icons.Values);
        Assert.Empty(errors);
    }

    [Fact]
    public void StyleAndMatchingRuleTransitionsPreserveOriginals()
    {
        var windows = new FakeWindows();
        windows.Items.Add(new(1, "server"));
        var icons = new FakeIcons();
        var controller = new WindowIconController(windows, icons, Assert.Fail);
        controller.Reconcile(Config(), false);
        var first = windows.Items[0].Icons[0];
        controller.Reconcile(Config(text: "B"), false);
        Assert.NotEqual(first, windows.Items[0].Icons[0]);
        Assert.Contains(first, icons.Destroyed);
        windows.Items[0].Title = "different";
        controller.Reconcile(Config("different", "C"), false);
        Assert.True(controller.RestoreAll());
        Assert.Equal(new nint[] { 101, 102 }, windows.Items[0].Icons);
    }

    [Fact]
    public void ClientResetUpdatesBaselineAndDpiChangeSelectsNewSize()
    {
        var windows = new FakeWindows();
        windows.Items.Add(new(1, "server"));
        var icons = new FakeIcons();
        var controller = new WindowIconController(windows, icons, Assert.Fail);
        controller.Reconcile(Config(), false);
        windows.Items[0].Icons[0] = 303;
        windows.Items[0].SmallSize = 24;
        windows.Items[0].LargeSize = 48;
        controller.Reconcile(Config(), false);
        Assert.Contains((Config().Instances[0].Style, 24), icons.Values.Keys);
        Assert.True(controller.RestoreAll());
        Assert.Equal(new nint[] { 303, 102 }, windows.Items[0].Icons);
    }

    [Fact]
    public void PauseAndRuleRemovalRestoreAndExternalChangesAreNotOverwritten()
    {
        var windows = new FakeWindows();
        windows.Items.Add(new(1, "server"));
        var icons = new FakeIcons();
        var controller = new WindowIconController(windows, icons, Assert.Fail);
        controller.Reconcile(Config(), false);
        windows.Items[0].Icons[1] = 909;
        controller.Reconcile(Config(), true);
        Assert.Equal(new nint[] { 101, 909 }, windows.Items[0].Icons);
        controller.Reconcile(Config(), false);
        controller.Reconcile(new([]), false);
        Assert.Equal(new nint[] { 101, 909 }, windows.Items[0].Icons);
        Assert.Empty(icons.Values);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartialMessageFailuresProtectHandlesAndPermitRestoration(bool completed)
    {
        var windows = new FakeWindows { FailSlot = 1, CompleteBeforeFailure = completed };
        windows.Items.Add(new(1, "server"));
        var icons = new FakeIcons();
        var errors = new List<string>();
        var controller = new WindowIconController(windows, icons, errors.Add);
        controller.Reconcile(Config(), false);
        Assert.NotEmpty(errors);
        Assert.Equal(2, icons.Values.Count);
        Assert.Equal(2, controller.ProtectedHandles.Count);
        windows.FailSlot = null;
        Assert.True(controller.RestoreAll());
        Assert.Equal(new nint[] { 101, 102 }, windows.Items[0].Icons);
        Assert.Empty(icons.Values);
    }

    [Fact]
    public void RestorationFailureKeepsReferencedHandlesUntilRetry()
    {
        var windows = new FakeWindows();
        windows.Items.Add(new(1, "server"));
        var icons = new FakeIcons();
        var errors = new List<string>();
        var controller = new WindowIconController(windows, icons, errors.Add);
        controller.Reconcile(Config(), false);
        windows.FailSlot = 1;
        Assert.False(controller.RestoreAll());
        Assert.True(icons.Owns(windows.Items[0].Icons[1]));
        windows.FailSlot = null;
        Assert.True(controller.RestoreAll());
        Assert.Empty(icons.Values);
    }

    [Fact]
    public void ClosedAndRecycledWindowsDoNotReceiveStaleRestoration()
    {
        var windows = new FakeWindows();
        windows.Items.Add(new(1, "server"));
        var icons = new FakeIcons();
        var controller = new WindowIconController(windows, icons, Assert.Fail);
        controller.Reconcile(Config(), false);
        windows.Items[0].Exists = false;
        windows.Items.Add(new(1, "unmatched") { Id = new(1, 1, 20) });
        controller.Reconcile(Config(), false);
        Assert.Equal(new nint[] { 101, 102 }, windows.Items[1].Icons);
        Assert.Empty(icons.Values);
        Assert.Equal(0, controller.ManagedCount);
    }

    [Fact]
    public void RepeatedOpenCloseAndStyleChangesDoNotAccumulateHandles()
    {
        var windows = new FakeWindows();
        var icons = new FakeIcons();
        var controller = new WindowIconController(windows, icons, Assert.Fail);
        for (int i = 1; i <= 40; i++)
        {
            var item = new FakeWindow(i, "server");
            windows.Items.Add(item);
            controller.Reconcile(Config(text: i.ToString()), false);
            Assert.Equal(2, icons.Values.Count);
            item.Exists = false;
            controller.Reconcile(Config(text: i.ToString()), false);
            Assert.Empty(icons.Values);
        }
        Assert.Equal(0, controller.ManagedCount);
    }
}
