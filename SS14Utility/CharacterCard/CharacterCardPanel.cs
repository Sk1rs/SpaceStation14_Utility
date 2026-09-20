using static SS14Utility.UiKit;

namespace SS14Utility.CharacterCard;

public sealed class CharacterCardPanel : UserControl
{
    private readonly CharacterCardData _data = new();
    private Bitmap? _renderedCard;

    private TextBox _txtName = null!, _txtAge = null!, _txtPosition = null!;
    private TextBox _txtLore = null!, _txtSkills = null!;
    private PictureBox _previewBox = null!;
    private Label _previewPlaceholder = null!;
    private Label _lblFront = null!, _lblBack = null!, _lblLeft = null!, _lblRight = null!;
    private Control[] _stackedGroups = Array.Empty<Control>();
    private Panel _content = null!;

    public CharacterCardPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = BgColor;
        ForeColor = FgColor;
        Font = new Font("Segoe UI", 9f);

        BuildUi();
    }

    #region UI construction

    private void BuildUi()
    {
        var groupsBottomToTop = new[]
        {
            BuildExportGroup(),
            BuildTextGroup(),
            BuildPhotosGroup(),
        };
        _stackedGroups = groupsBottomToTop;

        _content = (Panel)BuildContent();
        _content.Dock = DockStyle.None;

        foreach (var g in groupsBottomToTop)
            Controls.Add(g);
        Controls.Add(_content);

        Layout += (_, _) => RepositionContent();
        RepositionContent();
    }

    private void RepositionContent()
    {
        var top = _stackedGroups.Length == 0 ? 0 : _stackedGroups.Max(g => g.Bottom);
        _content.SetBounds(0, top, ClientSize.Width, Math.Max(0, ClientSize.Height - top));
    }

    private Control BuildPhotosGroup()
    {
        var box = MakeGroup("Фото персонажа (PNG)");
        var flow = MakeFlow();

        (Button btn, Label lbl) MakePhotoSlot(string title, Action<Bitmap> onLoaded)
        {
            var panel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = new Padding(3),
            };
            var btn = MakeButton(title, (_, _) => LoadPhoto(onLoaded));
            var lbl = MakeLabel("не загружено");
            lbl.ForeColor = MutedColor;
            panel.Controls.Add(btn);
            panel.Controls.Add(lbl);
            flow.Controls.Add(panel);
            return (btn, lbl);
        }

        (_, _lblFront) = MakePhotoSlot("Загрузить: ФАС", b => _data.Front = b);
        (_, _lblBack) = MakePhotoSlot("Загрузить: спина", b => _data.Back = b);
        (_, _lblLeft) = MakePhotoSlot("Загрузить: слева", b => _data.Left = b);
        (_, _lblRight) = MakePhotoSlot("Загрузить: справа", b => _data.Right = b);

        box.Controls.Add(flow);
        AttachAutoHeight(box, flow);
        return box;
    }

    private Control BuildTextGroup()
    {
        var box = MakeGroup("Данные персонажа");
        var flow = MakeFlow();

        flow.Controls.Add(MakeLabel("Имя:"));
        _txtName = MakeTextInput(220);
        flow.Controls.Add(_txtName);

        flow.Controls.Add(MakeLabel("Возраст:"));
        _txtAge = MakeTextInput(70);
        flow.Controls.Add(_txtAge);

        flow.Controls.Add(MakeLabel("Должность:"));
        _txtPosition = MakeTextInput(220);
        flow.Controls.Add(_txtPosition);

        flow.Controls.Add(MakeLabel("Навыки (по одному на строку, «Название: уровень»):"));
        _txtSkills = MakeTextInput(320, multiline: true, height: 90);
        flow.Controls.Add(_txtSkills);

        flow.Controls.Add(MakeLabel("Лор / история персонажа:"));
        _txtLore = MakeTextInput(700, multiline: true, height: 110);
        flow.Controls.Add(_txtLore);

        box.Controls.Add(flow);
        AttachAutoHeight(box, flow);
        return box;
    }

    private Control BuildExportGroup()
    {
        var box = MakeGroup("Экспорт");
        var flow = MakeFlow();

        flow.Controls.Add(MakeButton("Обновить превью", (_, _) => RenderPreview()));
        flow.Controls.Add(MakeButton("Сохранить PNG", (_, _) => SavePng()));
        flow.Controls.Add(MakeButton("Сохранить PDF", (_, _) => SavePdf()));

        box.Controls.Add(flow);
        AttachAutoHeight(box, flow);
        return box;
    }

    private Control BuildContent()
    {
        var host = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(42, 44, 59), BorderStyle = BorderStyle.FixedSingle };
        _previewBox = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
        _previewPlaceholder = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = MutedColor,
            Text = "Заполните данные и нажмите «Обновить превью»",
        };
        host.Controls.Add(_previewBox);
        host.Controls.Add(_previewPlaceholder);
        return host;
    }

    private TextBox MakeTextInput(int width, bool multiline = false, int height = 0)
    {
        var box = new TextBox { Width = width, Margin = new Padding(3, 6, 10, 3) };
        if (multiline)
        {
            box.Multiline = true;
            box.Height = height;
            box.ScrollBars = ScrollBars.Vertical;
            box.AcceptsReturn = true;
        }
        StyleInput(box);
        return box;
    }

    #endregion

    #region Actions

    private void LoadPhoto(Action<Bitmap> assign)
    {
        using var dialog = new OpenFileDialog { Filter = "PNG (*.png)|*.png|Все файлы (*.*)|*.*" };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        Bitmap loaded;
        try
        {
            using var raw = new Bitmap(dialog.FileName);
            loaded = new Bitmap(raw);
        }
        catch
        {
            MessageBox.Show(FindForm(), "Не удалось загрузить изображение: " + dialog.FileName, "Карточка персонажа",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        assign(loaded);
        UpdateSlotLabels();
    }

    private void UpdateSlotLabels()
    {
        void Set(Label lbl, Bitmap? bmp) => (lbl.Text, lbl.ForeColor) = bmp != null ? ($"{bmp.Width}x{bmp.Height} px", FgColor) : ("не загружено", MutedColor);
        Set(_lblFront, _data.Front);
        Set(_lblBack, _data.Back);
        Set(_lblLeft, _data.Left);
        Set(_lblRight, _data.Right);
    }

    private void RenderPreview()
    {
        _data.Name = _txtName.Text;
        _data.Age = _txtAge.Text;
        _data.Position = _txtPosition.Text;
        _data.Lore = _txtLore.Text;
        _data.Skills = _txtSkills.Text;

        _renderedCard?.Dispose();
        _renderedCard = CharacterCardRenderer.Render(_data);

        _previewBox.Image?.Dispose();
        _previewBox.Image = new Bitmap(_renderedCard);
        _previewPlaceholder.Visible = false;
    }

    private void SavePng()
    {
        RenderPreview();
        if (_renderedCard == null) return;

        using var dialog = new SaveFileDialog { Filter = "PNG files (*.png)|*.png", FileName = SuggestedFileName() + ".png" };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        _renderedCard.Save(dialog.FileName, System.Drawing.Imaging.ImageFormat.Png);
    }

    private void SavePdf()
    {
        RenderPreview();
        if (_renderedCard == null) return;

        using var dialog = new SaveFileDialog { Filter = "PDF files (*.pdf)|*.pdf", FileName = SuggestedFileName() + ".pdf" };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        SingleImagePdfWriter.Save(dialog.FileName, _renderedCard);
    }

    private string SuggestedFileName()
    {
        var name = string.IsNullOrWhiteSpace(_txtName.Text) ? "character_card" : _txtName.Text;
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    #endregion
}
