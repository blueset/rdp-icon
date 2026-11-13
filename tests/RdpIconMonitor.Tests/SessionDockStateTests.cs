using RdpIconMonitor.Configuration;
using RdpIconMonitor.Dock;
using RdpIconMonitor.Windows;

namespace RdpIconMonitor.Tests;

public class SessionDockStateTests
{
    private static IconConfiguration Configuration() => new([
        new("One", 30, Label: "First"),
        new("Two", 150, Label: "Second"),
        new("Missing", 270)
    ]);

    [Fact]
    public void IncludesOnlyConfiguredWindowsMeaningfullyIntersectingTheScreen()
    {
        var second = Snapshot(2, "Two", new(100, 100, 700, 500), foreground: true, zOrder: 4);
        var state = SessionDockState.Create(
            "screen",
            new(0, 0, 1000, 800),
            new(0, 0, 1000, 760),
            Configuration(),
            [
                Snapshot(1, "one", new(20, 20, 700, 700), zOrder: 2),
                second,
                Snapshot(3, "Missing", new(999, 100, 300, 300)),
                Snapshot(4, "Unconfigured", new(0, 0, 900, 900), foreground: true, zOrder: 0)
            ]);

        Assert.Collection(state.Sessions,
            first =>
            {
                Assert.True(first.Available);
                Assert.False(first.Current);
                Assert.Equal("First", first.Label);
                Assert.Equal(new WindowIdentity(1, 1, 1), first.Identity);
            },
            item =>
            {
                Assert.True(item.Available);
                Assert.True(item.Current);
                Assert.Equal("Second", item.Label);
                Assert.Equal(second.Identity, item.Identity);
            });
    }

    [Fact]
    public void IncludesVisibleMinimizedWindowsButExcludesHiddenWindows()
    {
        var minimized = Snapshot(1, "One", new(100, 100, 300, 300), minimized: true);
        var state = SessionDockState.Create(
            "screen",
            new(0, 0, 1000, 800),
            new(0, 0, 1000, 760),
            Configuration(),
            [
                minimized,
                Snapshot(2, "Two", new(100, 100, 300, 300), visible: false),
                Snapshot(3, "Missing", new(1200, 100, 300, 300))
            ]);

        var session = Assert.Single(state.Sessions);
        Assert.Equal(minimized.Identity, session.Identity);
        Assert.False(session.Current);
    }

    [Fact]
    public void RetainsTheHighestPriorityExactWindowIdentityForActions()
    {
        var background = Snapshot(1, "One", new(0, 0, 500, 500), zOrder: 0);
        var foreground = Snapshot(2, "ONE", new(100, 100, 500, 500), foreground: true, zOrder: 5);
        var state = SessionDockState.Create(
            "screen",
            new(0, 0, 1000, 800),
            new(0, 0, 1000, 760),
            Configuration(),
            [background, foreground]);

        var session = Assert.Single(state.Sessions);
        Assert.Equal(foreground.Identity, session.Identity);
        Assert.True(session.Current);
    }

    private static WindowSnapshot Snapshot(
        int handle,
        string title,
        Rectangle bounds,
        bool visible = true,
        bool minimized = false,
        bool foreground = false,
        int zOrder = 0) =>
        new(
            new(handle, handle, handle),
            title,
            16,
            32,
            bounds,
            visible,
            minimized,
            foreground,
            zOrder);
}
