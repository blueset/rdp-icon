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

    [Fact]
    public void IncludesHostAppsAndSelectsCurrentAcrossRdpAndHostWindows()
    {
        var config = Configuration() with
        {
            HostApps =
            [
                new("Teams", "ms-teams", "Teams", 200, true),
                new("Outlook", "olk", "Mail", 40)
            ]
        };
        var rdp = Snapshot(1, "One", new(20, 20, 500, 500), zOrder: 5);
        var teams = HostSnapshot("Teams", 2, new(600, 100, 300, 300), foreground: true, zOrder: 1);
        var outlook = HostSnapshot("Outlook", 3, new(100, 100, 300, 300), minimized: true, zOrder: 2);

        var state = SessionDockState.Create(
            "screen",
            new(0, 0, 1000, 800),
            new(0, 0, 1000, 760),
            config,
            [rdp],
            [outlook, teams],
            snapshot => snapshot.Configuration.TaskbarAlerts &&
                snapshot.Configuration.Name == "Teams");

        Assert.Collection(state.Sessions,
            first => Assert.False(first.Current),
            teamsItem =>
            {
                Assert.Equal("Teams", teamsItem.Label);
                Assert.True(teamsItem.Current);
                Assert.True(teamsItem.Alerting);
                Assert.Equal(teams.Window.Identity, teamsItem.Identity);
            },
            outlookItem =>
            {
                Assert.Equal("Mail", outlookItem.Label);
                Assert.False(outlookItem.Current);
                Assert.False(outlookItem.Alerting);
            });
    }

    [Fact]
    public void HostAppRowsRequireTheirConfiguredMainWindowSnapshot()
    {
        var config = Configuration() with
        {
            HostApps = [new("Teams", "ms-teams")]
        };
        var state = SessionDockState.Create(
            "screen",
            new(0, 0, 1000, 800),
            new(0, 0, 1000, 760),
            config,
            [],
            [HostSnapshot("Another configuration", 10, new(10, 10, 300, 300))]);

        Assert.Empty(state.Sessions);
    }

    [Fact]
    public void OmitsDisabledHostAppsAndTheirAlerts()
    {
        var disabled = new DockHostApp("Teams", "ms-teams", TaskbarAlerts: true, Enabled: false);
        var config = Configuration() with { HostApps = [disabled] };
        bool checkedAlert = false;

        var state = SessionDockState.Create(
            "screen",
            new(0, 0, 1000, 800),
            new(0, 0, 1000, 760),
            config,
            [],
            [new(disabled, Snapshot(
                10,
                "Teams",
                new(10, 10, 300, 300),
                foreground: true))],
            _ =>
            {
                checkedAlert = true;
                return true;
            });

        Assert.Empty(state.Sessions);
        Assert.False(checkedAlert);
    }

    [Fact]
    public void MinimizedHostWindowOffAllScreensUsesPrimaryMonitorBounds()
    {
        var primary = new Rectangle(0, 0, 1920, 1080);
        var bounds = HostWindowSystem.ResolveScreenBounds(
            new(28, -718, 319, 215),
            minimized: true,
            [primary],
            primary);

        Assert.True(Rectangle.Intersect(primary, bounds).Width >= 32);
        Assert.True(Rectangle.Intersect(primary, bounds).Height >= 32);
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

    private static HostWindowSnapshot HostSnapshot(
        string name,
        int handle,
        Rectangle bounds,
        bool minimized = false,
        bool foreground = false,
        int zOrder = 0) =>
        new(
            new(name, name, name == "Teams" ? "Teams" : name, 200, true),
            Snapshot(handle, "Actual window title", bounds, minimized: minimized,
                foreground: foreground, zOrder: zOrder));
}
