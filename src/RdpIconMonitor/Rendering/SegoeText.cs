using HarfBuzzSharp;
using SkiaSharp;

namespace RdpIconMonitor.Rendering;

public sealed record TextGeometry(SKRect Bounds, SKRect ContentBounds, float Baseline,
    float SmallSize, float LargeSize, float SmallAdvance, float LargeOrigin);

public sealed class SegoeText : IDisposable
{
    private readonly SKTypeface typeface;
    private readonly Blob blob;
    private readonly Face face;
    private readonly HarfBuzzSharp.Font shaper;
    public string FamilyName => typeface.FamilyName;
    public SKFontVariationPositionCoordinate[] Variations => typeface.VariationDesignPosition;

    public SegoeText()
    {
        string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "SegUIVar.ttf");
        using var source = SKTypeface.FromFile(file)
            ?? throw new InvalidDataException($"Segoe UI Variable could not be loaded from {file}.");
        var weight = SKFourByteTag.Parse("wght");
        var axis = source.VariationDesignParameters.SingleOrDefault(a => a.Tag == weight);
        if (!source.FamilyName.StartsWith("Segoe UI Variable", StringComparison.OrdinalIgnoreCase) ||
            axis.Min > 700 || axis.Max < 700)
            throw new InvalidDataException("The installed font does not support Segoe UI Variable Bold.");
        var coordinates = source.VariationDesignPosition.Where(c => c.Axis != weight)
            .Append(new SKFontVariationPositionCoordinate { Axis = weight, Value = 700 }).ToArray();
        typeface = source.Clone(coordinates)
            ?? throw new InvalidDataException("Could not select Segoe UI Variable Bold.");
        if (!typeface.VariationDesignPosition.Any(c => c.Axis == weight && c.Value == 700))
        {
            typeface.Dispose();
            throw new InvalidDataException("The renderer did not retain the Bold weight variation.");
        }
        blob = Blob.FromFile(file);
        face = new Face(blob, 0);
        shaper = new HarfBuzzSharp.Font(face);
        shaper.SetFunctionsOpenType();
        shaper.SetScale((int)face.UnitsPerEm, (int)face.UnitsPerEm);
        shaper.SetVariations(typeface.VariationDesignPosition.Select(c =>
            new Variation { Tag = c.Axis, Value = c.Value }).ToArray());
    }

    public (SKPath Path, TextGeometry Geometry) Layout(string? small, string? large, SKRect rectangle)
    {
        using var smallFont = new SKFont(typeface, 72);
        using var largeFont = new SKFont(typeface, 100);
        using var builder = new SKPathBuilder();
        float advance = Append(builder, small, smallFont, 0);
        Append(builder, large, largeFont, advance);
        var path = builder.Detach();
        try
        {
            var bounds = path.TightBounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                throw new InvalidDataException("Text has no drawable glyph outlines.");
            float padding = rectangle.Width * .08f;
            var content = new SKRect(rectangle.Left + padding, rectangle.Top + padding,
                rectangle.Right - padding, rectangle.Bottom - padding);
            float scale = Math.Min(content.Width / bounds.Width, content.Height / bounds.Height);
            float x = rectangle.MidX - bounds.MidX * scale;
            float y = rectangle.MidY - bounds.MidY * scale;
            path.Transform(SKMatrix.CreateScale(scale, scale));
            path.Transform(SKMatrix.CreateTranslation(x, y));
            return (path, new(path.TightBounds, content, y, 72 * scale, 100 * scale,
                advance * scale, advance * scale));
        }
        catch
        {
            path.Dispose();
            throw;
        }
    }

    private float Append(SKPathBuilder path, string? text, SKFont font, float start)
    {
        if (text is null) return 0;
        if (!font.ContainsGlyphs(text))
            throw new InvalidDataException($"Segoe UI Variable has no glyph for one or more characters in '{text}'.");
        using var buffer = new HarfBuzzSharp.Buffer();
        buffer.AddUtf16(text);
        buffer.GuessSegmentProperties();
        shaper.Shape(buffer);
        var info = buffer.GlyphInfos;
        var positions = buffer.GlyphPositions;
        float unit = font.Size / face.UnitsPerEm;
        float advance = 0;
        for (int i = 0; i < info.Length; i++)
        {
            if (info[i].Codepoint == 0)
                throw new InvalidDataException($"Shaping failed for '{text}'.");
            using var glyph = font.GetGlyphPath(checked((ushort)info[i].Codepoint));
            if (glyph is not null)
                path.AddPath(glyph, SKMatrix.CreateTranslation(
                    start + advance + positions[i].XOffset * unit, -positions[i].YOffset * unit));
            advance += positions[i].XAdvance * unit;
        }
        return advance;
    }

    public void Dispose()
    {
        shaper.Dispose();
        face.Dispose();
        blob.Dispose();
        typeface.Dispose();
    }
}
