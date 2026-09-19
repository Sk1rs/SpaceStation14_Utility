using System.Drawing.Imaging;

namespace SS14Utility.ImageTool;

/// <summary>Main UI for the image-to-SS14-text converter - ported from main.py's Window class.</summary>
public sealed class ImageToolPanel : UserControl
{
    private static readonly Color BgColor = Color.FromArgb(28, 30, 36);
    private static readonly Color FgColor = Color.FromArgb(224, 226, 232);
    private static readonly Color MutedColor = Color.FromArgb(150, 155, 168);
    private static readonly Color InputColor = Color.FromArgb(24, 26, 31);
    private static readonly Color BadColor = Color.FromArgb(214, 92, 92);

    private const string UsefulVideoUrl = "https://youtu.be/9FCF2Y4lIWk?si=LEDw75eOhhTPN_Ua";

    private static readonly string[] SymbolPresets =
    {
        "██", "▓▓", "▒▒", "░░",
        "▲▲", "▼▼", "◄◄", "►►", "△△", "▽▽",
        "◆◆", "◇◇", "●●", "○○", "◐◐", "◑◑",
        "■■", "□□", "▪▪", "▫▫",
        "★★", "☆☆", "♦♦", "♥♥", "♠♠", "♣♣",
        "••", "××", "##", "@@", "%%", "&&",
    };

    private readonly ImageToolState _state = new();
    private string? _path;
    private PixelBuffer? _currentPreview;
    private string _previewStyle = "None";
    private Color? _recolorSource;
    private Color _recolorTarget = Color.Red;
    private bool _suspendEvents;

    private CheckBox _chkKeepRatio = null!, _chkShading = null!, _chkUseLimit = null!;
    private TrackBar _sliderBrightness = null!, _sliderContrast = null!, _sliderHue = null!;
    private NumericUpDown _spinPosterize = null!, _spinW = null!, _spinH = null!;
    private Button _btnRecolorFrom = null!, _btnRecolorTo = null!;
    private Label _lblPaletteStatus = null!, _lblRecolorCount = null!, _lblInfo = null!;
    private TextBox _txtSymbol = null!, _txtOutput = null!;
    private ComboBox _comboPreset = null!, _comboStyle = null!;
    private PictureBox _previewBox = null!;
    private Label _previewPlaceholder = null!;

    public ImageToolPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = BgColor;
        ForeColor = FgColor;
        Font = new Font("Segoe UI", 9f);
        AllowDrop = true;

        BuildUi();

        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += OnDragDrop;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Only hijacks Ctrl+V when the output box isn't focused, so pasting text into it still works.
        if (keyData == (Keys.Control | Keys.V) && !_txtOutput.Focused)
        {
            OnPaste();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    #region UI construction

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        root.Controls.Add(BuildSourceGroup(), 0, 0);
        root.Controls.Add(BuildAdjustGroup(), 0, 1);
        root.Controls.Add(BuildRecolorGroup(), 0, 2);
        root.Controls.Add(BuildSymbolGroup(), 0, 3);
        root.Controls.Add(BuildSizeGroup(), 0, 4);
        root.Controls.Add(BuildOutputGroup(), 0, 5);
        root.Controls.Add(BuildContent(), 0, 6);

        _lblInfo = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Text = "Изображение не загружено" };
        root.Controls.Add(_lblInfo, 0, 7);

