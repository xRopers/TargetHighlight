using System;
using System.Numerics;

namespace TargetHighlight.Utils;

/// <summary>RGB/HSV conversions for the colour fields in the settings window. Channels are 0-1 unless stated.</summary>
public static class ColorConversion
{
    /// <summary>A 0-1 channel as 0-255.</summary>
    public static int ToByte(float channel) => (int)MathF.Round(Math.Clamp(channel, 0f, 1f) * 255f);

    public static (float H, float S, float V) RgbToHsv(Vector3 rgb)
    {
        var max = MathF.Max(rgb.X, MathF.Max(rgb.Y, rgb.Z));
        var min = MathF.Min(rgb.X, MathF.Min(rgb.Y, rgb.Z));
        var delta = max - min;

        var h = 0f;
        if (delta > 0f)
        {
            if (max == rgb.X)
                h = (rgb.Y - rgb.Z) / delta % 6f;
            else if (max == rgb.Y)
                h = (rgb.Z - rgb.X) / delta + 2f;
            else
                h = (rgb.X - rgb.Y) / delta + 4f;
            h /= 6f;
            if (h < 0f)
                h += 1f;
        }

        return (h, max == 0f ? 0f : delta / max, max);
    }

    public static Vector3 HsvToRgb(Vector3 hsv)
    {
        var h = hsv.X % 1f * 6f;
        var c = hsv.Z * hsv.Y;
        var x = c * (1f - MathF.Abs(h % 2f - 1f));
        var m = hsv.Z - c;
        var (r, g, b) = (int)h switch
        {
            0 => (c, x, 0f),
            1 => (x, c, 0f),
            2 => (0f, c, x),
            3 => (0f, x, c),
            4 => (x, 0f, c),
            _ => (c, 0f, x),
        };
        return new Vector3(r + m, g + m, b + m);
    }
}
