using System.Text.Json;
using RdpIconMonitor.Configuration;
using SkiaSharp;

namespace RdpIconMonitor.Rendering;

internal static class IconExport
{
    public static void Write(IconConfiguration configuration, string output)
    {
        Directory.CreateDirectory(output);
        using var renderer = new SvgIconRenderer();
        const int rowHeight = 340;
        using var sheet = SKSurface.Create(new SKImageInfo(800, Math.Max(1, configuration.Instances.Length) * rowHeight));
        sheet.Canvas.Clear(new SKColor(32, 32, 32));
        using var font = new SKFont(SKTypeface.Default, 16);
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        var manifest = new List<object>();
        for (int i = 0; i < configuration.Instances.Length; i++)
        {
            var rule = configuration.Instances[i];
            string prefix = Path.Combine(output, $"instance-{i + 1}");
            var frames = SvgIconRenderer.Sizes.Select(size => renderer.Render(rule.Style, size)).ToArray();
            sheet.Canvas.DrawText(rule.Name, 12, i * rowHeight + 24, SKTextAlign.Left, font, paint);
            int x = 12;
            foreach (var frame in frames)
            {
                File.WriteAllBytes($"{prefix}-{frame.Size}.png", frame.Png);
                File.WriteAllText($"{prefix}-{frame.Size}.svg", frame.Svg);
                using var image = SKImage.FromEncodedData(frame.Png);
                sheet.Canvas.DrawImage(image, x, i * rowHeight + 42, new SKSamplingOptions());
                sheet.Canvas.DrawText(frame.Size.ToString(), x, i * rowHeight + 328, SKTextAlign.Left, font, paint);
                x += Math.Max(28, frame.Size) + 12;
            }
            File.WriteAllBytes($"{prefix}.ico", SvgIconRenderer.EncodeIco(frames));
            manifest.Add(new { rule.Name, rule.Hue, rule.SmallText, rule.LargeText, Ico = $"instance-{i + 1}.ico" });
        }
        using var snapshot = sheet.Snapshot();
        using var png = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(output, "contact-sheet.png"), png.ToArray());
        File.WriteAllText(Path.Combine(output, "manifest.json"),
            JsonSerializer.Serialize(manifest, IconConfiguration.JsonOptions));
    }
}
