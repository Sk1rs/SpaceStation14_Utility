namespace SS14Utility.ImageTool;

public static class ImageAdjustments
{
    public static void Grayscale(PixelBuffer buf)
    {
        for (var y = 0; y < buf.Height; y++)
        for (var x = 0; x < buf.Width; x++)
        {
            var c = buf[x, y];
            var lum = (byte)Math.Clamp((c.R * 299 + c.G * 587 + c.B * 114) / 1000, 0, 255);
            buf[x, y] = Color.FromArgb(c.A, lum, lum, lum);
        }
    }

    public static void Invert(PixelBuffer buf)
    {
        for (var y = 0; y < buf.Height; y++)
        for (var x = 0; x < buf.Width; x++)
        {
            var c = buf[x, y];
            buf[x, y] = Color.FromArgb(c.A, 255 - c.R, 255 - c.G, 255 - c.B);
        }
    }

    public static void Brightness(PixelBuffer buf, double factor)
    {
        for (var y = 0; y < buf.Height; y++)
        for (var x = 0; x < buf.Width; x++)
        {
            var c = buf[x, y];
            buf[x, y] = Color.FromArgb(c.A, Scale(c.R, factor), Scale(c.G, factor), Scale(c.B, factor));
        }
    }

    public static void Contrast(PixelBuffer buf, double factor)
    {
        double sum = 0;
        for (var y = 0; y < buf.Height; y++)
        for (var x = 0; x < buf.Width; x++)
        {
            var c = buf[x, y];
            sum += (c.R * 299 + c.G * 587 + c.B * 114) / 1000.0;
        }
        var mean = (int)(sum / (buf.Width * buf.Height) + 0.5);

        for (var y = 0; y < buf.Height; y++)
        for (var x = 0; x < buf.Width; x++)
        {
            var c = buf[x, y];
            byte Blend(byte v) => (byte)Math.Clamp(mean * (1 - factor) + v * factor, 0, 255);
            buf[x, y] = Color.FromArgb(c.A, Blend(c.R), Blend(c.G), Blend(c.B));
        }
    }

    public static void Posterize(PixelBuffer buf, int bits)
    {
        var mask = (byte)(~((1 << (8 - bits)) - 1) & 0xFF);
        for (var y = 0; y < buf.Height; y++)
        for (var x = 0; x < buf.Width; x++)
        {
            var c = buf[x, y];
            buf[x, y] = Color.FromArgb(c.A, c.R & mask, c.G & mask, c.B & mask);
        }
    }

    public static void HueShift(PixelBuffer buf, int degrees)
    {
        var offset = (int)Math.Round(degrees / 360.0 * 255) % 256;
        if (offset < 0) offset += 256;
        if (offset == 0) return;

        for (var y = 0; y < buf.Height; y++)
        for (var x = 0; x < buf.Width; x++)
        {
            var c = buf[x, y];
            RgbToHsv(c.R, c.G, c.B, out var h, out var s, out var v);
            h = (h + offset) % 256;
            HsvToRgb(h, s, v, out var r, out var g, out var b);
            buf[x, y] = Color.FromArgb(c.A, r, g, b);
        }
    }

