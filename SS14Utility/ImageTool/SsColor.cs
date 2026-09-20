namespace SS14Utility.ImageTool;

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

    public string GetColor()
    {
        var result = R + G + B;
        if (A != "f" && A != "ff")
            result += A;
        return result;
    }

    public (byte r, byte g, byte b, byte a) GetRgba()
    {
        return (Expand(R), Expand(G), Expand(B), Expand(A));
    }

    private static byte Expand(string h) => (byte)Convert.ToInt32(h.Length == 1 ? h + h : h, 16);
}
