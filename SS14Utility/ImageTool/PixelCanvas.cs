using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SS14Utility.ImageTool;

public sealed class PixelCanvas : Panel
{
    public int CellSize;
    public Color PaintColor = Color.Black;
    public bool TileMode;
    public Bitmap Image;
    public string Tool = "pencil";
    public bool FilledShapes;
    public bool Symmetry;
    public bool IsoGrid;
    public int IsoTileWidth = 4;
    public Action<Color>? OnColorPicked;

    private readonly List<Bitmap> _undoStack = new();
    private Bitmap? _dragBase;
    private Point? _dragStart;

    public PixelCanvas(int w, int h, int cellSize = 20)
    {
        CellSize = cellSize;
        Image = new Bitmap(Math.Max(1, w), Math.Max(1, h), PixelFormat.Format32bppArgb);
        DoubleBuffered = true;
        ApplyFixedSize();
    }

    public static PixelCanvas FromBuffer(PixelBuffer buf)
    {
        var canvas = new PixelCanvas(buf.Width, buf.Height);
        canvas.Image.Dispose();
        canvas.Image = buf.ToBitmap();
        return canvas;
    }

    public PixelBuffer ToPixelBuffer() => PixelBuffer.FromBitmap(Image);

    private void ApplyFixedSize()
    {
        var factor = TileMode ? 3 : 1;
        Size = new Size(Image.Width * CellSize * factor, Image.Height * CellSize * factor);
    }

    public void SetTileMode(bool enabled)
    {
        TileMode = enabled;
        ApplyFixedSize();
        Invalidate();
    }

    public void SetZoom(int delta)
    {
        CellSize = Math.Clamp(CellSize + delta, 4, 60);
        ApplyFixedSize();
        Invalidate();
    }