    public static void Dither(PixelBuffer buf)
    {
        var w = buf.Width;
        var h = buf.Height;
        var err = new double[w, h, 3];

        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var c = buf[x, y];
            if (c.A == 0) continue;

            var channels = new[] { (double)c.R, c.G, c.B };
            var result = new byte[3];
            for (var ch = 0; ch < 3; ch++)
            {
                var old = Math.Clamp(channels[ch] + err[x, y, ch], 0, 255);
                var bucket = Math.Min(15, (int)old / 16);
                var value = bucket * 17;
                var diff = old - value;
                result[ch] = (byte)value;

                if (x + 1 < w) err[x + 1, y, ch] += diff * 7 / 16;
                if (x - 1 >= 0 && y + 1 < h) err[x - 1, y + 1, ch] += diff * 3 / 16;
                if (y + 1 < h) err[x, y + 1, ch] += diff * 5 / 16;
                if (x + 1 < w && y + 1 < h) err[x + 1, y + 1, ch] += diff * 1 / 16;
            }
            buf[x, y] = Color.FromArgb(c.A, result[0], result[1], result[2]);
        }
    }

    public static void Recolor(PixelBuffer buf, IReadOnlyDictionary<Color, Color> mapping)
    {
        if (mapping.Count == 0) return;
        for (var y = 0; y < buf.Height; y++)
        for (var x = 0; x < buf.Width; x++)
            if (mapping.TryGetValue(buf[x, y], out var replacement))
                buf[x, y] = replacement;
    }

    public static List<Color> ExtractPalette(PixelBuffer reference, int nColors)
    {
        var points = new List<(int r, int g, int b)>(reference.Width * reference.Height);
        for (var y = 0; y < reference.Height; y++)
        for (var x = 0; x < reference.Width; x++)
        {
            var c = reference[x, y];
            points.Add((c.R, c.G, c.B));
        }
        if (points.Count == 0) return new List<Color>();

        var buckets = new List<List<(int r, int g, int b)>> { points };
        while (buckets.Count < nColors)
        {
            var splitIndex = -1;
            var maxRange = -1;
            var splitChannel = 0;
            for (var i = 0; i < buckets.Count; i++)
            {
                if (buckets[i].Count < 2) continue;
                var (range, channel) = WidestChannel(buckets[i]);
                if (range > maxRange)
                {
                    maxRange = range;
                    splitIndex = i;
                    splitChannel = channel;
                }
            }
            if (splitIndex < 0) break;

            var bucket = buckets[splitIndex];
            bucket.Sort((a, b) => splitChannel switch
            {
                0 => a.r.CompareTo(b.r),
                1 => a.g.CompareTo(b.g),
                _ => a.b.CompareTo(b.b),
            });
            var mid = bucket.Count / 2;
            buckets[splitIndex] = bucket.GetRange(0, mid);
            buckets.Add(bucket.GetRange(mid, bucket.Count - mid));
        }

        var palette = new List<Color>();
        foreach (var bucket in buckets)
        {
            long sr = 0, sg = 0, sb = 0;
            foreach (var (r, g, b) in bucket) { sr += r; sg += g; sb += b; }
            palette.Add(Color.FromArgb((int)(sr / bucket.Count), (int)(sg / bucket.Count), (int)(sb / bucket.Count)));
        }
        return palette;
    }

    private static (int range, int channel) WidestChannel(List<(int r, int g, int b)> bucket)
    {
        int minR = int.MaxValue, maxR = int.MinValue;
        int minG = int.MaxValue, maxG = int.MinValue;
        int minB = int.MaxValue, maxB = int.MinValue;
        foreach (var (r, g, b) in bucket)
        {
            if (r < minR) minR = r; if (r > maxR) maxR = r;
            if (g < minG) minG = g; if (g > maxG) maxG = g;
            if (b < minB) minB = b; if (b > maxB) maxB = b;
        }
        int rr = maxR - minR, gr = maxG - minG, br = maxB - minB;
        if (rr >= gr && rr >= br) return (rr, 0);
        return gr >= br ? (gr, 1) : (br, 2);
    }

    public static void ApplyPalette(PixelBuffer buf, IReadOnlyList<Color> palette)
    {
        if (palette.Count == 0) return;
        var cache = new Dictionary<int, Color>();

        for (var y = 0; y < buf.Height; y++)
        for (var x = 0; x < buf.Width; x++)
        {
            var c = buf[x, y];
            var key = c.R << 16 | c.G << 8 | c.B;
            if (!cache.TryGetValue(key, out var nearest))
            {
                var best = 0;
                var bestDist = int.MaxValue;
                for (var i = 0; i < palette.Count; i++)
                {
                    var p = palette[i];
                    var dr = p.R - c.R;
                    var dg = p.G - c.G;
                    var db = p.B - c.B;
                    var dist = dr * dr + dg * dg + db * db;
                    if (dist < bestDist) { bestDist = dist; best = i; }
                }
                nearest = palette[best];
                cache[key] = nearest;
            }
            buf[x, y] = Color.FromArgb(c.A, nearest.R, nearest.G, nearest.B);
        }
    }

    public static readonly Color[] GameBoyPalette =
    {
        Color.FromArgb(15, 56, 15), Color.FromArgb(48, 98, 48),
        Color.FromArgb(139, 172, 15), Color.FromArgb(155, 188, 15),
    };

    public static readonly Color[] NesPalette =
    {
        Color.FromArgb(124,124,124), Color.FromArgb(0,0,252), Color.FromArgb(0,0,188), Color.FromArgb(68,40,188),
        Color.FromArgb(148,0,132), Color.FromArgb(168,0,32), Color.FromArgb(168,16,0), Color.FromArgb(136,20,0),
        Color.FromArgb(80,48,0), Color.FromArgb(0,120,0), Color.FromArgb(0,104,0), Color.FromArgb(0,88,0),
        Color.FromArgb(0,64,88), Color.FromArgb(0,0,0),
        Color.FromArgb(188,188,188), Color.FromArgb(0,120,248), Color.FromArgb(0,88,248), Color.FromArgb(104,68,252),
        Color.FromArgb(216,0,204), Color.FromArgb(228,0,88), Color.FromArgb(248,56,0), Color.FromArgb(228,92,16),
        Color.FromArgb(172,124,0), Color.FromArgb(0,184,0), Color.FromArgb(0,168,0), Color.FromArgb(0,168,68),
        Color.FromArgb(0,136,136),
        Color.FromArgb(248,248,248), Color.FromArgb(60,188,252), Color.FromArgb(104,136,252), Color.FromArgb(152,120,248),
        Color.FromArgb(248,120,248), Color.FromArgb(248,88,152), Color.FromArgb(248,120,88), Color.FromArgb(252,160,68),
        Color.FromArgb(248,184,0), Color.FromArgb(184,248,24), Color.FromArgb(88,216,84), Color.FromArgb(88,248,152),
        Color.FromArgb(0,232,216), Color.FromArgb(120,120,120),
        Color.FromArgb(252,252,252), Color.FromArgb(164,228,252), Color.FromArgb(184,184,248), Color.FromArgb(216,184,248),
        Color.FromArgb(248,184,248), Color.FromArgb(248,164,192), Color.FromArgb(240,208,176), Color.FromArgb(252,224,168),
        Color.FromArgb(248,216,120), Color.FromArgb(216,248,120), Color.FromArgb(184,248,184), Color.FromArgb(184,248,216),
        Color.FromArgb(0,252,252), Color.FromArgb(248,216,248),
    };

    public static void StyleGameBoy(PixelBuffer buf) => ApplyPalette(buf, GameBoyPalette);
    public static void StyleNes(PixelBuffer buf) => ApplyPalette(buf, NesPalette);

    public static void StyleGenesis(PixelBuffer buf) => Posterize(buf, 3);

    public static void StyleCrt(PixelBuffer buf)
    {
        for (var y = 0; y < buf.Height; y += 2)
        for (var x = 0; x < buf.Width; x++)
        {
            var c = buf[x, y];
            buf[x, y] = Color.FromArgb(c.A, c.R / 2, c.G / 2, c.B / 2);
        }
    }

    public static readonly string[] HardwareStyleNames = { "None", "Game Boy", "NES", "Genesis", "Arcade CRT" };

    public static readonly Dictionary<string, Action<PixelBuffer>?> HardwareStyles = new()
    {
        ["None"] = null,
        ["Game Boy"] = StyleGameBoy,
        ["NES"] = StyleNes,
        ["Genesis"] = StyleGenesis,
        ["Arcade CRT"] = StyleCrt,
    };

    public static PixelBuffer GenerateNormalMap(PixelBuffer source, double strength, bool useSilhouette)
    {
        var w = source.Width;
        var h = source.Height;
        var height = new double[w, h];

        var luminance = new double[w, h];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var c = source[x, y];
            luminance[x, y] = (c.R * 299 + c.G * 587 + c.B * 114) / 1000.0;
        }

        if (useSilhouette)
        {
            var falloff = SilhouetteFalloff(source, 3);
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                height[x, y] = luminance[x, y] * falloff[x, y] / 255.0;
        }
        else
        {
            height = luminance;
        }

        Sobel(height, w, h, out var gx, out var gy);

        var result = new PixelBuffer(w, h);
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var alpha = source[x, y].A;
            if (alpha == 0)
            {
                result[x, y] = Color.FromArgb(0, 128, 128, 255);
                continue;
            }

            var nx = -gx[x, y] * strength / 255;
            var ny = -gy[x, y] * strength / 255;
            const double nz = 1.0;
            var length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            nx /= length; ny /= length; var nzN = nz / length;

            var r = (byte)Math.Clamp((nx * 0.5 + 0.5) * 255, 0, 255);
            var g = (byte)Math.Clamp((ny * 0.5 + 0.5) * 255, 0, 255);
            var b = (byte)Math.Clamp((nzN * 0.5 + 0.5) * 255, 0, 255);
            result[x, y] = Color.FromArgb(alpha, r, g, b);
        }
        return result;
    }

    private static double[,] SilhouetteFalloff(PixelBuffer source, int radius)
    {
        var w = source.Width;
        var h = source.Height;
        var mask = new bool[w, h];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            mask[x, y] = source[x, y].A > 0;

        var falloff = new double[w, h];
        var current = mask;
        var step = 255.0 / radius;

        for (var i = 0; i < radius; i++)
        {
            current = MinFilter3(current, w, h);
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                if (current[x, y])
                    falloff[x, y] = Math.Min(255, falloff[x, y] + step);
        }
        return falloff;
    }

    private static bool[,] MinFilter3(bool[,] mask, int w, int h)
    {
        var result = new bool[w, h];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var all = true;
            for (var dy = -1; dy <= 1 && all; dy++)
            for (var dx = -1; dx <= 1 && all; dx++)
            {
                var sx = Math.Clamp(x + dx, 0, w - 1);
                var sy = Math.Clamp(y + dy, 0, h - 1);
                if (!mask[sx, sy]) all = false;
            }
            result[x, y] = all;
        }
        return result;
    }

    private static void Sobel(double[,] height, int w, int h, out double[,] gx, out double[,] gy)
    {
        gx = new double[w, h];
        gy = new double[w, h];

        double Get(int x, int y) => height[Math.Clamp(x, 0, w - 1), Math.Clamp(y, 0, h - 1)];

        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            gx[x, y] = (Get(x + 1, y - 1) + 2 * Get(x + 1, y) + Get(x + 1, y + 1))
                       - (Get(x - 1, y - 1) + 2 * Get(x - 1, y) + Get(x - 1, y + 1));
            gy[x, y] = (Get(x - 1, y + 1) + 2 * Get(x, y + 1) + Get(x + 1, y + 1))
                       - (Get(x - 1, y - 1) + 2 * Get(x, y - 1) + Get(x + 1, y - 1));
        }
    }

    private static byte Scale(byte v, double factor) => (byte)Math.Clamp(v * factor, 0, 255);

    private static void RgbToHsv(byte r, byte g, byte b, out int h, out int s, out int v)
    {
        double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
        var max = Math.Max(rd, Math.Max(gd, bd));
        var min = Math.Min(rd, Math.Min(gd, bd));
        var delta = max - min;

        double hue;
        if (delta == 0) hue = 0;
        else if (max == rd) hue = 60 * (((gd - bd) / delta) % 6);
        else if (max == gd) hue = 60 * ((bd - rd) / delta + 2);
        else hue = 60 * ((rd - gd) / delta + 4);
        if (hue < 0) hue += 360;

        var sat = max == 0 ? 0 : delta / max;

        h = (int)Math.Round(hue / 360.0 * 255) % 256;
        if (h < 0) h += 256;
        s = (int)Math.Round(sat * 255);
        v = (int)Math.Round(max * 255);
    }

    private static void HsvToRgb(int h, int s, int v, out byte r, out byte g, out byte b)
    {
        var hue = h / 255.0 * 360.0;
        var sat = s / 255.0;
        var val = v / 255.0;

        var c = val * sat;
        var x = c * (1 - Math.Abs(hue / 60.0 % 2 - 1));
        var m = val - c;

        double rd, gd, bd;
        if (hue < 60) (rd, gd, bd) = (c, x, 0);
        else if (hue < 120) (rd, gd, bd) = (x, c, 0);
        else if (hue < 180) (rd, gd, bd) = (0, c, x);
        else if (hue < 240) (rd, gd, bd) = (0, x, c);
        else if (hue < 300) (rd, gd, bd) = (x, 0, c);
        else (rd, gd, bd) = (c, 0, x);

        r = (byte)Math.Clamp((rd + m) * 255, 0, 255);
        g = (byte)Math.Clamp((gd + m) * 255, 0, 255);
        b = (byte)Math.Clamp((bd + m) * 255, 0, 255);
    }
}
