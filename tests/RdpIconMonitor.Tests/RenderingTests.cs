using RdpIconMonitor.Configuration;
using RdpIconMonitor.Rendering;
using SkiaSharp;

namespace RdpIconMonitor.Tests;

public class RenderingTests
{
    [Fact]
    public void RealVariableFontHasBoldWeight()
    {
        using var text = new SegoeText();
        Assert.StartsWith("Segoe UI Variable", text.FamilyName);
        Assert.Contains(text.Variations, coordinate => coordinate.Axis.ToString() == "wght" && coordinate.Value == 700);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(40)]
    [InlineData(48)]
    [InlineData(64)]
    [InlineData(256)]
    public void NativeTemplatesAndTextRenderAtEverySize(int size)
    {
        using var renderer = new SvgIconRenderer();
        foreach (var style in new[]
        {
            new IconStyle(null, null, null), new IconStyle(300, null, null),
            new IconStyle(150, "CA", null), new IconStyle(250, null, "B"),
            new IconStyle(45, "W", "3"), new IconStyle(null, "descenders gy", "12345678901234567890")
        })
        {
            var result = renderer.Render(style, size);
            using var bitmap = SKBitmap.Decode(result.Png);
            Assert.Equal(size, bitmap.Width);
            Assert.Equal(size, bitmap.Height);
            Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
            Assert.Contains(bitmap.Pixels, pixel => pixel.Alpha > 0);
            if (result.Text is { } layout)
            {
                Assert.InRange(layout.SmallSize / layout.LargeSize, .71999f, .72001f);
                Assert.Equal(layout.SmallAdvance, layout.LargeOrigin);
                Assert.InRange(Math.Abs(layout.Bounds.MidX - layout.ContentBounds.MidX), 0, .001f);
                Assert.InRange(Math.Abs(layout.Bounds.MidY - layout.ContentBounds.MidY), 0, .001f);
                Assert.True(layout.Bounds.Width <= layout.ContentBounds.Width + .001f);
                Assert.True(layout.Bounds.Height <= layout.ContentBounds.Height + .001f);
            }
        }
    }

    [Theory]
    [InlineData("#0078D4")]
    [InlineData("#50E6FF")]
    [InlineData("#FFFFFF")]
    [InlineData("#000000")]
    public void ColorRoundTrip(string color) => Assert.Equal(color, OklchColor.FromHex(color).ToHex());

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(150)]
    [InlineData(250)]
    [InlineData(300)]
    public void ReferenceColorGetsRequestedHue(double hue)
    {
        var output = OklchColor.FromHex(OklchColor.Rotate("#0078D4", hue));
        Assert.InRange(Math.Abs((output.H - hue + 540) % 360 - 180), 0, .6);
    }
}
