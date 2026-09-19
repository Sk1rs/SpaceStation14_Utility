using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;
using System.Text.RegularExpressions;

namespace SS14Utility.ImageTool;

/// <summary>Mutable session state for one conversion - mirrors main.py's module-level globals.</summary>
public sealed class ImageToolState
{
    public bool FullColor;
    public bool Grayscale;
    public bool Invert;
    public int Brightness = 100;
    public int Contrast = 100;
    public int PosterizeBits = 8;
    public bool Dither;
    public int HueShift;
    public readonly Dictionary<Color, Color> RecolorMap = new();
    public List<Color>? ActivePalette;

    // Standard SS14 paper symbol limit is 10000 (at least in RMC-14).
    public int SymbolLimit = 10050;
    public bool UseLimit = true;

    public Size ImageSize;
    public Size OriginalSize;
    public Size ResizeSize;

    // Standard SS14 paper size, in pixels (2 characters wide, so 21x26 fits nicely).
    public static readonly Size PaperSize = new(21, 26);

    public string Symbol = "██";
    public string SymbolMode = "solid"; // "solid" (always Symbol) or "shading" (glyph density follows brightness)
    public const string ShadeRamp = "░▒▓█"; // sparse -> dense

    /// <summary>The loaded/drawn/pasted source image, before any adjustment is applied.</summary>
    public PixelBuffer? SourceImage;

    /// <summary>True for pasted/drawn/parsed images - resized with NEAREST to stay crisp instead of blurring like a photo.</summary>
    public bool IsCustomImage;
}

public static class ImageTransformer
{
    public static (string text, PixelBuffer? preview) Transform(ImageToolState st)
    {
        if (st.SourceImage == null)
            return ("", null);

        var targetW = st.ResizeSize.Width != 0 ? st.ResizeSize.Width : st.SourceImage.Width;
        var targetH = st.ResizeSize.Height != 0 ? st.ResizeSize.Height : st.SourceImage.Height;

        var im = targetW != st.SourceImage.Width || targetH != st.SourceImage.Height
            ? Resize(st.SourceImage, targetW, targetH, st.IsCustomImage)
            : st.SourceImage.Clone();

        if (st.Grayscale) ImageAdjustments.Grayscale(im);
        if (st.Invert) ImageAdjustments.Invert(im);
        if (st.Brightness != 100) ImageAdjustments.Brightness(im, st.Brightness / 100.0);
        if (st.Contrast != 100) ImageAdjustments.Contrast(im, st.Contrast / 100.0);
        if (st.PosterizeBits < 8) ImageAdjustments.Posterize(im, st.PosterizeBits);
        if (st.HueShift != 0) ImageAdjustments.HueShift(im, st.HueShift);
        if (st.ActivePalette is { Count: > 0 }) ImageAdjustments.ApplyPalette(im, st.ActivePalette);
        if (st.Dither && !st.FullColor) ImageAdjustments.Dither(im);
        if (st.RecolorMap.Count > 0) ImageAdjustments.Recolor(im, st.RecolorMap);

        st.ImageSize = new Size(im.Width, im.Height);

        var preview = new PixelBuffer(im.Width, im.Height);
        var text = new StringBuilder();
        var line = new StringBuilder();
        var blankUnit = new string(' ', st.SymbolMode == "shading" ? 2 : st.Symbol.Length);

        // Persists across row boundaries on purpose: an SS14 [color] tag stays in scope across the
        // embedded "\n", so a run that continues into the next row shouldn't re-emit the same tag.
        string? prevColorHex = null;
        var row = 0;

        for (var y = 0; y < im.Height; y++)
        {
            line.Clear();
            for (var x = 0; x < im.Width; x++)
            {
                var c = im[x, y];
                var cur = new SsColor(c.R, c.G, c.B, c.A, st.FullColor);

                if (cur.A is "0" or "00")
                {
                    line.Append(blankUnit);
                    continue;
                }

                var (pr, pg, pb, pa) = cur.GetRgba();
                preview[x, y] = Color.FromArgb(pa, pr, pg, pb);

                string glyph;
                if (st.SymbolMode == "shading")
                {
                    var lum = (c.R * 299 + c.G * 587 + c.B * 114) / 1000;
                    var idx = Math.Min(ImageToolState.ShadeRamp.Length - 1, (255 - lum) * ImageToolState.ShadeRamp.Length / 256);
                    glyph = new string(ImageToolState.ShadeRamp[idx], 2);
                }
                else
                {
                    glyph = st.Symbol;
                }

                var hex = cur.GetColor();
                if (hex == prevColorHex)
                {
                    line.Append(glyph);
                }
                else
                {
                    line.Append("[color=#").Append(hex).Append(']').Append(glyph);
                    prevColorHex = hex;
                }
            }

            if (st.UseLimit && text.Length + line.Length > st.SymbolLimit)
                break;

            text.Append(line).Append('\n');
            row++;
        }

        var cropped = new PixelBuffer(im.Width, Math.Max(row, 0));
        for (var y = 0; y < cropped.Height; y++)
        for (var x = 0; x < cropped.Width; x++)
            cropped[x, y] = preview[x, y];

        return (text.ToString(), cropped);
    }

