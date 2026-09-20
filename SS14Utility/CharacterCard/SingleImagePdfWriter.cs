using System.Drawing.Imaging;
using System.IO.Compression;
using System.Text;

namespace SS14Utility.CharacterCard;

/// <summary>
///     Writes a one-page PDF containing a single full-bleed raster image - just enough PDF to be
///     printable, hand-built so the project doesn't need a PDF library dependency for one feature.
///     The image is embedded losslessly (FlateDecode over raw RGB), not as JPEG, so card text stays sharp.
/// </summary>
public static class SingleImagePdfWriter
{
    public static void Save(string path, Bitmap image, float dpi = 150f)
    {
        var rgb = ExtractRgbBytes(image);
        var compressed = ZlibCompress(rgb);

        var wPt = image.Width * 72f / dpi;
        var hPt = image.Height * 72f / dpi;

        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));
        void WriteBytes(byte[] b) => ms.Write(b, 0, b.Length);

        Write("%PDF-1.4\n%âãÏÓ\n");

        var offsets = new long[6]; // index 1..5 used

        offsets[1] = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        offsets[2] = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        offsets[3] = ms.Position;
        Write($"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(wPt)} {F(hPt)}] " +
              "/Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>\nendobj\n");

        offsets[4] = ms.Position;
        Write($"4 0 obj\n<< /Type /XObject /Subtype /Image /Width {image.Width} /Height {image.Height} " +
              $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /Length {compressed.Length} >>\nstream\n");
        WriteBytes(compressed);
        Write("\nendstream\nendobj\n");

        var content = $"q {F(wPt)} 0 0 {F(hPt)} 0 0 cm /Im0 Do Q";
        var contentBytes = Encoding.ASCII.GetBytes(content);
        offsets[5] = ms.Position;
        Write($"5 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
        WriteBytes(contentBytes);
        Write("\nendstream\nendobj\n");

        var xrefStart = ms.Position;
        Write("xref\n0 6\n0000000000 65535 f \n");
        for (var i = 1; i <= 5; i++)
            Write($"{offsets[i]:D10} 00000 n \n");
        Write("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n");
        Write(xrefStart.ToString());
        Write("\n%%EOF");

        File.WriteAllBytes(path, ms.ToArray());
    }

    private static string F(float points) => points.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

    private static byte[] ExtractRgbBytes(Bitmap image)
    {
        var w = image.Width;
        var h = image.Height;

        using var rgb24 = image.PixelFormat == PixelFormat.Format24bppRgb
            ? image
            : image.Clone(new Rectangle(0, 0, w, h), PixelFormat.Format24bppRgb);

        var data = rgb24.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var stride = data.Stride;
            var raw = new byte[stride * h];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, raw, 0, raw.Length);

            var outBytes = new byte[w * h * 3];
            for (var y = 0; y < h; y++)
            {
                var srcRow = y * stride;
                var dstRow = y * w * 3;
                for (var x = 0; x < w; x++)
                {
                    var si = srcRow + x * 3;
                    var di = dstRow + x * 3;
                    // 24bppRgb is stored as BGR - PDF DeviceRGB wants RGB.
                    outBytes[di] = raw[si + 2];
                    outBytes[di + 1] = raw[si + 1];
                    outBytes[di + 2] = raw[si];
                }
            }
            return outBytes;
        }
        finally
        {
            rgb24.UnlockBits(data);
            if (!ReferenceEquals(rgb24, image))
                rgb24.Dispose();
        }
    }

    private static byte[] ZlibCompress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var zlib = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(data, 0, data.Length);
        return ms.ToArray();
    }
}
