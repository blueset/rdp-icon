using RdpIconMonitor.Configuration;
using RdpIconMonitor.Windows;

namespace RdpIconMonitor.Dock;

internal sealed record SessionDockItemState(
    string Title,
    string Label,
    double? Hue,
    bool Available,
    bool Current,
    WindowIdentity Identity,
    bool Alerting = false);

internal sealed record SessionDockScreenState(
    string DeviceName,
    Rectangle Bounds,
    Rectangle WorkingArea,
    IReadOnlyList<SessionDockItemState> Sessions);

internal static class SessionDockState
{
    public static SessionDockScreenState Create(
        string deviceName,
        Rectangle bounds,
        Rectangle workingArea,
        IconConfiguration config,
        IReadOnlyList<WindowSnapshot> snapshots,
        IReadOnlyList<HostWindowSnapshot>? hostSnapshots = null,
        Func<HostWindowSnapshot, bool>? isAlerting = null)
    {
        var rdpWindows = snapshots
            .Where(snapshot => snapshot.Visible &&
                snapshot.Bounds is not null &&
                HasMeaningfulIntersection(bounds, snapshot.Bounds.Value))
            .ToArray();
        var hostWindows = (hostSnapshots ?? [])
            .Where(snapshot => snapshot.Window.Visible &&
                snapshot.Window.Bounds is not null &&
                HasMeaningfulIntersection(bounds, snapshot.Window.Bounds.Value))
            .ToArray();
        var current = rdpWindows
            .Where(snapshot => !snapshot.Minimized &&
                config.Instances.Any(rule =>
                    rule.Name.Equals(snapshot.Title, StringComparison.OrdinalIgnoreCase)))
            .Concat(hostWindows
                .Where(snapshot => snapshot.Configuration.Enabled &&
                    !snapshot.Window.Minimized &&
                    (config.HostApps ?? []).Any(app =>
                        app.Enabled &&
                        app.Name.Equals(snapshot.Configuration.Name, StringComparison.OrdinalIgnoreCase)))
                .Select(snapshot => snapshot.Window))
            .OrderByDescending(snapshot => snapshot.Foreground)
            .ThenBy(snapshot => snapshot.ZOrder)
            .FirstOrDefault();
        var rdpSessions = config.Instances
            .Select(rule => new
            {
                Rule = rule,
                Window = rdpWindows
                    .Where(snapshot =>
                        snapshot.Title.Equals(rule.Name, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(snapshot => snapshot.Foreground)
                    .ThenBy(snapshot => snapshot.Minimized)
                    .ThenBy(snapshot => snapshot.ZOrder)
                    .FirstOrDefault()
            })
            .Where(match => match.Window is not null)
            .Select(match => new SessionDockItemState(
                match.Rule.Name,
                match.Rule.Label ?? match.Rule.Name,
                match.Rule.Hue,
                true,
                current?.Identity == match.Window!.Identity,
                match.Window.Identity))
            .ToArray();
        var appSessions = (config.HostApps ?? [])
            .Where(app => app.Enabled)
            .Select(app => new
            {
                App = app,
                Host = hostWindows
                    .Where(snapshot => snapshot.Configuration.Enabled &&
                        snapshot.Configuration.Name.Equals(
                            app.Name, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(snapshot => snapshot.Window.Foreground)
                    .ThenBy(snapshot => snapshot.Window.Minimized)
                    .ThenBy(snapshot => snapshot.Window.ZOrder)
                    .FirstOrDefault()
            })
            .Where(match => match.Host is not null)
            .Select(match => new SessionDockItemState(
                match.App.Name,
                match.App.Label ?? match.App.Name,
                match.App.Hue,
                true,
                current?.Identity == match.Host!.Window.Identity,
                match.Host.Window.Identity,
                isAlerting?.Invoke(match.Host) == true))
            .ToArray();
        return new(deviceName, bounds, workingArea, [.. rdpSessions, .. appSessions]);
    }

    private static bool HasMeaningfulIntersection(Rectangle screen, Rectangle window)
    {
        var intersection = Rectangle.Intersect(screen, window);
        return intersection.Width >= 32 && intersection.Height >= 32;
    }
}