    /// <summary>Resizes with a GDI+ pipeline that keeps straight (non-premultiplied) alpha and no dark edge fringing.</summary>
    private static PixelBuffer Resize(PixelBuffer source, int width, int height, bool nearest)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        using var src = source.ToBitmap();
        using var dst = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.InterpolationMode = nearest ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.None;

            using var attrs = new ImageAttributes();
            attrs.SetWrapMode(WrapMode.TileFlipXY);
            g.DrawImage(src, new Rectangle(0, 0, width, height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
        }
        return PixelBuffer.FromBitmap(dst);
    }

    private static readonly Regex ColorTagRegex = new(@"\G\[color=#([0-9a-fA-F]{3,8})\]", RegexOptions.Compiled);

    /// <summary>Reverses Transform(): turns previously generated SS14 markup text back into a pixel image.</summary>
    public static PixelBuffer? ParseSs14Text(string text, string pixelSymbol)
    {
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = text.Split('\n').ToList();
        if (lines.Count > 0 && lines[^1] == "")
            lines.RemoveAt(lines.Count - 1);
        if (lines.Count == 0)
            return null;

        var unit = pixelSymbol.Length;
        if (unit == 0) return null;
        var blank = new string(' ', unit);

        // null means "no color established yet" - a bare (untagged) glyph before any tag is invalid,
        // which is what lets this reject unrelated/foreign text instead of misreading it as an image.
        (byte r, byte g, byte b, byte a)? currentColor = null;
        var rows = new List<List<(byte r, byte g, byte b, byte a)?>>();

        foreach (var line in lines)
        {
            var row = new List<(byte, byte, byte, byte)?>();
            var pos = 0;
            while (pos < line.Length)
            {
                var m = ColorTagRegex.Match(line, pos);
                if (m.Success)
                {
                    var parsed = ParseColorTag(m.Groups[1].Value);
                    if (parsed == null) return null;
                    currentColor = parsed;
                    pos = m.Index + m.Length;

                    var remaining = line.Length - pos;
                    if (remaining < unit || line.Substring(pos, unit) == blank)
                        return null;
                    row.Add(currentColor);
                    pos += unit;
                }
                else if (pos + unit <= line.Length && line.Substring(pos, unit) == blank)
                {
                    row.Add(null);
                    pos += unit;
                }
                else if (currentColor != null)
                {
                    row.Add(currentColor);
                    pos += unit;
                }
                else
                {
                    return null;
                }
            }
            rows.Add(row);
        }

        // Width is however many pixels were actually parsed out of each row, not raw character count
        // (tag text like "[color=#fff]" would otherwise massively inflate a naive length-based estimate).
        var width = rows.Count == 0 ? 0 : rows.Max(r => r.Count);
        var height = rows.Count;
        if (width == 0 || height == 0)
            return null;

        var image = new PixelBuffer(width, height);
        for (var y = 0; y < rows.Count; y++)
        for (var x = 0; x < rows[y].Count; x++)
        {
            var c = rows[y][x];
            if (c != null)
                image[x, y] = Color.FromArgb(c.Value.a, c.Value.r, c.Value.g, c.Value.b);
        }
        return image;
    }

    private static byte ExpandHexChannel(string chunk) => (byte)Convert.ToInt32(chunk.Length == 1 ? chunk + chunk : chunk, 16);

    /// <summary>Reverses SsColor.GetColor(): turns a #rgb/#rgba/#rrggbb/#rrggbbaa hex string back into (r,g,b,a).</summary>
    private static (byte, byte, byte, byte)? ParseColorTag(string hex)
    {
        var n = hex.Length;
        var step = n is 3 or 4 ? 1 : n is 6 or 8 ? 2 : -1;
        if (step == -1) return null;

        var values = new List<byte>();
        for (var i = 0; i < n; i += step)
            values.Add(ExpandHexChannel(hex.Substring(i, step)));
        while (values.Count < 4) values.Add(255);

        return (values[0], values[1], values[2], values[3]);
    }
}
