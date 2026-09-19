namespace SS14Utility.ImageTool;

/// <summary>
///     Generates a tangent-space normal map from the current output image - an export-only asset for
///     real-time lighting in an external game engine. SS14's paper text can't use this itself.
/// </summary>
public sealed class NormalMapDialog : Form
{
    private readonly PixelBuffer _source;
    private PixelBuffer? _result;

    private readonly PictureBox _sourceBox = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(42, 44, 59) };
    private readonly PictureBox _resultBox = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(42, 44, 59) };
    private readonly TrackBar _strength = new() { Minimum = 1, Maximum = 100, Value = 35 };
    private readonly CheckBox _silhouette = new() { Text = "Скруглять края силуэта", Checked = true, AutoSize = true, ForeColor = Color.FromArgb(232, 232, 236) };

    public NormalMapDialog(PixelBuffer source)
    {
        _source = source;

        Text = "Генератор Normal Map";
        Size = new Size(560, 540);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(30, 31, 38);
        ForeColor = Color.FromArgb(232, 232, 236);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        var info = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Строит RGB normal map по форме и яркости этого изображения - ассет для освещения во внешнем "
                   + "движке. Это экспорт-инструмент, для самого текста SS14 он бесполезен.",
            AutoSize = false,
        };
        layout.Controls.Add(info, 0, 0);

        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        controls.Controls.Add(new Label { Text = "Сила:", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        _strength.Width = 200;
        _strength.ValueChanged += (_, _) => Regenerate();
        controls.Controls.Add(_strength);
        _silhouette.CheckedChanged += (_, _) => Regenerate();
        controls.Controls.Add(_silhouette);
        layout.Controls.Add(controls, 0, 1);

        var previews = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        previews.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        previews.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        previews.Controls.Add(_sourceBox, 0, 0);
        previews.Controls.Add(_resultBox, 1, 0);
        layout.Controls.Add(previews, 0, 2);

        var save = new Button { Text = "Сохранить normal map как PNG...", Dock = DockStyle.Fill };
        save.Click += (_, _) => SaveResult();
        layout.Controls.Add(save, 0, 3);

        Controls.Add(layout);

        _sourceBox.Image = _source.ToBitmap();
        Regenerate();
    }

    private void Regenerate()
    {
        var strength = _strength.Value / 10.0;
        _result = ImageAdjustments.GenerateNormalMap(_source, strength, _silhouette.Checked);
        _resultBox.Image?.Dispose();
        _resultBox.Image = _result.ToBitmap();
    }

    private void SaveResult()
    {
        if (_result == null) return;
        using var dialog = new SaveFileDialog { Filter = "PNG files (*.png)|*.png" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        using var bmp = _result.ToBitmap();
        bmp.Save(dialog.FileName, System.Drawing.Imaging.ImageFormat.Png);
    }
}
