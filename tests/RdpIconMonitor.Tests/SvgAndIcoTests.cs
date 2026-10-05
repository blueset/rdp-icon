using System.Xml.Linq;
using RdpIconMonitor.Configuration;
using RdpIconMonitor.Rendering;
using SkiaSharp;

namespace RdpIconMonitor.Tests;

public class SvgAndIcoTests
{
    private static readonly XNamespace Ns = "http://www.w3.org/2000/svg";

    [Fact]
    public void HueAndTextPreserveTheNeutralGradientAndShadowPrimitives()
    {
        using var renderer = new SvgIconRenderer();
        foreach (int size in SvgIconRenderer.Sizes)
        {
            var before = XDocument.Parse(renderer.Render(new(null, null, null), size).Svg);
            var after = XDocument.Parse(renderer.Render(new(300, "CA", "B"), size).Svg);
            var originalGradient = Gradient(before, "paint7");
            var changedGradient = Gradient(after, "paint7");
            Assert.Equal(originalGradient.Elements().Select(node => node.ToString()),
                changedGradient.Elements().Select(node => node.ToString()));
            var originalFilter = before.Descendants(Ns + "filter").Single(node => Id(node).StartsWith("filter2_"));
            var changedFilter = after.Descendants(Ns + "filter").Single(node => Id(node).StartsWith("filter2_"));
            Assert.Equal(originalFilter.Elements().Select(node => node.ToString()),
                changedFilter.Elements().Select(node => node.ToString()));
            var originalCloud = before.Descendants(Ns + "path").Single(node => Fill(node).StartsWith("url(#paint7"));
            var text = after.Descendants(Ns + "path").Single(node => Fill(node).StartsWith("url(#paint7"));
            Assert.NotEqual(originalCloud.Attribute("d")!.Value, text.Attribute("d")!.Value);
            Assert.Equal(before.Descendants(Ns + "path").Count(), after.Descendants(Ns + "path").Count());
            foreach (var gradient in before.Descendants(Ns + "linearGradient").Where(node => !Id(node).StartsWith("paint7")))
            {
                var changed = after.Descendants(Ns + "linearGradient").Single(node => Id(node) == Id(gradient));
                Assert.Equal(gradient.Attributes(), changed.Attributes(), new AttributeComparer());
                foreach (var pair in gradient.Elements(Ns + "stop").Zip(changed.Elements(Ns + "stop")))
                {
                    var sourceColor = OklchColor.FromHex(pair.First.Attribute("stop-color")!.Value);
                    var outputColor = OklchColor.FromHex(pair.Second.Attribute("stop-color")!.Value);
                    double expectedHue = OklchColor.Wrap(sourceColor.H + 300 - OklchColor.FromHex("#0078D4").H);
                    Assert.InRange(Math.Abs((outputColor.H - expectedHue + 540) % 360 - 180), 0, 1);
                    Assert.InRange(Math.Abs(outputColor.L - sourceColor.L), 0, .005);
                    Assert.True(outputColor.C <= sourceColor.C + .002);
                }
            }
        }
    }

    [Fact]
    public void OmittedHueDoesNotRewriteGradientColors()
    {
        using var renderer = new SvgIconRenderer();
        var original = XDocument.Parse(renderer.Render(new(null, null, null), 48).Svg);
        var text = XDocument.Parse(renderer.Render(new(null, "W", "2"), 48).Svg);
        Assert.Equal(original.Descendants(Ns + "stop").Select(node => node.ToString()),
            text.Descendants(Ns + "stop").Select(node => node.ToString()));
    }

    [Fact]
    public void TextBoundsUseEightPercentMiddleRectanglePadding()
    {
        using var renderer = new SvgIconRenderer();
        var output = renderer.Render(new(null, "gy", "AB"), 48);
        var doc = XDocument.Parse(output.Svg);
        using var path = SKPath.ParseSvgPathData(doc.Descendants(Ns + "path")
            .Single(node => Fill(node).StartsWith("url(#paint6")).Attribute("d")!.Value);
        var bounds = path.TightBounds;
        var text = Assert.IsType<TextGeometry>(output.Text);
        Assert.InRange(Math.Abs(text.ContentBounds.Left - bounds.Left - bounds.Width * .08f), 0, .0001f);
        Assert.InRange(Math.Abs(text.ContentBounds.Top - bounds.Top - bounds.Width * .08f), 0, .0001f);
    }

    [Fact]
    public void IcoHasEightValidTransparent32BitPngFrames()
    {
        using var renderer = new SvgIconRenderer();
        var frames = SvgIconRenderer.Sizes.Select(size => renderer.Render(new(150, "W", "1"), size)).ToArray();
        byte[] ico = SvgIconRenderer.EncodeIco(frames);
        Assert.Equal(0, BitConverter.ToUInt16(ico, 0));
        Assert.Equal(1, BitConverter.ToUInt16(ico, 2));
        Assert.Equal(8, BitConverter.ToUInt16(ico, 4));
        uint expected = 134;
        for (int i = 0; i < 8; i++)
        {
            int position = 6 + i * 16;
            Assert.Equal(frames[i].Size == 256 ? 0 : frames[i].Size, ico[position]);
            Assert.Equal(32, BitConverter.ToUInt16(ico, position + 6));
            uint length = BitConverter.ToUInt32(ico, position + 8);
            uint offset = BitConverter.ToUInt32(ico, position + 12);
            Assert.Equal(expected, offset);
            Assert.Equal(frames[i].Png, ico.AsSpan((int)offset, (int)length).ToArray());
            using var bitmap = SKBitmap.Decode(ico.AsSpan((int)offset, (int)length).ToArray());
            Assert.Equal(frames[i].Size, bitmap.Width);
            Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
            expected += length;
        }
        Assert.Equal((uint)ico.Length, expected);
        string path = Path.Combine(Path.GetTempPath(), $"RdpIcon-{Guid.NewGuid()}.ico");
        File.WriteAllBytes(path, ico);
        try
        {
            using var icon = Icon.ExtractIcon(path, 0, 256);
            Assert.NotNull(icon);
            Assert.Equal(256, icon.Width);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(18)]
    [InlineData(36)]
    [InlineData(72)]
    [InlineData(96)]
    public void IntermediateDpiSizesHaveExactRequestedDimensions(int size)
    {
        using var renderer = new SvgIconRenderer();
        using var bitmap = SKBitmap.Decode(renderer.Render(new(200, "W", "2"), size).Png);
        Assert.Equal(size, bitmap.Width);
        Assert.Equal(size, bitmap.Height);
    }

    private static string Id(XElement node) => node.Attribute("id")!.Value;
    private static string Fill(XElement node) => node.Attribute("fill")!.Value;
    private static XElement Gradient(XDocument doc, string prefix) =>
        doc.Descendants(Ns + "linearGradient").Single(node => Id(node).StartsWith(prefix));
    private sealed class AttributeComparer : IEqualityComparer<XAttribute>
    {
        public bool Equals(XAttribute? left, XAttribute? right) => left?.Name == right?.Name && left?.Value == right?.Value;
        public int GetHashCode(XAttribute value) => HashCode.Combine(value.Name, value.Value);
    }
}
