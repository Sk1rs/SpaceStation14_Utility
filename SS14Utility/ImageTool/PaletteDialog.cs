namespace SS14Utility.ImageTool;

public sealed class PaletteDialog : Form
{
    private readonly FlowLayoutPanel _swatches = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly NumericUpDown _colorsBox;
    private PixelBuffer? _reference;

    public List<Color>? SelectedPalette { get; private set; }
    private List<Color> _currentPalette = new();

    public PaletteDialog()
    {
        Text = "Палитра из референса";
        Size = new Size(600, 260);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(30, 31, 38);
        ForeColor = Color.FromArgb(232, 232, 236);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };

        var load = new Button { Text = "Загрузить референс..." };
        load.Click += (_, _) => LoadReference();
        toolbar.Controls.Add(load);

        toolbar.Controls.Add(new Label { Text = "Цветов:", AutoSize = true, Padding = new Padding(10, 8, 0, 0) });
        _colorsBox = new NumericUpDown { Minimum = 2, Maximum = 64, Value = 16, Width = 60 };
        _colorsBox.ValueChanged += (_, _) => RefreshPalette();
        toolbar.Controls.Add(_colorsBox);

        foreach (var preset in new[] { 8, 16, 32 })
        {
            var b = new Button { Text = preset.ToString() };
            b.Click += (_, _) => _colorsBox.Value = preset;
            toolbar.Controls.Add(b);
        }

        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(_swatches, 0, 1);

        var use = new Button { Text = "Использовать эту палитру", Dock = DockStyle.Fill };
        use.Click += (_, _) =>
        {
            if (_currentPalette.Count == 0) return;
            SelectedPalette = _currentPalette;
            DialogResult = DialogResult.OK;
            Close();
        };
        layout.Controls.Add(use, 0, 2);

        Controls.Add(layout);
        _swatches.Controls.Add(new Label { Text = "Референс ещё не загружен", AutoSize = true });
    }

    private void LoadReference()
    {
        using var dialog = new OpenFileDialog { Filter = "Изображения (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|Все файлы (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            using var bmp = new Bitmap(dialog.FileName);
            _reference = PixelBuffer.FromBitmap(bmp);
        }
        catch
        {
            return;
        }
        RefreshPalette();
    }

    private void RefreshPalette()
    {
        if (_reference == null) return;
        _currentPalette = ImageAdjustments.ExtractPalette(_reference, (int)_colorsBox.Value);
        RebuildSwatches();
    }

    private void RebuildSwatches()
    {
        _swatches.Controls.Clear();
        foreach (var c in _currentPalette)
        {
            _swatches.Controls.Add(new Panel
            {
                Width = 22,
                Height = 22,
                Margin = new Padding(2),
                BackColor = c,
                BorderStyle = BorderStyle.FixedSingle,
            });
        }
    }
}
