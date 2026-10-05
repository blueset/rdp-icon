using System.ComponentModel;
using RdpIconMonitor.Configuration;
using RdpIconMonitor.Rendering;

namespace RdpIconMonitor.Windows;

public sealed class WindowIconController(IWindowSystem windows, IIconRepository icons, Action<string> report)
{
    private sealed class Slot
    {
        public nint Original;
        public bool HasOriginal;
        public bool InFlight;
        public readonly HashSet<nint> Possible = [];
    }

    private sealed class Assignment(WindowIdentity identity, string title)
    {
        public WindowIdentity Identity = identity;
        public string Title = title;
        public readonly Slot[] Slots = [new(), new()];
    }

    private readonly Dictionary<WindowIdentity, Assignment> managed = [];
    public int ManagedCount => managed.Count;
    public IReadOnlySet<nint> ProtectedHandles => managed.Values.SelectMany(
        assignment => assignment.Slots.SelectMany(slot => slot.Possible)).ToHashSet();

    public void Reconcile(IconConfiguration config, bool paused)
    {
        var rules = config.Instances.ToDictionary(rule => rule.Name, StringComparer.OrdinalIgnoreCase);
        var snapshots = windows.Enumerate();
        var desired = snapshots.Where(window => !paused && rules.ContainsKey(window.Title))
            .ToDictionary(window => window.Identity);
        foreach (var assignment in managed.Values.ToArray())
        {
            if (desired.ContainsKey(assignment.Identity)) continue;
            Attempt(assignment, () => Restore(assignment));
        }
        foreach (var snapshot in desired.Values)
        {
            if (!managed.TryGetValue(snapshot.Identity, out var assignment))
            {
                assignment = new(snapshot.Identity, snapshot.Title);
                managed.Add(snapshot.Identity, assignment);
            }
            assignment.Title = snapshot.Title;
            Attempt(assignment, () =>
            {
                if (!windows.IsSame(assignment.Identity))
                {
                    managed.Remove(assignment.Identity);
                    return;
                }
                var style = rules[snapshot.Title].Style;
                Apply(assignment, 0, icons.Get(style, snapshot.SmallSize));
                Apply(assignment, 1, icons.Get(style, snapshot.LargeSize));
            });
        }
        icons.Trim(config.Instances.Select(rule => rule.Style).ToHashSet(), ProtectedHandles);
    }

    public bool RestoreAll()
    {
        foreach (var assignment in managed.Values.ToArray())
            Attempt(assignment, () => Restore(assignment));
        icons.Trim(new HashSet<IconStyle>(), ProtectedHandles);
        return managed.Count == 0;
    }

    private void Apply(Assignment assignment, int index, nint desired)
    {
        var slot = assignment.Slots[index];
        nint current = windows.GetIcon(assignment.Identity, index);
        if (current == desired && !slot.InFlight) return;
        if (!icons.Owns(current))
        {
            slot.Original = current;
            slot.HasOriginal = true;
        }
        if (!slot.HasOriginal)
            throw new InvalidOperationException("Cannot replace an owned icon without a restoration baseline.");
        // A timed-out message can still complete: protect the proposed handle before sending it.
        slot.Possible.Add(desired);
        slot.InFlight = true;
        nint previous = windows.SetIcon(assignment.Identity, index, desired);
        if (!icons.Owns(previous)) slot.Original = previous;
        if (windows.GetIcon(assignment.Identity, index) != desired)
            throw new InvalidOperationException($"The client rejected/reset icon slot {index}.");
        slot.Possible.Clear();
        slot.Possible.Add(desired);
        slot.InFlight = false;
    }

    private void Restore(Assignment assignment)
    {
        if (!windows.IsSame(assignment.Identity))
        {
            managed.Remove(assignment.Identity);
            return;
        }
        for (int index = 0; index < 2; index++)
        {
            var slot = assignment.Slots[index];
            if (!slot.HasOriginal) continue;
            nint current = windows.GetIcon(assignment.Identity, index);
            if (icons.Owns(current) || slot.InFlight)
            {
                // Complete a synchronous write even after an ambiguous timeout. Preserve an external replacement.
                nint target = icons.Owns(current) ? slot.Original : current;
                nint previous = windows.SetIcon(assignment.Identity, index, target);
                if (previous != current && !icons.Owns(previous))
                {
                    target = previous;
                    windows.SetIcon(assignment.Identity, index, target);
                }
                nint restored = windows.GetIcon(assignment.Identity, index);
                if (target != 0 ? restored != target : icons.Owns(restored))
                    throw new InvalidOperationException($"Could not restore icon slot {index}.");
            }
            slot.Possible.Clear();
            slot.HasOriginal = false;
            slot.InFlight = false;
        }
        managed.Remove(assignment.Identity);
    }

    private void Attempt(Assignment assignment, Action action)
    {
        try { action(); }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or IOException or InvalidDataException or ArgumentException)
        {
            report($"{assignment.Title} (PID {assignment.Identity.ProcessId}, HWND 0x{assignment.Identity.Handle:X}): {error.Message}");
        }
    }
}
