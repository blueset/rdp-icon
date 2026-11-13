using RdpIconMonitor.Configuration;
using RdpIconMonitor.Windows;

namespace RdpIconMonitor.Dock;

internal sealed record SessionDockItemState(
    string Title,
    string Label,
    double? Hue,
    bool Available,
    bool Current,
    WindowIdentity Identity);

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
        IReadOnlyList<WindowSnapshot> snapshots)
    {
        var matching = snapshots
            .Where(snapshot => snapshot.Visible &&
                snapshot.Bounds is not null &&
                HasMeaningfulIntersection(bounds, snapshot.Bounds.Value))
            .ToArray();
        var current = matching
            .Where(snapshot => !snapshot.Minimized &&
                config.Instances.Any(rule =>
                    rule.Name.Equals(snapshot.Title, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(snapshot => snapshot.Foreground)
            .ThenBy(snapshot => snapshot.ZOrder)
            .FirstOrDefault();
        var sessions = config.Instances
            .Select(rule => new
            {
                Rule = rule,
                Window = matching
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
        return new(deviceName, bounds, workingArea, sessions);
    }

    private static bool HasMeaningfulIntersection(Rectangle screen, Rectangle window)
    {
        var intersection = Rectangle.Intersect(screen, window);
        return intersection.Width >= 32 && intersection.Height >= 32;
    }
}
