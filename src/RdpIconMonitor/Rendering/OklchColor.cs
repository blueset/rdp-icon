using System.Globalization;

namespace RdpIconMonitor.Rendering;

public readonly record struct OklchColor(double L, double C, double H)
{
    public static OklchColor FromHex(string hex)
    {
        var rgb = int.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        double r = Linear((rgb >> 16 & 255) / 255d);
        double g = Linear((rgb >> 8 & 255) / 255d);
        double b = Linear((rgb & 255) / 255d);
        double l = Math.Cbrt(.4122214708 * r + .5363325363 * g + .0514459929 * b);
        double m = Math.Cbrt(.2119034982 * r + .6806995451 * g + .1073969566 * b);
        double s = Math.Cbrt(.0883024619 * r + .2817188376 * g + .6299787005 * b);
        double a = 1.9779984951 * l - 2.4285922050 * m + .4505937099 * s;
        double bb = .0259040371 * l + .7827717662 * m - .8086757660 * s;
        return new(.2104542553 * l + .7936177850 * m - .0040720468 * s,
            Math.Sqrt(a * a + bb * bb), Wrap(Math.Atan2(bb, a) * 180 / Math.PI));
    }

    public string ToHex()
    {
        var rgb = ToLinear(C);
        if (!InGamut(rgb))
        {
            double low = 0, high = C;
            for (int i = 0; i < 32; i++)
            {
                double middle = (low + high) / 2;
                if (InGamut(ToLinear(middle))) low = middle;
                else high = middle;
            }
            rgb = ToLinear(low);
        }
        return $"#{Channel(rgb.R):X2}{Channel(rgb.G):X2}{Channel(rgb.B):X2}";
    }

    public static string Rotate(string hex, double targetHue)
    {
        var original = FromHex(hex);
        return (original with { H = Wrap(original.H + targetHue - FromHex("#0078D4").H) }).ToHex();
    }

    public static double Wrap(double hue) => (hue % 360 + 360) % 360;

    private (double R, double G, double B) ToLinear(double chroma)
    {
        double a = chroma * Math.Cos(H * Math.PI / 180);
        double b = chroma * Math.Sin(H * Math.PI / 180);
        double l = Math.Pow(L + .3963377774 * a + .2158037573 * b, 3);
        double m = Math.Pow(L - .1055613458 * a - .0638541728 * b, 3);
        double s = Math.Pow(L - .0894841775 * a - 1.2914855480 * b, 3);
        return (4.0767416621 * l - 3.3077115913 * m + .2309699292 * s,
            -1.2684380046 * l + 2.6097574011 * m - .3413193965 * s,
            -.0041960863 * l - .7034186147 * m + 1.7076147010 * s);
    }

    private static bool InGamut((double R, double G, double B) rgb) =>
        rgb.R is >= -1e-7 and <= 1.0000001 &&
        rgb.G is >= -1e-7 and <= 1.0000001 &&
        rgb.B is >= -1e-7 and <= 1.0000001;

    private static double Linear(double value) =>
        value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);

    private static int Channel(double value)
    {
        double encoded = value <= .0031308 ? 12.92 * value : 1.055 * Math.Pow(value, 1 / 2.4) - .055;
        return (int)Math.Round(Math.Clamp(encoded, 0, 1) * 255);
    }
}
