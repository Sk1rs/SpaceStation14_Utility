namespace SS14Utility.ImageTool;

/// <summary>
///     One pixel's SS14 `[color=#...]` representation. When <paramref name="fullColor" /> is false
///     (the game's non-#RRGGBB text mode), each channel is truncated to a single hex digit - the same
///     16-level quantization the "Dither" option is built to hide.
/// </summary>
public readonly struct SsColor
{
    public readonly string R, G, B, A;

    public SsColor(byte r, byte g, byte b, byte a, bool fullColor)
    {
        var rs = r.ToString("x2");
        var gs = g.ToString("x2");
        var bs = b.ToString("x2");
        var as_ = a.ToString("x2");

        if (!fullColor)
        {
            rs = rs[..1];
            gs = gs[..1];
            bs = bs[..1];
            as_ = as_[..1];
        }

        R = rs;
        G = gs;
        B = bs;
        A = as_;
    }

    /// <summary>Returns rgb/rrggbb(/aa) hex, omitting alpha when it's fully opaque.</summary>
    public string GetColor()
    {
        var result = R + G + B;
        if (A != "f" && A != "ff")
            result += A;
        return result;
    }

    /// <summary>The (r, g, b, a) this pixel will actually render as in-game.</summary>
    public (byte r, byte g, byte b, byte a) GetRgba()
    {
        return (Expand(R), Expand(G), Expand(B), Expand(A));
    }

    private static byte Expand(string h) => (byte)Convert.ToInt32(h.Length == 1 ? h + h : h, 16);
}
