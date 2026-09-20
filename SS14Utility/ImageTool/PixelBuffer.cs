using System.Drawing.Imaging;

namespace SS14Utility.ImageTool;

public sealed class PixelBuffer
{
    public readonly int Width;
    public readonly int Height;
    private readonly Color[] _pixels;

    public PixelBuffer(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        _pixels = new Color[Width * Height];
    }

    public Color this[int x, int y]
    {
        get => _pixels[y * Width + x];
        set => _pixels[y * Width + x] = value;
    }

    public static PixelBuffer FromBitmap(Bitmap bitmap)
    {
        var w = bitmap.Width;
        var h = bitmap.Height;
        var buffer = new PixelBuffer(w, h);

        var argb = bitmap.PixelFormat == PixelFormat.Format32bppArgb
            ? bitmap
            : bitmap.Clone(new Rectangle(0, 0, w, h), PixelFormat.Format32bppArgb);

        var data = argb.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[data.Stride * h];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            for (var y = 0; y < h; y++)
            {
                var row = y * data.Stride;
                for (var x = 0; x < w; x++)
                {
                    var i = row + x * 4;
                    buffer[x, y] = Color.FromArgb(bytes[i + 3], bytes[i + 2], bytes[i + 1], bytes[i]);
                }
            }
        }
        finally
        {
            argb.UnlockBits(data);
            if (!ReferenceEquals(argb, bitmap))
                argb.Dispose();
        }

        return buffer;
    }

    public Bitmap ToBitmap()
    {
        var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[data.Stride * Height];
            for (var y = 0; y < Height; y++)
            {
                var row = y * data.Stride;
                for (var x = 0; x < Width; x++)
                {
                    var c = this[x, y];
                    var i = row + x * 4;
                    bytes[i] = c.B;
                    bytes[i + 1] = c.G;
                    bytes[i + 2] = c.R;
                    bytes[i + 3] = c.A;
                }
            }
            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        return bmp;
    }

    public PixelBuffer Clone()
    {
        var copy = new PixelBuffer(Width, Height);
        Array.Copy(_pixels, copy._pixels, _pixels.Length);
        return copy;
    }
}
