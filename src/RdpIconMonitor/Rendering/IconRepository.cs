using System.ComponentModel;
using System.Runtime.InteropServices;
using RdpIconMonitor.Configuration;
using RdpIconMonitor.Windows;

namespace RdpIconMonitor.Rendering;

public interface IIconRepository
{
    void Prepare(IEnumerable<IconStyle> styles);
    nint Get(IconStyle style, int size);
    bool Owns(nint icon);
    void Trim(IReadOnlySet<IconStyle> styles, IReadOnlySet<nint> protectedHandles);
}

public sealed class IconRepository : IIconRepository, IDisposable
{
    private readonly SvgIconRenderer renderer = new();
    private readonly Dictionary<(IconStyle Style, int Size), RenderedIcon> images = [];
    private readonly Dictionary<(IconStyle Style, int Size), nint> handles = [];
    public int HandleCount => handles.Count;

    public void Prepare(IEnumerable<IconStyle> styles)
    {
        var candidate = new Dictionary<(IconStyle, int), RenderedIcon>();
        foreach (var style in styles.Distinct())
            foreach (int size in SvgIconRenderer.Sizes)
                if (!images.ContainsKey((style, size)))
                    candidate.Add((style, size), renderer.Render(style, size));
        foreach (var pair in candidate) images.Add(pair.Key, pair.Value);
    }

    public nint Get(IconStyle style, int size)
    {
        var key = (style, size);
        if (handles.TryGetValue(key, out var handle)) return handle;
        if (!images.TryGetValue(key, out var image))
            images.Add(key, image = renderer.Render(style, size));
        handle = NativeMethods.CreateIconFromResourceEx(image.Png, (uint)image.Png.Length, true, 0x30000, size, size, 0);
        if (handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the generated window icon.");
        handles.Add(key, handle);
        return handle;
    }

    public bool Owns(nint icon) => icon != 0 && handles.ContainsValue(icon);

    public void Trim(IReadOnlySet<IconStyle> styles, IReadOnlySet<nint> protectedHandles)
    {
        foreach (var pair in handles.ToArray())
        {
            if (protectedHandles.Contains(pair.Value)) continue;
            if (!NativeMethods.DestroyIcon(pair.Value))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not destroy owned HICON 0x{pair.Value:X}.");
            handles.Remove(pair.Key);
        }
        foreach (var key in images.Keys.ToArray())
            if (!styles.Contains(key.Style) && !handles.ContainsKey(key)) images.Remove(key);
    }

    public void Dispose()
    {
        Trim(new HashSet<IconStyle>(), new HashSet<nint>());
        renderer.Dispose();
    }
}