        Controls.Add(root);
    }

    private Control BuildSourceGroup()
    {
        var box = MakeGroup("Источник");
        var flow = MakeFlow();
        flow.Controls.Add(MakeButton("Открыть файл", (_, _) => OnOpenFile()));
        flow.Controls.Add(MakeButton("Выбрать кадр GIF", (_, _) => OnOpenGif()));
        flow.Controls.Add(MakeButton("Вставить (Ctrl+V)", (_, _) => OnPaste()));
        flow.Controls.Add(MakeButton("Рисовать", (_, _) => OnDraw()));
        box.Controls.Add(flow);
        return box;
    }

    private Control BuildAdjustGroup()
    {
        var box = MakeGroup("Коррекция");
        var flow = MakeFlow();

        var chkFullColor = MakeCheck("Полный #RRGGBB");
        chkFullColor.CheckedChanged += (_, _) => { _state.FullColor = chkFullColor.Checked; RefreshIfLoaded(); };
        flow.Controls.Add(chkFullColor);

        var chkDither = MakeCheck("Dither");
        chkDither.CheckedChanged += (_, _) => { _state.Dither = chkDither.Checked; RefreshIfLoaded(); };
        flow.Controls.Add(chkDither);

        var chkGrayscale = MakeCheck("Ч/Б");
        chkGrayscale.CheckedChanged += (_, _) => { _state.Grayscale = chkGrayscale.Checked; RefreshIfLoaded(); };
        flow.Controls.Add(chkGrayscale);

        var chkInvert = MakeCheck("Инверсия");
        chkInvert.CheckedChanged += (_, _) => { _state.Invert = chkInvert.Checked; RefreshIfLoaded(); };
        flow.Controls.Add(chkInvert);

        flow.Controls.Add(MakeLabel("Яркость:"));
        _sliderBrightness = MakeSlider(50, 150, 100);
        _sliderBrightness.ValueChanged += (_, _) => { _state.Brightness = _sliderBrightness.Value; RefreshIfLoaded(); };
        flow.Controls.Add(_sliderBrightness);

        flow.Controls.Add(MakeLabel("Контраст:"));
        _sliderContrast = MakeSlider(50, 150, 100);
        _sliderContrast.ValueChanged += (_, _) => { _state.Contrast = _sliderContrast.Value; RefreshIfLoaded(); };
        flow.Controls.Add(_sliderContrast);

        flow.Controls.Add(MakeLabel("Постеризация:"));
        _spinPosterize = MakeNumeric(1, 8, 8);
        _spinPosterize.ValueChanged += (_, _) => { _state.PosterizeBits = (int)_spinPosterize.Value; RefreshIfLoaded(); };
        flow.Controls.Add(_spinPosterize);

        flow.Controls.Add(MakeLabel("Сдвиг оттенка:"));
        _sliderHue = MakeSlider(-180, 180, 0);
        _sliderHue.ValueChanged += (_, _) => { _state.HueShift = _sliderHue.Value; RefreshIfLoaded(); };
        flow.Controls.Add(_sliderHue);

        flow.Controls.Add(MakeButton("Палитра из изображения...", (_, _) => OnPickPalette()));

        _lblPaletteStatus = MakeLabel("Палитра: нет");
        flow.Controls.Add(_lblPaletteStatus);

        flow.Controls.Add(MakeButton("Очистить палитру", (_, _) => OnClearPalette()));
        flow.Controls.Add(MakeButton("Сброс", (_, _) => OnResetAdjust()));

        box.Controls.Add(flow);
        return box;
    }

    private Control BuildRecolorGroup()
    {
        var box = MakeGroup("Перекраска");
        var flow = MakeFlow();
        flow.Controls.Add(MakeLabel("Кликните по превью, чтобы выбрать цвет:"));

        _btnRecolorFrom = new Button { Width = 26, Height = 26, Enabled = false, Margin = new Padding(3) };
        flow.Controls.Add(_btnRecolorFrom);

        flow.Controls.Add(MakeLabel("→"));

        _btnRecolorTo = new Button { Width = 26, Height = 26, BackColor = _recolorTarget, Margin = new Padding(3) };
        _btnRecolorTo.Click += (_, _) => OnPickRecolorTarget();
        flow.Controls.Add(_btnRecolorTo);

        flow.Controls.Add(MakeButton("Заменить", (_, _) => OnRecolorApply()));
        flow.Controls.Add(MakeButton("Очистить всё", (_, _) => OnRecolorClear()));

        _lblRecolorCount = MakeLabel("0 активных");
        flow.Controls.Add(_lblRecolorCount);

        box.Controls.Add(flow);
        return box;
    }

    private Control BuildSymbolGroup()
    {
        var box = MakeGroup("Символ");
        var flow = MakeFlow();

        flow.Controls.Add(MakeLabel("Символ:"));
        _txtSymbol = new TextBox { Width = 60, Text = _state.Symbol, Margin = new Padding(3, 6, 3, 3) };
        StyleInput(_txtSymbol);
        _txtSymbol.TextChanged += (_, _) =>
        {
            if (string.IsNullOrEmpty(_txtSymbol.Text)) return;
            _state.Symbol = _txtSymbol.Text;
            RefreshIfLoaded();
        };
        flow.Controls.Add(_txtSymbol);

        flow.Controls.Add(MakeLabel("Пресеты:"));
        _comboPreset = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70, Margin = new Padding(3, 6, 3, 3) };
        StyleCombo(_comboPreset);
        _comboPreset.Items.Add("...");
        foreach (var p in SymbolPresets) _comboPreset.Items.Add(p);
        _comboPreset.SelectedIndex = 0;
        _comboPreset.SelectedIndexChanged += (_, _) =>
        {
            var text = _comboPreset.SelectedItem as string;
            if (!string.IsNullOrEmpty(text) && text != "...") _txtSymbol.Text = text;
        };
        flow.Controls.Add(_comboPreset);

        _chkShading = MakeCheck("Режим тени (плотность = яркость)");
        _chkShading.CheckedChanged += (_, _) =>
        {
            _state.SymbolMode = _chkShading.Checked ? "shading" : "solid";
            _txtSymbol.Enabled = !_chkShading.Checked;
            RefreshIfLoaded();
        };
        flow.Controls.Add(_chkShading);

        box.Controls.Add(flow);
        return box;
    }

    private Control BuildSizeGroup()
    {
        var box = MakeGroup("Размер");
        var flow = MakeFlow();

        flow.Controls.Add(MakeLabel("Ш:"));
        _spinW = MakeNumeric(1, 500, 1);
        _spinW.ValueChanged += (_, _) => OnSpinWChanged();
        flow.Controls.Add(_spinW);

        flow.Controls.Add(MakeLabel("В:"));
        _spinH = MakeNumeric(1, 500, 1);
        _spinH.ValueChanged += (_, _) => OnSpinHChanged();
        flow.Controls.Add(_spinH);

        _chkKeepRatio = MakeCheck("Сохранять пропорции");
        _chkKeepRatio.Checked = true;
        flow.Controls.Add(_chkKeepRatio);

        foreach (var step in new[] { 2, 4 })
        {
            var s = step;
            flow.Controls.Add(MakeButton($"+{s}", (_, _) => OnScale(s)));
        }

        flow.Controls.Add(MakeButton($"По размеру листа ({ImageToolState.PaperSize.Width}x{ImageToolState.PaperSize.Height})", (_, _) => OnFitToPaper()));
        flow.Controls.Add(MakeButton("По лимиту символов", (_, _) => OnFitToLimit()));
        flow.Controls.Add(MakeButton("Сброс размера", (_, _) => OnResetSize()));

        box.Controls.Add(flow);
        return box;
    }

    private Control BuildOutputGroup()
    {
        var box = MakeGroup("Вывод");
        var flow = MakeFlow();

        flow.Controls.Add(MakeButton("Обновить", (_, _) => { if (_state.SourceImage != null) RunTransform(); }));

        _chkUseLimit = MakeCheck("Учитывать лимит");
        _chkUseLimit.Checked = true;
        _chkUseLimit.CheckedChanged += (_, _) => _state.UseLimit = _chkUseLimit.Checked;
        flow.Controls.Add(_chkUseLimit);

        flow.Controls.Add(MakeButton("Копировать", (_, _) => { if (_txtOutput.Text.Length > 0) Clipboard.SetText(_txtOutput.Text); }));
        flow.Controls.Add(MakeButton("Сохранить текст", (_, _) => OnSaveText()));
        flow.Controls.Add(MakeButton("Загрузить текст", (_, _) => OnLoadText()));
        flow.Controls.Add(MakeButton("Сохранить превью", (_, _) => OnSavePreview()));
        flow.Controls.Add(MakeButton("Normal map...", (_, _) => OnNormalMap()));
        flow.Controls.Add(MakeButton("Полезное видео", (_, _) => OnVideo()));

        box.Controls.Add(flow);
        return box;
    }

    private Control BuildContent()
    {
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));

        _txtOutput = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9f) };
        StyleInput(_txtOutput);
        split.Controls.Add(_txtOutput, 0, 0);

        var previewPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        previewPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        previewPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var styleRow = new FlowLayoutPanel { Dock = DockStyle.Fill };
        styleRow.Controls.Add(MakeLabel("Превью стиля:"));
        _comboStyle = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Margin = new Padding(3, 6, 3, 3) };
        StyleCombo(_comboStyle);
        foreach (var name in ImageAdjustments.HardwareStyleNames) _comboStyle.Items.Add(name);
        _comboStyle.SelectedIndex = 0;
        _comboStyle.SelectedIndexChanged += (_, _) => { _previewStyle = (string)_comboStyle.SelectedItem!; ShowPreview(_currentPreview); };
        styleRow.Controls.Add(_comboStyle);
        previewPanel.Controls.Add(styleRow, 0, 0);

        var previewHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(42, 44, 59), BorderStyle = BorderStyle.FixedSingle };
        _previewBox = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
        _previewBox.MouseClick += OnPreviewClicked;
        _previewPlaceholder = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Text = "Нет превью", ForeColor = MutedColor };
        previewHost.Controls.Add(_previewBox);
        previewHost.Controls.Add(_previewPlaceholder);
        previewPanel.Controls.Add(previewHost, 0, 1);

        split.Controls.Add(previewPanel, 1, 0);
        return split;
    }

    private static GroupBox MakeGroup(string title) => new() { Text = title, Dock = DockStyle.Fill, ForeColor = FgColor };

    private static FlowLayoutPanel MakeFlow() => new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoScroll = false };

    private Button MakeButton(string text, EventHandler onClick)
    {
        var b = new Button { Text = text, AutoSize = true, Margin = new Padding(3) };
        StyleButton(b);
        b.Click += onClick;
        return b;
    }

    private static void StyleButton(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.BackColor = Color.FromArgb(52, 56, 66);
        b.ForeColor = FgColor;
        b.FlatAppearance.BorderColor = Color.FromArgb(70, 76, 90);
        b.UseVisualStyleBackColor = false;
    }

    private CheckBox MakeCheck(string text) => new() { Text = text, AutoSize = true, ForeColor = FgColor, Margin = new Padding(3, 8, 3, 3) };

    private static Label MakeLabel(string text) => new() { Text = text, AutoSize = true, ForeColor = FgColor, Padding = new Padding(3, 8, 0, 0) };

    private static TrackBar MakeSlider(int min, int max, int value) => new() { Minimum = min, Maximum = max, Value = value, Width = 100, TickStyle = TickStyle.None, Margin = new Padding(3) };

    private static NumericUpDown MakeNumeric(int min, int max, int value)
    {
        var n = new NumericUpDown { Minimum = min, Maximum = max, Value = value, Width = 55, Margin = new Padding(3, 6, 3, 3) };
        n.BackColor = InputColor;
        n.ForeColor = FgColor;
        n.BorderStyle = BorderStyle.FixedSingle;
        return n;
    }

    private static void StyleInput(TextBox box)
    {
        box.BackColor = InputColor;
        box.ForeColor = FgColor;
        box.BorderStyle = BorderStyle.FixedSingle;
    }

    private static void StyleCombo(ComboBox box)
    {
        box.BackColor = InputColor;
        box.ForeColor = FgColor;
        box.FlatStyle = FlatStyle.Flat;
    }

    #endregion

    #region Transform / preview

    private void RunTransform()
    {
        var (text, preview) = ImageTransformer.Transform(_state);
        _currentPreview = preview;
        UpdateLabel(text.Length);
        _txtOutput.Text = text;
        ShowPreview(preview);
    }

    private void RefreshIfLoaded()
    {
        if (_suspendEvents) return;
        if (_state.SourceImage != null) RunTransform();
    }

    private void UpdateLabel(int length)
    {
        _lblInfo.Text = $"{_state.ImageSize.Width}x{_state.ImageSize.Height}  {length}/{_state.SymbolLimit}";
        _lblInfo.ForeColor = length > _state.SymbolLimit ? BadColor : FgColor;
    }

    private void ShowPreview(PixelBuffer? preview)
    {
        if (preview == null || preview.Width == 0 || preview.Height == 0)
        {
            _previewBox.Image?.Dispose();
            _previewBox.Image = null;
            _previewPlaceholder.Visible = true;
            return;
        }
        _previewPlaceholder.Visible = false;

        var styled = preview;
        if (ImageAdjustments.HardwareStyles.TryGetValue(_previewStyle, out var styleFn) && styleFn != null)
        {
            try
            {
                styled = preview.Clone();
                styleFn(styled);
            }
            catch
            {
                styled = preview;
            }
        }

        _previewBox.Image?.Dispose();
        _previewBox.Image = styled.ToBitmap();
    }

    private void OnPreviewClicked(object? sender, MouseEventArgs e)
    {
        if (_currentPreview == null || _previewBox.Image == null) return;
        var img = _previewBox.Image;
        var box = _previewBox;
        if (box.Width <= 0 || box.Height <= 0) return;

        var scale = Math.Min((double)box.Width / img.Width, (double)box.Height / img.Height);
        var dispW = img.Width * scale;
        var dispH = img.Height * scale;
        var offX = (box.Width - dispW) / 2;
        var offY = (box.Height - dispH) / 2;

        var localX = e.X - offX;
        var localY = e.Y - offY;
        if (localX < 0 || localX >= dispW || localY < 0 || localY >= dispH) return;

        var srcX = Math.Clamp((int)(localX / dispW * _currentPreview.Width), 0, _currentPreview.Width - 1);
        var srcY = Math.Clamp((int)(localY / dispH * _currentPreview.Height), 0, _currentPreview.Height - 1);
        var color = _currentPreview[srcX, srcY];

        _recolorSource = color;
        _btnRecolorFrom.BackColor = color;
        _btnRecolorFrom.Enabled = true;
    }

    #endregion

    #region Source

    private void OnOpenFile()
    {
        using var dialog = new OpenFileDialog();
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        LoadPath(dialog.FileName);
    }

    private void OnOpenGif()
    {
        using var dialog = new OpenFileDialog { Filter = "Изображения (*.gif;*.webp;*.png;*.apng)|*.gif;*.webp;*.png;*.apng|Все файлы (*.*)|*.*" };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        Image img;
        try { img = Image.FromFile(dialog.FileName); }
        catch { return; }

        try
        {
            var frameCount = 1;
            try { frameCount = img.GetFrameCount(FrameDimension.Time); }
            catch { /* format has no Time frame dimension */ }

            if (frameCount <= 1)
            {
                LoadPath(dialog.FileName);
                return;
            }

            using var picker = new FramePickerDialog(img);
            if (picker.ShowDialog(FindForm()) == DialogResult.OK && picker.SelectedFrame != null)
                UseCustomImage(picker.SelectedFrame, $"(кадр {picker.SelectedIndex} из {dialog.FileName})");
        }
        finally
        {
            img.Dispose();
        }
    }

    private void OnPaste()
    {
        if (!Clipboard.ContainsImage()) return;
        using var img = Clipboard.GetImage();
        if (img == null) return;
        using var bmp = new Bitmap(img);
        UseCustomImage(PixelBuffer.FromBitmap(bmp), "(буфер обмена)");
    }

    private void OnDraw()
    {
        var w = (int)_spinW.Value; if (w <= 0) w = 16;
        var h = (int)_spinH.Value; if (h <= 0) h = 16;
        using var dialog = new DrawDialog(w, h, _currentPreview);
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK && dialog.AppliedImage != null)
            UseCustomImage(dialog.AppliedImage, "(рисунок)");
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            LoadPath(files[0]);
    }

    private void LoadPath(string path)
    {
        _path = path;
        _state.IsCustomImage = false;

        try
        {
            using var bmp = new Bitmap(path);
            _state.OriginalSize = new Size(bmp.Width, bmp.Height);
            _state.SourceImage = PixelBuffer.FromBitmap(bmp);
        }
        catch
        {
            return;
        }

        var w = Math.Min(_state.OriginalSize.Width, (int)_spinW.Maximum);
        var h = Math.Min(_state.OriginalSize.Height, (int)_spinH.Maximum);

        _suspendEvents = true;
        _spinW.Value = Math.Max((int)_spinW.Minimum, w);
        _spinH.Value = Math.Max((int)_spinH.Minimum, h);
        _suspendEvents = false;

        _state.ResizeSize = new Size((int)_spinW.Value, (int)_spinH.Value);
        RunTransform();
    }

    private void UseCustomImage(PixelBuffer image, string label)
    {
        _path = label;
        _state.SourceImage = image;
        _state.IsCustomImage = true;
        _state.OriginalSize = new Size(image.Width, image.Height);

        var w = Math.Min(image.Width, (int)_spinW.Maximum);
        var h = Math.Min(image.Height, (int)_spinH.Maximum);

        _suspendEvents = true;
        _spinW.Value = Math.Max((int)_spinW.Minimum, w);
        _spinH.Value = Math.Max((int)_spinH.Minimum, h);
        _suspendEvents = false;

        _state.ResizeSize = new Size((int)_spinW.Value, (int)_spinH.Value);
        RunTransform();
    }

    #endregion

    #region Recolor / palette

    private void OnPickRecolorTarget()
    {
        using var dialog = new ColorDialog { Color = _recolorTarget, FullOpen = true };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        _recolorTarget = dialog.Color;
        _btnRecolorTo.BackColor = _recolorTarget;
    }

    private void OnRecolorApply()
    {
        if (_recolorSource == null) return;
        _state.RecolorMap[_recolorSource.Value] = _recolorTarget;
        _lblRecolorCount.Text = $"{_state.RecolorMap.Count} активных";
        RefreshIfLoaded();
    }

    private void OnRecolorClear()
    {
        _state.RecolorMap.Clear();
        _lblRecolorCount.Text = "0 активных";
        RefreshIfLoaded();
    }

    private void OnPickPalette()
    {
        using var dialog = new PaletteDialog();
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK && dialog.SelectedPalette is { Count: > 0 } palette)
        {
            _state.ActivePalette = palette;
            _lblPaletteStatus.Text = $"Палитра: {palette.Count} цветов";
            RefreshIfLoaded();
        }
    }

    private void OnClearPalette()
    {
        _state.ActivePalette = null;
        _lblPaletteStatus.Text = "Палитра: нет";
        RefreshIfLoaded();
    }

    private void OnResetAdjust()
    {
        _suspendEvents = true;
        _state.Brightness = 100; _state.Contrast = 100; _state.PosterizeBits = 8; _state.HueShift = 0;
        _sliderBrightness.Value = 100;
        _sliderContrast.Value = 100;
        _spinPosterize.Value = 8;
        _sliderHue.Value = 0;
        _suspendEvents = false;
        RefreshIfLoaded();
    }

    #endregion

    #region Size

    private void OnSpinWChanged()
    {
        if (_suspendEvents) return;
        var height = (int)_spinH.Value;
        if (_chkKeepRatio.Checked && _state.OriginalSize.Width != 0)
            height = Math.Max(1, (int)Math.Round((double)_spinW.Value * _state.OriginalSize.Height / _state.OriginalSize.Width));
        SetResize((int)_spinW.Value, height);
    }

    private void OnSpinHChanged()
    {
        if (_suspendEvents) return;
        var width = (int)_spinW.Value;
        if (_chkKeepRatio.Checked && _state.OriginalSize.Height != 0)
            width = Math.Max(1, (int)Math.Round((double)_spinH.Value * _state.OriginalSize.Width / _state.OriginalSize.Height));
        SetResize(width, (int)_spinH.Value);
    }

    private void SetResize(int w, int h)
    {
        _suspendEvents = true;
        _spinW.Value = Math.Clamp(w, (int)_spinW.Minimum, (int)_spinW.Maximum);
        _spinH.Value = Math.Clamp(h, (int)_spinH.Minimum, (int)_spinH.Maximum);
        w = (int)_spinW.Value;
        h = (int)_spinH.Value;
        _suspendEvents = false;

        _state.ResizeSize = new Size(w, h);
        RefreshIfLoaded();
    }

    private void OnScale(int step)
    {
        var w = (int)_spinW.Value;
        var h = (int)_spinH.Value;
        if (w == 0 || h == 0) return;
        var scale = (double)(w + step) / w;
        SetResize(w + step, Math.Max(1, (int)Math.Round(h * scale)));
    }

    private void OnFitToPaper()
    {
        var w = _state.OriginalSize.Width;
        var h = _state.OriginalSize.Height;
        if (w == 0 || h == 0) return;
        var ratio = Math.Min(Math.Min((double)ImageToolState.PaperSize.Width / w, (double)ImageToolState.PaperSize.Height / h), 1);
        SetResize(Math.Max(1, (int)Math.Round(w * ratio)), Math.Max(1, (int)Math.Round(h * ratio)));
    }

    private void OnResetSize()
    {
        if (_state.OriginalSize.Width == 0 || _state.OriginalSize.Height == 0) return;
        var w = Math.Min(_state.OriginalSize.Width, (int)_spinW.Maximum);
        var h = Math.Min(_state.OriginalSize.Height, (int)_spinH.Maximum);
        SetResize(w, h);
    }

    /// <summary>Shrinks the current size (keeping its aspect ratio) until the FULL image fits under the symbol limit.</summary>
    private void OnFitToLimit()
    {
        var baseW = (int)_spinW.Value;
        var baseH = (int)_spinH.Value;
        if (baseW == 0 || baseH == 0) return;

        (int length, int w, int h) Probe(double scale)
        {
            var w = Math.Max(1, (int)Math.Round(baseW * scale));
            var h = Math.Max(1, (int)Math.Round(baseH * scale));
            var savedResize = _state.ResizeSize;
            var savedLimit = _state.UseLimit;
            _state.ResizeSize = new Size(w, h);
            // Measure the TRUE, untruncated length regardless of the "use limit" checkbox, otherwise
            // a truncated probe always looks like it "fits" and the search never shrinks.
            _state.UseLimit = false;
            var (text, _) = ImageTransformer.Transform(_state);
            _state.ResizeSize = savedResize;
            _state.UseLimit = savedLimit;
            return (text.Length, w, h);
        }

        var (length, w, h) = Probe(1.0);
        if (length <= _state.SymbolLimit) return;

        double lo = 0.0, hi = 1.0;
        for (var i = 0; i < 20; i++)
        {
            var mid = (lo + hi) / 2;
            (length, w, h) = Probe(mid);
            if (length <= _state.SymbolLimit) lo = mid; else hi = mid;
        }

        (_, w, h) = Probe(lo);
        SetResize(w, h);
    }

    #endregion

    #region Output

    private void OnSaveText()
    {
        if (_txtOutput.Text.Length == 0) return;
        using var dialog = new SaveFileDialog { Filter = "Текстовые файлы (*.txt)|*.txt" };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        File.WriteAllText(dialog.FileName, _txtOutput.Text, System.Text.Encoding.UTF8);
    }

    private void OnLoadText()
    {
        using var dialog = new OpenFileDialog { Filter = "Текстовые файлы (*.txt)|*.txt" };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        string text;
        try { text = File.ReadAllText(dialog.FileName); }
        catch { return; }

        var parsed = ImageTransformer.ParseSs14Text(text, _state.Symbol);
        if (parsed == null)
        {
            // Doesn't look like valid SS14 markup - just show the raw text, no preview to derive.
            _currentPreview = null;
            ShowPreview(null);
            _txtOutput.Text = text;
            _lblInfo.Text = $"Загружено из файла (не похоже на разметку, превью нет): {text.Length}/{_state.SymbolLimit}";
            return;
        }
        UseCustomImage(parsed, $"(загружено из {dialog.FileName})");
    }

    private void OnSavePreview()
    {
        if (_currentPreview == null) return;
        using var dialog = new SaveFileDialog { Filter = "PNG files (*.png)|*.png" };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        using var bmp = _currentPreview.ToBitmap();
        bmp.Save(dialog.FileName, ImageFormat.Png);
    }

    private void OnNormalMap()
    {
        if (_currentPreview == null) return;
        using var dialog = new NormalMapDialog(_currentPreview);
        dialog.ShowDialog(FindForm());
    }

    private void OnVideo()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(UsefulVideoUrl) { UseShellExecute = true });
    }

    #endregion
}
