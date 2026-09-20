using System.Drawing.Imaging;

namespace SS14Utility.ImageTool;

public sealed class FramePickerDialog : Form
{
    private readonly Image _source;
    private readonly int _frameCount;
    private readonly PictureBox _preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(42, 44, 59) };
    private readonly NumericUpDown _frameBox;
    private readonly Label _countLabel;

    public PixelBuffer? SelectedFrame { get; private set; }
    public int SelectedIndex { get; private set; }

    public FramePickerDialog(Image source)
    {
        _source = source;
        _frameCount = source.GetFrameCount(FrameDimension.Time);

        Text = "Выбор кадра";
        Size = new Size(420, 480);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(30, 31, 38);
        ForeColor = Color.FromArgb(232, 232, 236);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        layout.Controls.Add(_preview, 0, 0);

        _frameBox = new NumericUpDown { Minimum = 0, Maximum = Math.Max(0, _frameCount - 1), Width = 70 };
        _frameBox.ValueChanged += (_, _) => ShowFrame((int)_frameBox.Value);

        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        var prev = new Button { Text = "< Пред" };
        prev.Click += (_, _) => _frameBox.Value = Math.Max(_frameBox.Minimum, _frameBox.Value - 1);
        controls.Controls.Add(prev);

        controls.Controls.Add(new Label { Text = "Кадр:", AutoSize = true, Padding = new Padding(6, 8, 0, 0) });
        controls.Controls.Add(_frameBox);

        _countLabel = new Label { Text = $"/ {_frameCount - 1}", AutoSize = true, Padding = new Padding(4, 8, 0, 0) };
        controls.Controls.Add(_countLabel);

        var next = new Button { Text = "След >" };
        next.Click += (_, _) => _frameBox.Value = Math.Min(_frameBox.Maximum, _frameBox.Value + 1);
        controls.Controls.Add(next);

        layout.Controls.Add(controls, 0, 1);

        var apply = new Button { Text = "Использовать этот кадр", Dock = DockStyle.Fill };
        apply.Click += (_, _) =>
        {
            SelectedIndex = (int)_frameBox.Value;
            DialogResult = DialogResult.OK;
            Close();
        };
        layout.Controls.Add(apply, 0, 2);

        Controls.Add(layout);
        ShowFrame(0);
    }

    private void ShowFrame(int index)
    {
        _source.SelectActiveFrame(FrameDimension.Time, index);
        using var frame = new Bitmap(_source);
        SelectedFrame = PixelBuffer.FromBitmap(frame);
        _preview.Image?.Dispose();
        _preview.Image = SelectedFrame.ToBitmap();
    }
}