    public void ResizeCanvas(int w, int h)
    {
        var newImage = new Bitmap(Math.Max(1, w), Math.Max(1, h), PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(newImage))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImageUnscaled(Image, 0, 0);
        }
        Image.Dispose();
        Image = newImage;
        ApplyFixedSize();
        Invalidate();
    }

    public void Clear()
    {
        PushUndo();
        using (var g = Graphics.FromImage(Image))
            g.Clear(Color.Transparent);
        Invalidate();
    }

    public void FlipHorizontal()
    {
        PushUndo();
        Image.RotateFlip(RotateFlipType.RotateNoneFlipX);
        Invalidate();
    }

    public void FlipVertical()
    {
        PushUndo();
        Image.RotateFlip(RotateFlipType.RotateNoneFlipY);
        Invalidate();
    }

    public void Rotate90()
    {
        PushUndo();
        Image.RotateFlip(RotateFlipType.Rotate90FlipNone);
        ApplyFixedSize();
        Invalidate();
    }

    private Point CellAt(Point pos)
    {
        var x = CellSize > 0 ? pos.X / CellSize : 0;
        var y = CellSize > 0 ? pos.Y / CellSize : 0;
        if (TileMode)
        {
            x = ((x - Image.Width) % Image.Width + Image.Width) % Image.Width;
            y = ((y - Image.Height) % Image.Height + Image.Height) % Image.Height;
        }
        return new Point(x, y);
    }

    private void PaintCell(int x, int y, bool erase)
    {
        if (x < 0 || x >= Image.Width || y < 0 || y >= Image.Height) return;
        Image.SetPixel(x, y, erase ? Color.FromArgb(0, 0, 0, 0) : PaintColor);
        Invalidate();
    }

    private void PaintCellSymmetric(int x, int y, bool erase)
    {
        PaintCell(x, y, erase);
        if (Symmetry) PaintCell(Image.Width - 1 - x, y, erase);
    }

    private static List<Point> LinePoints(int x0, int y0, int x1, int y1)
    {
        var points = new List<Point>();
        var dx = Math.Abs(x1 - x0);
        var sx = x0 < x1 ? 1 : -1;
        var dy = -Math.Abs(y1 - y0);
        var sy = y0 < y1 ? 1 : -1;
        var err = dx + dy;
        int x = x0, y = y0;
        while (true)
        {
            points.Add(new Point(x, y));
            if (x == x1 && y == y1) break;
            var e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }
        }
        return points;
    }

    private List<Point> RectPoints(int x0, int y0, int x1, int y1)
    {
        var xmin = Math.Min(x0, x1); var xmax = Math.Max(x0, x1);
        var ymin = Math.Min(y0, y1); var ymax = Math.Max(y0, y1);
        var points = new List<Point>();
        if (FilledShapes)
        {
            for (var x = xmin; x <= xmax; x++)
            for (var y = ymin; y <= ymax; y++)
                points.Add(new Point(x, y));
        }
        else
        {
            for (var x = xmin; x <= xmax; x++) { points.Add(new Point(x, ymin)); points.Add(new Point(x, ymax)); }
            for (var y = ymin; y <= ymax; y++) { points.Add(new Point(xmin, y)); points.Add(new Point(xmax, y)); }
        }
        return points;
    }

    private void DrawShapePreview(int x, int y)
    {
        Image.Dispose();
        Image = new Bitmap(_dragBase!);
        var (x0, y0) = (_dragStart!.Value.X, _dragStart.Value.Y);
        var points = Tool == "line" ? LinePoints(x0, y0, x, y) : RectPoints(x0, y0, x, y);
        var w = Image.Width; var h = Image.Height;
        foreach (var p in points)
        {
            if (p.X < 0 || p.X >= w || p.Y < 0 || p.Y >= h) continue;
            Image.SetPixel(p.X, p.Y, PaintColor);
            if (Symmetry) Image.SetPixel(w - 1 - p.X, p.Y, PaintColor);
        }
        Invalidate();
    }

    public void FloodFill(int x, int y, Color newColor)
    {
        var w = Image.Width; var h = Image.Height;
        if (x < 0 || x >= w || y < 0 || y >= h) return;
        var target = Image.GetPixel(x, y);
        if (target == newColor) return;

        PushUndo();
        var stack = new Stack<(int x, int y)>();
        stack.Push((x, y));
        var visited = new HashSet<(int, int)>();
        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Pop();
            if (cx < 0 || cx >= w || cy < 0 || cy >= h || visited.Contains((cx, cy))) continue;
            if (Image.GetPixel(cx, cy) != target) continue;
            visited.Add((cx, cy));
            Image.SetPixel(cx, cy, newColor);
            stack.Push((cx + 1, cy)); stack.Push((cx - 1, cy)); stack.Push((cx, cy + 1)); stack.Push((cx, cy - 1));
        }
        Invalidate();
    }

    private void PushUndo()
    {
        _undoStack.Add(new Bitmap(Image));
        if (_undoStack.Count > 50)
        {
            _undoStack[0].Dispose();
            _undoStack.RemoveAt(0);
        }
    }

    public void Undo()
    {
        if (_undoStack.Count == 0) return;
        Image.Dispose();
        Image = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        ApplyFixedSize();
        Invalidate();
    }

    private void PickColorAt(int x, int y)
    {
        if (x < 0 || x >= Image.Width || y < 0 || y >= Image.Height) return;
        var picked = Image.GetPixel(x, y);
        if (picked.A > 0)
        {
            PaintColor = picked;
            OnColorPicked?.Invoke(picked);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var cell = CellAt(e.Location);

        if (e.Button == MouseButtons.Middle) { PickColorAt(cell.X, cell.Y); return; }
        if (e.Button == MouseButtons.Right) { PushUndo(); PaintCellSymmetric(cell.X, cell.Y, true); return; }

        if (Tool == "fill")
        {
            FloodFill(cell.X, cell.Y, PaintColor);
        }
        else if (Tool is "line" or "rect")
        {
            _dragBase = new Bitmap(Image);
            _dragStart = cell;
            DrawShapePreview(cell.X, cell.Y);
        }
        else
        {
            PushUndo();
            PaintCellSymmetric(cell.X, cell.Y, false);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var cell = CellAt(e.Location);

        if ((e.Button & MouseButtons.Right) != 0)
            PaintCellSymmetric(cell.X, cell.Y, true);
        else if (Tool == "pencil" && (e.Button & MouseButtons.Left) != 0)
            PaintCellSymmetric(cell.X, cell.Y, false);
        else if (Tool is "line" or "rect" && (e.Button & MouseButtons.Left) != 0 && _dragStart != null)
            DrawShapePreview(cell.X, cell.Y);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && _dragBase != null)
        {
            _undoStack.Add(_dragBase);
            if (_undoStack.Count > 50) { _undoStack[0].Dispose(); _undoStack.RemoveAt(0); }
            _dragBase = null;
            _dragStart = null;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        var w = Image.Width; var h = Image.Height;
        Point isoOffset;

        if (TileMode)
        {
            for (var ty = -1; ty <= 1; ty++)
            for (var tx = -1; tx <= 1; tx++)
                PaintTileCopy(g, tx * w, ty * h, w, h, tx != 0 || ty != 0);

            using var pen = new Pen(Color.FromArgb(220, 80, 200, 255));
            g.DrawRectangle(pen, w * CellSize, h * CellSize, w * CellSize - 1, h * CellSize - 1);
            isoOffset = new Point(w, h);
        }
        else
        {
            PaintTileCopy(g, 0, 0, w, h, false);
            isoOffset = Point.Empty;
        }

        if (IsoGrid) DrawIsoGrid(g, w, h, isoOffset);
    }

    private void PaintTileCopy(Graphics g, int cellOffsetX, int cellOffsetY, int w, int h, bool dim)
    {
        var ox = cellOffsetX * CellSize; var oy = cellOffsetY * CellSize;

        using var lightBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
        using var darkBrush = new SolidBrush(Color.FromArgb(160, 160, 160));
        for (var gy = 0; gy < h; gy++)
        for (var gx = 0; gx < w; gx++)
            g.FillRectangle((gx + gy) % 2 == 0 ? lightBrush : darkBrush, ox + gx * CellSize, oy + gy * CellSize, CellSize, CellSize);

        g.DrawImage(Image, new Rectangle(ox, oy, w * CellSize, h * CellSize));

        if (dim)
        {
            using var dimBrush = new SolidBrush(Color.FromArgb(120, 10, 10, 15));
            g.FillRectangle(dimBrush, ox, oy, w * CellSize, h * CellSize);
        }
        else
        {
            using var pen = new Pen(Color.FromArgb(60, 0, 0, 0));
            for (var gx = 0; gx <= w; gx++)
                g.DrawLine(pen, ox + gx * CellSize, oy, ox + gx * CellSize, oy + h * CellSize);
            for (var gy = 0; gy <= h; gy++)
                g.DrawLine(pen, ox, oy + gy * CellSize, ox + w * CellSize, oy + gy * CellSize);
        }
    }

    private void DrawIsoGrid(Graphics g, int w, int h, Point offset)
    {
        var tw = Math.Max(2, IsoTileWidth);
        var th = Math.Max(1, tw / 2);
        var pxW = w * CellSize; var pxH = h * CellSize;
        var dx = tw * CellSize; var dy = th * CellSize;
        if (dx <= 0) return;

        var ox = offset.X * CellSize; var oy = offset.Y * CellSize;
        var spanSteps = pxW / dx + pxH / Math.Max(dy, 1) + 2;

        using var pen = new Pen(Color.FromArgb(140, 80, 200, 255));
        for (var x = -spanSteps * dx; x <= pxW + spanSteps * dx; x += dx)
        {
            g.DrawLine(pen, ox + x, oy, ox + x + spanSteps * dx, oy + spanSteps * dy);
            g.DrawLine(pen, ox + x, oy, ox + x - spanSteps * dx, oy + spanSteps * dy);
        }
    }
}
