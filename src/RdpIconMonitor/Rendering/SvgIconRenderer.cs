using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using RdpIconMonitor.Configuration;
using SkiaSharp;
using Svg.Skia;

namespace RdpIconMonitor.Rendering;

public sealed record RenderedIcon(int Size, byte[] Png, string Svg, TextGeometry? Text);

public sealed class SvgIconRenderer : IDisposable
{
    public static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 256];
    private static readonly XNamespace Ns = "http://www.w3.org/2000/svg";
    private readonly Dictionary<int, XDocument> templates = [];
    private SegoeText? text;

    public SvgIconRenderer()
    {
        foreach (int size in Sizes)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Icons._{size}.svg")
                ?? throw new InvalidDataException($"Missing SVG template for size {size}.");
            var document = XDocument.Load(stream);
            Gradient(document, "paint6_linear_");
            Gradient(document, "paint7_linear_");
            PathFor(document, "paint6_linear_");
            PathFor(document, "paint7_linear_");
            templates.Add(size, document);
        }
    }

    public RenderedIcon Render(IconStyle style, int size)
    {
        if (size < 1 || size > 1024) throw new ArgumentOutOfRangeException(nameof(size));
        int sourceSize = Sizes.FirstOrDefault(value => value >= size, 256);
        var document = new XDocument(templates[sourceSize]);
        if (style.Hue is double hue)
        {
            foreach (var gradient in document.Descendants(Ns + "linearGradient")
                .Where(element => !Id(element).StartsWith("paint7_linear_", StringComparison.Ordinal)))
            {
                foreach (var stop in gradient.Elements(Ns + "stop"))
                    stop.SetAttributeValue("stop-color", OklchColor.Rotate(
                        (string?)stop.Attribute("stop-color") ?? throw new InvalidDataException("Missing stop color."), hue));
            }
        }
        TextGeometry? geometry = null;
        if (style.SmallText is not null || style.LargeText is not null)
        {
            var cloud = PathFor(document, "paint7_linear_");
            using var cloudPath = ParsePath(cloud);
            using var middle = ParsePath(PathFor(document, "paint6_linear_"));
            var layout = (text ??= new SegoeText()).Layout(style.SmallText, style.LargeText, middle.TightBounds);
            using var glyphs = layout.Path;
            geometry = layout.Geometry;
            RemapGradient(Gradient(document, "paint7_linear_"), cloudPath.TightBounds, glyphs.TightBounds);
            var filter = document.Descendants(Ns + "filter").Single(
                element => Id(element).StartsWith("filter2_dd_", StringComparison.Ordinal));
            ExpandFilter(filter, cloudPath.TightBounds, glyphs.TightBounds);
            cloud.ReplaceWith(new XElement(Ns + "path",
                new XAttribute("d", glyphs.ToSvgPathData()), new XAttribute("fill", cloud.Attribute("fill")!.Value)));
        }
        string svgText = document.ToString(SaveOptions.DisableFormatting);
        using var svg = new SKSvg();
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(svgText));
        var picture = svg.Load(stream) ?? throw new InvalidDataException("SVG rasterizer returned no picture.");
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul))
            ?? throw new InvalidDataException("Could not allocate icon surface.");
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.Scale((float)size / sourceSize);
        surface.Canvas.DrawPicture(picture);
        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return new(size, png.ToArray(), svgText, geometry);
    }

    public static byte[] EncodeIco(IReadOnlyList<RenderedIcon> images)
    {
        if (images.Count is < 1 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(images));
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Count);
        uint offset = checked((uint)(6 + images.Count * 16));
        foreach (var image in images)
        {
            if (image.Size > 256) throw new InvalidDataException("ICO frames cannot exceed 256 pixels.");
            writer.Write((byte)(image.Size == 256 ? 0 : image.Size));
            writer.Write((byte)(image.Size == 256 ? 0 : image.Size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)image.Png.Length);
            writer.Write(offset);
            offset += (uint)image.Png.Length;
        }
        foreach (var image in images) writer.Write(image.Png);
        return stream.ToArray();
    }

    private static XElement Gradient(XDocument doc, string prefix) =>
        doc.Descendants(Ns + "linearGradient").Single(element => Id(element).StartsWith(prefix, StringComparison.Ordinal));
    private static XElement PathFor(XDocument doc, string prefix) =>
        doc.Descendants(Ns + "path").Single(element =>
            ((string?)element.Attribute("fill"))?.StartsWith($"url(#{prefix}", StringComparison.Ordinal) == true);
    private static SKPath ParsePath(XElement element) =>
        SKPath.ParseSvgPathData((string?)element.Attribute("d")) ?? throw new InvalidDataException("Invalid SVG path.");
    private static string Id(XElement element) => (string?)element.Attribute("id") ?? "";
    private static float Number(XElement element, string name) =>
        float.Parse((string?)element.Attribute(name) ?? throw new InvalidDataException($"Missing {name}."), CultureInfo.InvariantCulture);
    private static void Set(XElement element, string name, float value) =>
        element.SetAttributeValue(name, value.ToString("G9", CultureInfo.InvariantCulture));

    private static void RemapGradient(XElement gradient, SKRect before, SKRect after)
    {
        foreach (string name in new[] { "x1", "x2" })
            Set(gradient, name, after.Left + (Number(gradient, name) - before.Left) / before.Width * after.Width);
        foreach (string name in new[] { "y1", "y2" })
            Set(gradient, name, after.Top + (Number(gradient, name) - before.Top) / before.Height * after.Height);
    }

    private static void ExpandFilter(XElement filter, SKRect before, SKRect after)
    {
        float x = Number(filter, "x"), y = Number(filter, "y");
        float rightMargin = x + Number(filter, "width") - before.Right;
        float bottomMargin = y + Number(filter, "height") - before.Bottom;
        float leftMargin = before.Left - x, topMargin = before.Top - y;
        Set(filter, "x", after.Left - leftMargin);
        Set(filter, "y", after.Top - topMargin);
        Set(filter, "width", after.Width + leftMargin + rightMargin);
        Set(filter, "height", after.Height + topMargin + bottomMargin);
    }

    public void Dispose() => text?.Dispose();
}
