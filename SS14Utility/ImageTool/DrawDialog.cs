namespace SS14Utility.ImageTool;

public sealed class DrawDialog : Form
{
    private readonly PixelCanvas _canvas;
    private readonly List<Color> _recentColors = new();
    private readonly Button _colorButton = new() { Width = 50, Height = 26 };
    private readonly FlowLayoutPanel _swatches = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoScroll = true };
    private readonly Button _toolPencil, _toolFill, _toolLine, _toolRect;
    private readonly NumericUpDown _widthBox, _heightBox;

    public PixelBuffer? AppliedImage { get; private set; }

    public DrawDialog(int initialW, int initialH, PixelBuffer? initialImage)
    {
        _canvas = initialImage is { Width: > 0, Height: > 0 } ? PixelCanvas.FromBuffer(initialImage) : new PixelCanvas(Math.Clamp(initialW, 1, 100), Math.Clamp(initialH, 1, 100));
        _canvas.OnColorPicked = c => { UpdateColorButton(); AddRecentColor(c); };

        Text = "Рисование";
        Size = new Size(680, 640);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(30, 31, 38);
        ForeColor = Color.FromArgb(232, 232, 236);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, Padding = new Padding(6) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        var toolbar1 = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        toolbar1.Controls.Add(new Label { Text = "ЛКМ: рисовать/заливать. ПКМ: стереть. СКМ: пипетка.", AutoSize = true, Padding = new Padding(0, 6, 8, 0) });

        UpdateColorButton();
        _colorButton.Click += (_, _) => PickColor();
        toolbar1.Controls.Add(_colorButton);

        _toolPencil = MakeToolButton("Карандаш", "pencil");
        _toolFill = MakeToolButton("Заливка", "fill");
        _toolLine = MakeToolButton("Линия", "line");
        _toolRect = MakeToolButton("Прямоугольник", "rect");
        toolbar1.Controls.Add(_toolPencil);
        toolbar1.Controls.Add(_toolFill);
        toolbar1.Controls.Add(_toolLine);
        toolbar1.Controls.Add(_toolRect);
        SetTool("pencil");

        var undoBtn = new Button { Text = "Отменить" };
        undoBtn.Click += (_, _) => _canvas.Undo();
        toolbar1.Controls.Add(undoBtn);

        var clearBtn = new Button { Text = "Очистить" };
        clearBtn.Click += (_, _) => _canvas.Clear();
        toolbar1.Controls.Add(clearBtn);

        root.Controls.Add(toolbar1, 0, 0);

        var toolbar2 = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };

        var flipH = new Button { Text = "Отразить Г" };
        flipH.Click += (_, _) => _canvas.FlipHorizontal();
        toolbar2.Controls.Add(flipH);

        var flipV = new Button { Text = "Отразить В" };
        flipV.Click += (_, _) => _canvas.FlipVertical();
        toolbar2.Controls.Add(flipV);

        var rotate = new Button { Text = "Повернуть 90°" };
        rotate.Click += (_, _) => { _canvas.Rotate90(); SyncSizeBoxes(); };
        toolbar2.Controls.Add(rotate);

        var zoomOut = new Button { Text = "Zoom -" };
        zoomOut.Click += (_, _) => _canvas.SetZoom(-4);
        toolbar2.Controls.Add(zoomOut);

        var zoomIn = new Button { Text = "Zoom +" };
        zoomIn.Click += (_, _) => _canvas.SetZoom(4);
        toolbar2.Controls.Add(zoomIn);

        var filled = new CheckBox { Text = "Заливка фигур", AutoSize = true, ForeColor = ForeColor };
        filled.CheckedChanged += (_, _) => _canvas.FilledShapes = filled.Checked;
        toolbar2.Controls.Add(filled);

        var symmetry = new CheckBox { Text = "Симметрия", AutoSize = true, ForeColor = ForeColor };
        symmetry.CheckedChanged += (_, _) => _canvas.Symmetry = symmetry.Checked;
        toolbar2.Controls.Add(symmetry);

        var iso = new CheckBox { Text = "Изо-сетка", AutoSize = true, ForeColor = ForeColor };
        iso.CheckedChanged += (_, _) => { _canvas.IsoGrid = iso.Checked; _canvas.Invalidate(); };
        toolbar2.Controls.Add(iso);

        toolbar2.Controls.Add(new Label { Text = "Тайл:", AutoSize = true, Padding = new Padding(6, 6, 0, 0) });
        var isoTile = new NumericUpDown { Minimum = 2, Maximum = 20, Value = _canvas.IsoTileWidth, Width = 50 };
        isoTile.ValueChanged += (_, _) => { _canvas.IsoTileWidth = (int)isoTile.Value; if (_canvas.IsoGrid) _canvas.Invalidate(); };
        toolbar2.Controls.Add(isoTile);

        var tileMode = new CheckBox { Text = "Бесшовный тайл", AutoSize = true, ForeColor = ForeColor };
        tileMode.CheckedChanged += (_, _) => _canvas.SetTileMode(tileMode.Checked);
        toolbar2.Controls.Add(tileMode);

        toolbar2.Controls.Add(new Label { Text = "Ш:", AutoSize = true, Padding = new Padding(6, 6, 0, 0) });
        _widthBox = new NumericUpDown { Minimum = 1, Maximum = 100, Value = _canvas.Image.Width, Width = 55 };
        toolbar2.Controls.Add(_widthBox);

        toolbar2.Controls.Add(new Label { Text = "В:", AutoSize = true, Padding = new Padding(6, 6, 0, 0) });
        _heightBox = new NumericUpDown { Minimum = 1, Maximum = 100, Value = _canvas.Image.Height, Width = 55 };
        toolbar2.Controls.Add(_heightBox);

        var resizeBtn = new Button { Text = "Изменить размер" };
        resizeBtn.Click += (_, _) => _canvas.ResizeCanvas((int)_widthBox.Value, (int)_heightBox.Value);
        toolbar2.Controls.Add(resizeBtn);

        root.Controls.Add(toolbar2, 0, 1);

        _swatches.Controls.Add(new Label { Text = "Недавние цвета:", AutoSize = true, Padding = new Padding(0, 4, 4, 0) });
        root.Controls.Add(_swatches, 0, 2);

        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(22, 23, 29) };
        scroller.Controls.Add(_canvas);
        root.Controls.Add(scroller, 0, 3);

        var apply = new Button { Text = "Использовать этот рисунок", Dock = DockStyle.Fill };
        apply.Click += (_, _) =>
        {
            AppliedImage = _canvas.ToPixelBuffer();
            DialogResult = DialogResult.OK;
            Close();
        };
        root.Controls.Add(apply, 0, 4);

        Controls.Add(root);

        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Z) _canvas.Undo();
        };
    }

    private Button MakeToolButton(string text, string tool)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += (_, _) => SetTool(tool);
        return button;
    }

    private void SetTool(string tool)
    {
        _canvas.Tool = tool;
        foreach (var (btn, name) in new[] { (_toolPencil, "pencil"), (_toolFill, "fill"), (_toolLine, "line"), (_toolRect, "rect") })
            btn.BackColor = name == tool ? Color.FromArgb(79, 109, 245) : Color.FromArgb(52, 56, 66);
    }

    private void UpdateColorButton() => _colorButton.BackColor = _canvas.PaintColor;

    private void PickColor()
    {
        using var dialog = new ColorDialog { Color = _canvas.PaintColor, FullOpen = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _canvas.PaintColor = dialog.Color;
        UpdateColorButton();
        AddRecentColor(dialog.Color);
    }

    private void AddRecentColor(Color color)
    {
        _recentColors.RemoveAll(c => c.ToArgb() == color.ToArgb());
        _recentColors.Insert(0, color);
        if (_recentColors.Count > 10) _recentColors.RemoveRange(10, _recentColors.Count - 10);
        RebuildSwatches();
    }

    private void RebuildSwatches()
    {
        _swatches.Controls.Clear();
        _swatches.Controls.Add(new Label { Text = "Недавние цвета:", AutoSize = true, Padding = new Padding(0, 4, 4, 0) });
        foreach (var color in _recentColors)
        {
            var button = new Button { Width = 24, Height = 24, BackColor = color, FlatStyle = FlatStyle.Flat };
            button.Click += (_, _) => { _canvas.PaintColor = color; UpdateColorButton(); };
            _swatches.Controls.Add(button);
        }
    }

    private void SyncSizeBoxes()
    {
        _widthBox.Value = Math.Min(_widthBox.Maximum, _canvas.Image.Width);
        _heightBox.Value = Math.Min(_heightBox.Maximum, _canvas.Image.Height);
    }
}
