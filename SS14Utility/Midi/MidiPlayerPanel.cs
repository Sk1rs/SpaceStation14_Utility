namespace SS14Utility.Midi;

public sealed class MidiPlayerPanel : UserControl
{
    private static readonly Color BgColor = Color.FromArgb(28, 30, 36);
    private static readonly Color PanelColor = Color.FromArgb(37, 40, 48);
    private static readonly Color InputColor = Color.FromArgb(24, 26, 31);
    private static readonly Color FgColor = Color.FromArgb(224, 226, 232);
    private static readonly Color MutedColor = Color.FromArgb(150, 155, 168);
    private static readonly Color AccentColor = Color.FromArgb(88, 148, 220);
    private static readonly Color WarnColor = Color.FromArgb(226, 172, 74);
    private static readonly Color BadColor = Color.FromArgb(214, 92, 92);

    private readonly Ss14MidiEngine _engine = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 100 };

    // File list
    private readonly TextBox _fileSearch = new();
    private readonly ListBox _fileList = new();
    private readonly Label _folderLabel = new();
    private List<string> _files = new();
    private string? _folder;

    // Instruments
    private readonly TextBox _instrumentSearch = new();
    private readonly ListBox _instrumentList = new();
    private readonly Label _instrumentInfo = new();
    private readonly ComboBox _styleBox = new();
    private List<InstrumentDef> _shownInstruments = new();
    private InstrumentDef? _instrument;

    // Instrument logic
    private readonly NumericUpDown _programBox = new();
    private readonly NumericUpDown _bankBox = new();
    private readonly ComboBox _gmBox = new();
    private readonly CheckBox _percussionBox = new();
    private readonly CheckBox _programChangeBox = new();
    private readonly CheckBox _limitsBox = new();
    private readonly CheckBox _stopOnCrampBox = new();
    private readonly Label _limitsLabel = new();

    // Channels
    private readonly CheckedListBox _channelList = new();
    private readonly CheckBox _trackNamesBox = new();
    private List<MidiTrackInfo?> _tracks = new();
    private int _division = 480;

    // Transport
    private readonly Button _playButton = new();
    private readonly Button _stopButton = new();
    private readonly TrackBar _positionBar = new();
    private readonly TrackBar _volumeBar = new();
    private readonly CheckBox _loopBox = new();
    private readonly Label _positionLabel = new();
    private readonly Label _statusLabel = new();
    private readonly Label _nowPlayingLabel = new();

    private bool _updatingUi;
    private bool _seeking;
    private string? _currentFile;

    public MidiPlayerPanel()
    {
        Dock = DockStyle.Fill;
        MinimumSize = new Size(1000, 660);
        BackColor = BgColor;
        ForeColor = FgColor;
        Font = new Font("Segoe UI", 9f);
        AllowDrop = true;

        BuildUi();
        WireEvents();

        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += OnDragDrop;

        Load += OnFormLoad;
    }

    /// <summary>Called by the host form on exit, since a UserControl has no FormClosing of its own.</summary>
    public void Shutdown() => OnFormClosing();

    #region UI construction

    private void BuildUi()
    {
        var outerSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            BackColor = BgColor,
            SplitterWidth = 6,
        };
        // WinForms lays docked controls out back-to-front, so the bottom bar goes in first and the
        // filling splitter is brought to the front to get whatever space is left.
        Controls.Add(BuildTransport());
        Controls.Add(outerSplit);
        outerSplit.BringToFront();

        var innerSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            BackColor = BgColor,
            SplitterWidth = 6,
        };
        outerSplit.Panel2.Controls.Add(innerSplit);

        BuildFiles(outerSplit.Panel1);
        BuildInstruments(innerSplit.Panel1);

        var right = innerSplit.Panel2;
        right.BackColor = BgColor;
        right.Padding = new Padding(8, 8, 4, 4);
        right.Controls.Add(BuildChannels());
        right.Controls.Add(BuildLogic());

        // A UserControl has no Shown event, so defer past the current message-loop tick with
        // BeginInvoke instead - by then the host form has finished laying out the docked panel
        // and the splitters have their real width, same as Shown would have guaranteed on a Form.
        HandleCreated += (_, _) => BeginInvoke(new Action(() =>
        {
            SetupSplitter(outerSplit, 200, 320, 320);
            SetupSplitter(innerSplit, 200, 320, 330);
        }));
    }

    private static void SetupSplitter(SplitContainer split, int panel1Min, int panel2Min, int distance)
    {
        try
        {
            if (split.Width <= panel1Min + panel2Min + split.SplitterWidth)
                return;

            split.Panel1MinSize = panel1Min;
            split.Panel2MinSize = panel2Min;
            split.SplitterDistance = Math.Clamp(distance, panel1Min, split.Width - panel2Min - split.SplitterWidth);
        }
        catch (InvalidOperationException)
        {
            // Window too small for the requested split, WinForms keeps its own arrangement.
        }
    }

    private void BuildFiles(Control host)
    {
        host.BackColor = PanelColor;
        host.Padding = new Padding(10);

        _folderLabel.Dock = DockStyle.Top;
        _folderLabel.Height = 34;
        _folderLabel.ForeColor = MutedColor;
        _folderLabel.AutoEllipsis = true;

        _fileSearch.Dock = DockStyle.Top;
        StyleInput(_fileSearch);
        _fileSearch.PlaceholderText = "Поиск по названию...";

        _fileList.Dock = DockStyle.Fill;
        StyleList(_fileList);

        var fileButtons = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 112,
            BackColor = PanelColor,
            ColumnCount = 2,
            RowCount = 3,
        };
        fileButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fileButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var i = 0; i < 3; i++)
        {
            fileButtons.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 3));
        }

        fileButtons.Controls.Add(MakeGridButton("Папка...", OnPickFolder), 0, 0);
        fileButtons.Controls.Add(MakeGridButton("Файл...", OnPickFile), 1, 0);
        fileButtons.Controls.Add(MakeGridButton("Обновить", (_, _) => RefreshFiles()), 0, 1);
        fileButtons.Controls.Add(MakeGridButton("Папка игры", (_, _) => OpenGameFolder()), 1, 1);
        fileButtons.Controls.Add(MakeGridButton("Саундфонты", (_, _) => ShowSoundfonts()), 0, 2);

        host.Controls.Add(_fileList);
        host.Controls.Add(fileButtons);
        host.Controls.Add(_fileSearch);
        host.Controls.Add(_folderLabel);
        host.Controls.Add(MakeHeader("MIDI файлы"));
    }

    private void BuildInstruments(Control host)
    {
        host.BackColor = PanelColor;
        host.Padding = new Padding(10);

        _instrumentSearch.Dock = DockStyle.Top;
        StyleInput(_instrumentSearch);
        _instrumentSearch.PlaceholderText = "Поиск инструмента...";

        _instrumentList.Dock = DockStyle.Fill;
        StyleList(_instrumentList);

        _instrumentInfo.Dock = DockStyle.Bottom;
        _instrumentInfo.Height = 96;
        _instrumentInfo.ForeColor = MutedColor;

        host.Controls.Add(_instrumentList);
        host.Controls.Add(_instrumentInfo);
        host.Controls.Add(_instrumentSearch);
        host.Controls.Add(MakeHeader("Инструменты игры"));
    }

    private Control BuildLogic()
    {
        var box = new GroupBox
        {
            Dock = DockStyle.Top,
            Height = 262,
            Text = "Логика инструмента",
            ForeColor = FgColor,
            BackColor = BgColor,
            Padding = new Padding(10, 6, 10, 6),
        };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = BgColor,
            ColumnCount = 3,
            RowCount = 7,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        for (var i = 0; i < 6; i++)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        }

        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        StyleCombo(_styleBox);
        _styleBox.Dock = DockStyle.Fill;
        _styleBox.Margin = new Padding(0, 4, 8, 4);
        _styleBox.Enabled = false;

        var resetButton = MakeButton("Сбросить к прототипу", (_, _) => ApplyInstrument(_instrument, true), 180);
        resetButton.Dock = DockStyle.Fill;
        resetButton.Margin = new Padding(0, 3, 0, 3);

        StyleNumeric(_programBox);
        _programBox.Maximum = 127;
        _programBox.Width = 70;
        _programBox.Margin = new Padding(0, 5, 16, 0);

        StyleNumeric(_bankBox);
        _bankBox.Maximum = 255;
        _bankBox.Width = 70;
        _bankBox.Margin = new Padding(0, 5, 0, 0);

        var numbers = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = BgColor, Margin = new Padding(0) };
        numbers.Controls.Add(_programBox);
        numbers.Controls.Add(new Label { Text = "Bank:", ForeColor = FgColor, Width = 46, Margin = new Padding(0, 9, 4, 0) });
        numbers.Controls.Add(_bankBox);

        StyleCombo(_gmBox);
        _gmBox.Dock = DockStyle.Fill;
        _gmBox.Margin = new Padding(0, 4, 8, 4);
        foreach (var program in Catalog.Programs)
        {
            _gmBox.Items.Add(program);
        }

        StyleCheck(_percussionBox, "Разрешить перкуссию (канал 10)");
        _percussionBox.Dock = DockStyle.Fill;

        StyleCheck(_programChangeBox, "Разрешить смену программы из файла");
        _programChangeBox.Dock = DockStyle.Fill;

        StyleCheck(_limitsBox, "Играть как слышат другие (лимиты сервера)");
        _limitsBox.Dock = DockStyle.Fill;

        StyleCheck(_stopOnCrampBox, "Обрывать при судорогах");
        _stopOnCrampBox.Dock = DockStyle.Fill;
        _stopOnCrampBox.Checked = true;
        _stopOnCrampBox.Enabled = false;

        _limitsLabel.Dock = DockStyle.Fill;
        _limitsLabel.ForeColor = MutedColor;
        _limitsLabel.AutoEllipsis = true;

        grid.Controls.Add(MakeGridLabel("Стиль (ПКМ в игре):"), 0, 0);
        grid.Controls.Add(_styleBox, 1, 0);
        grid.Controls.Add(resetButton, 2, 0);

        grid.Controls.Add(MakeGridLabel("Program:"), 0, 1);
        grid.Controls.Add(numbers, 1, 1);

        grid.Controls.Add(MakeGridLabel("GM инструмент:"), 0, 2);
        grid.Controls.Add(_gmBox, 1, 2);

        grid.Controls.Add(_percussionBox, 0, 3);
        grid.SetColumnSpan(_percussionBox, 3);

        grid.Controls.Add(_programChangeBox, 0, 4);
        grid.SetColumnSpan(_programChangeBox, 3);

        grid.Controls.Add(_limitsBox, 0, 5);
        grid.SetColumnSpan(_limitsBox, 2);
        grid.Controls.Add(_stopOnCrampBox, 2, 5);

        grid.Controls.Add(_limitsLabel, 0, 6);
        grid.SetColumnSpan(_limitsLabel, 3);

        box.Controls.Add(grid);
        return box;
    }

    private static Label MakeGridLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = FgColor,
            TextAlign = ContentAlignment.MiddleLeft,
        };
    }

    private Control BuildChannels()
    {
        var box = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "Каналы MIDI",
            ForeColor = FgColor,
            BackColor = BgColor,
            Padding = new Padding(10, 6, 10, 10),
        };

        _channelList.Dock = DockStyle.Fill;
        _channelList.BackColor = InputColor;
        _channelList.ForeColor = FgColor;
        _channelList.BorderStyle = BorderStyle.FixedSingle;
        _channelList.CheckOnClick = true;
        _channelList.IntegralHeight = false;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, BackColor = BgColor };
        buttons.Controls.Add(MakeButton("Все", (_, _) => SetAllChannels(true), 80));
        buttons.Controls.Add(MakeButton("Ни одного", (_, _) => SetAllChannels(false), 90));

        _trackNamesBox.AutoSize = true;
        _trackNamesBox.Margin = new Padding(12, 8, 0, 0);
        StyleCheck(_trackNamesBox, "Показывать названия треков");
        buttons.Controls.Add(_trackNamesBox);

        box.Controls.Add(_channelList);
        box.Controls.Add(buttons);
        return box;
    }

    private Control BuildTransport()
    {
        // Docked rows instead of fixed coordinates, so nothing overlaps when the window is resized.
        var panel = new Panel { Dock = DockStyle.Bottom, Height = 152, BackColor = PanelColor, Padding = new Padding(12, 8, 12, 8) };

        _nowPlayingLabel.Dock = DockStyle.Top;
        _nowPlayingLabel.Height = 22;
        _nowPlayingLabel.Text = "Файл не выбран";
        _nowPlayingLabel.AutoEllipsis = true;

        StyleButton(_playButton);
        _playButton.Text = "▶ Играть";
        _playButton.Dock = DockStyle.Fill;
        _playButton.Margin = new Padding(0, 3, 6, 3);

        StyleButton(_stopButton);
        _stopButton.Text = "■ Стоп";
        _stopButton.Dock = DockStyle.Fill;
        _stopButton.Margin = new Padding(0, 3, 12, 3);

        _positionBar.Dock = DockStyle.Fill;
        _positionBar.BackColor = PanelColor;
        _positionBar.TickStyle = TickStyle.None;
        _positionBar.Maximum = 1;

        StyleCheck(_loopBox, "Повтор");
        _loopBox.Dock = DockStyle.Fill;
        _loopBox.Margin = new Padding(12, 8, 6, 0);

        var panicButton = MakeButton("Паника (сброс нот)", (_, _) => { _engine.StopAllNotes(); _engine.SystemReset(); }, 160);
        panicButton.Dock = DockStyle.Fill;
        panicButton.Margin = new Padding(0, 3, 0, 3);

        var controlsRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            BackColor = PanelColor,
            ColumnCount = 5,
            RowCount = 1,
        };
        controlsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        controlsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        controlsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        controlsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        controlsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        controlsRow.Controls.Add(_playButton, 0, 0);
        controlsRow.Controls.Add(_stopButton, 1, 0);
        controlsRow.Controls.Add(_positionBar, 2, 0);
        controlsRow.Controls.Add(_loopBox, 3, 0);
        controlsRow.Controls.Add(panicButton, 4, 0);

        _positionLabel.Dock = DockStyle.Fill;
        _positionLabel.ForeColor = MutedColor;
        _positionLabel.Text = "0:00 / 0:00";
        _positionLabel.TextAlign = ContentAlignment.MiddleLeft;

        var volumeLabel = new Label
        {
            Text = "Громкость",
            Dock = DockStyle.Fill,
            ForeColor = FgColor,
            TextAlign = ContentAlignment.MiddleRight,
        };

        _volumeBar.Dock = DockStyle.Fill;
        _volumeBar.BackColor = PanelColor;
        _volumeBar.TickStyle = TickStyle.None;
        _volumeBar.Maximum = 200;
        _volumeBar.Value = 50;

        var volumeRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 34,
            BackColor = PanelColor,
            ColumnCount = 3,
            RowCount = 1,
        };
        volumeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        volumeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        volumeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        volumeRow.Controls.Add(_positionLabel, 0, 0);
        volumeRow.Controls.Add(volumeLabel, 1, 0);
        volumeRow.Controls.Add(_volumeBar, 2, 0);

        _statusLabel.Dock = DockStyle.Top;
        _statusLabel.Height = 22;
        _statusLabel.ForeColor = MutedColor;
        _statusLabel.AutoEllipsis = true;

        // Docked top controls stack in reverse order of adding.
        panel.Controls.Add(_statusLabel);
        panel.Controls.Add(volumeRow);
        panel.Controls.Add(controlsRow);
        panel.Controls.Add(_nowPlayingLabel);

        return panel;
    }

    private static Panel MakePanel(DockStyle dock, int width)
    {
        return new Panel { Dock = dock, Width = width, BackColor = PanelColor };
    }

    private static Label MakeHeader(string text)
    {
        return new Label
        {
            Dock = DockStyle.Top,
            Height = 26,
            Text = text,
            Font = new Font("Segoe UI Semibold", 10.5f),
            ForeColor = Color.White,
        };
    }

    private Button MakeGridButton(string text, EventHandler onClick)
    {
        var button = MakeButton(text, onClick, 100);
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(0, 2, 4, 2);
        return button;
    }

    private Button MakeButton(string text, EventHandler onClick, int width)
    {
        var button = new Button { Text = text, Width = width, Height = 30, Margin = new Padding(0, 4, 6, 0) };
        StyleButton(button);
        button.Click += onClick;
        return button;
    }

    private static void StyleButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Color.FromArgb(52, 56, 66);
        button.ForeColor = FgColor;
        button.FlatAppearance.BorderColor = Color.FromArgb(70, 76, 90);
        button.UseVisualStyleBackColor = false;
    }

    private static void StyleInput(TextBox box)
    {
        box.BackColor = InputColor;
        box.ForeColor = FgColor;
        box.BorderStyle = BorderStyle.FixedSingle;
    }

    private static void StyleList(ListBox list)
    {
        list.BackColor = InputColor;
        list.ForeColor = FgColor;
        list.BorderStyle = BorderStyle.FixedSingle;
        list.IntegralHeight = false;
        list.HorizontalScrollbar = true;
    }

    private static void StyleCombo(ComboBox box)
    {
        box.BackColor = InputColor;
        box.ForeColor = FgColor;
        box.FlatStyle = FlatStyle.Flat;
        box.DropDownStyle = ComboBoxStyle.DropDownList;
    }

    private static void StyleNumeric(NumericUpDown box)
    {
        box.BackColor = InputColor;
        box.ForeColor = FgColor;
        box.BorderStyle = BorderStyle.FixedSingle;
    }

    private static void StyleCheck(CheckBox box, string text)
    {
        box.Text = text;
        box.ForeColor = FgColor;
        box.FlatStyle = FlatStyle.Flat;
    }

    #endregion

    #region Wiring

    private void WireEvents()
    {
        _fileSearch.TextChanged += (_, _) => PopulateFiles();
        _fileList.DoubleClick += (_, _) => PlaySelectedFile();
        _fileList.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
                PlaySelectedFile();
        };

        _instrumentSearch.TextChanged += (_, _) => PopulateInstruments();
        _instrumentList.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingUi || _instrumentList.SelectedIndex < 0)
                return;

            ApplyInstrument(_shownInstruments[_instrumentList.SelectedIndex], true);
        };

        _styleBox.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingUi || _styleBox.SelectedItem is not InstrumentStyle style)
                return;

            SetProgram(style.Program, style.Bank);
        };

        _programBox.ValueChanged += (_, _) =>
        {
            if (_updatingUi)
                return;

            SetProgram((byte) _programBox.Value, (byte) _bankBox.Value);
        };

        _bankBox.ValueChanged += (_, _) =>
        {
            if (_updatingUi)
                return;

            SetProgram((byte) _programBox.Value, (byte) _bankBox.Value);
        };

        _gmBox.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingUi || _gmBox.SelectedItem is not ProgramDef def)
                return;

            SetProgram((byte) def.Program, (byte) _bankBox.Value);
        };

        _percussionBox.CheckedChanged += (_, _) =>
        {
            if (_updatingUi)
                return;

            _engine.DisablePercussionChannel = !_percussionBox.Checked;
            if (!_percussionBox.Checked)
                _engine.SendMidiEvent(PlayerEvent.AllNotesOff(Ss14MidiEngine.PercussionChannel));

            SyncChannelChecks();
        };

        _programChangeBox.CheckedChanged += (_, _) =>
        {
            if (_updatingUi)
                return;

            _engine.DisableProgramChangeEvent = !_programChangeBox.Checked;
            if (!_programChangeBox.Checked)
            {
                _engine.MidiBank = (byte) _bankBox.Value;
                _engine.MidiProgram = (byte) _programBox.Value;
            }
        };

        _limitsBox.CheckedChanged += (_, _) =>
        {
            _engine.Limits.Enabled = _limitsBox.Checked;
            _stopOnCrampBox.Enabled = _limitsBox.Checked;
        };

        _stopOnCrampBox.CheckedChanged += (_, _) => _engine.Limits.StopWhenCramped = _stopOnCrampBox.Checked;

        _trackNamesBox.CheckedChanged += (_, _) => PopulateChannels();

        _channelList.ItemCheck += OnChannelCheck;

        _playButton.Click += (_, _) => TogglePlay();
        _stopButton.Click += (_, _) => StopPlayback();

        _loopBox.CheckedChanged += (_, _) => _engine.LoopMidi = _loopBox.Checked;

        _volumeBar.ValueChanged += (_, _) => _engine.Gain = _volumeBar.Value / 100f;

        _positionBar.MouseDown += (_, _) => _seeking = true;
        _positionBar.MouseUp += (_, _) =>
        {
            _seeking = false;
            if (_engine.IsFileOpen)
                _engine.PlayerTick = _positionBar.Value;
        };

        _uiTimer.Tick += (_, _) => UpdateUi();

        _engine.PlaybackFinished += () => BeginInvoke(OnPlaybackFinished);
        _engine.Limits.Cramped += () => BeginInvoke(OnCramped);
    }

    private void OnFormLoad(object? sender, EventArgs e)
    {
        try
        {
            _engine.LoadSoundfonts(Paths.SoundfontLoadOrder());
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Не удалось запустить синтезатор:" + Environment.NewLine + ex.Message,
                "SS14 MIDI плеер", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        _folder = Directory.Exists(_settings.MidiFolder ?? "")
            ? _settings.MidiFolder
            : Paths.DefaultMidiFolders().FirstOrDefault();

        _volumeBar.Value = (int) Math.Clamp(_settings.Volume * 100f, 0, 200);
        _engine.Gain = _volumeBar.Value / 100f;
        _loopBox.Checked = _settings.Loop;
        _limitsBox.Checked = _settings.SimulateLimits;
        _trackNamesBox.Checked = _settings.TrackNames;

        _statusLabel.Text = "Аудио: " + _engine.AudioDriverName
                            + " · саундфонтов: " + _engine.LoadedSoundfonts.Count
                            + " · инструментов: " + Catalog.Instruments.Count
                            + " · данные из билда МК (dead-space-server/space-station-14-soyuz)";

        PopulateInstruments();

        var saved = Catalog.Instruments.FirstOrDefault(x => x.Id == _settings.InstrumentId)
                    ?? Catalog.Instruments.FirstOrDefault(x => x.Id == "PianoInstrument")
                    ?? Catalog.Instruments.First();

        SelectInstrument(saved);
        RefreshFiles();
        PopulateChannels();

        _uiTimer.Start();
    }

    private void OnFormClosing()
    {
        _uiTimer.Stop();

        _settings.MidiFolder = _folder;
        _settings.InstrumentId = _instrument?.Id;
        _settings.Volume = _volumeBar.Value / 100f;
        _settings.Loop = _loopBox.Checked;
        _settings.SimulateLimits = _limitsBox.Checked;
        _settings.TrackNames = _trackNamesBox.Checked;
        _settings.Save();

        _engine.Dispose();
    }

    #endregion

    #region Files

    private void OnPickFolder(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Папка с MIDI файлами",
            SelectedPath = _folder ?? "",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _folder = dialog.SelectedPath;
        RefreshFiles();
    }

    private void OnPickFile(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "MIDI файлы (*.mid;*.midi)|*.mid;*.midi|Все файлы (*.*)|*.*",
            InitialDirectory = _folder ?? "",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        PlayFile(dialog.FileName);
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
            return;

        if (Directory.Exists(paths[0]))
        {
            _folder = paths[0];
            RefreshFiles();
            return;
        }

        PlayFile(paths[0]);
    }

    private void RefreshFiles()
    {
        _files = new List<string>();

        if (!string.IsNullOrEmpty(_folder) && Directory.Exists(_folder))
        {
            try
            {
                _files = Directory
                    .EnumerateFiles(_folder, "*.*", SearchOption.AllDirectories)
                    .Where(x => x.EndsWith(".mid", StringComparison.OrdinalIgnoreCase)
                                || x.EndsWith(".midi", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
            catch (Exception e)
            {
                _statusLabel.Text = "Не удалось прочитать папку: " + e.Message;
            }
        }

        _folderLabel.Text = _folder ?? "Папка не выбрана";
        PopulateFiles();
    }

    private void PopulateFiles()
    {
        var filter = _fileSearch.Text.Trim();

        _fileList.BeginUpdate();
        _fileList.Items.Clear();

        foreach (var file in _files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (filter.Length > 0 && name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) < 0)
                continue;

            _fileList.Items.Add(new FileEntry(file, name));
        }

        _fileList.EndUpdate();
    }

    private sealed record FileEntry(string Path, string Name)
    {
        public override string ToString() => Name;
    }

    private void PlaySelectedFile()
    {
        if (_fileList.SelectedItem is FileEntry entry)
            PlayFile(entry.Path);
    }

    private void PlayFile(string path)
    {
        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch (Exception e)
        {
            _statusLabel.Text = "Не удалось открыть файл: " + e.Message;
            return;
        }

        if (data.Length > Ss14MidiEngine.MidiSizeLimit)
        {
            var answer = MessageBox.Show(this,
                "Файл весит " + (data.Length / 1000000d).ToString("0.00") + " МБ, игра принимает только до 2 МБ." +
                Environment.NewLine + "Проиграть его здесь всё равно?",
                "Больше игрового лимита", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (answer != DialogResult.Yes)
                return;
        }

        _engine.CloseMidi();

        // The game parses the file for channel names before playback starts.
        if (MidiFileParser.TryGetTracks(data, out var tracks, out var division, out var parseError))
        {
            _tracks = tracks;
            _division = division > 0 ? division : 480;
        }
        else
        {
            _tracks = new List<MidiTrackInfo?>();
            _division = 480;
            _statusLabel.Text = "Не удалось разобрать треки: " + parseError;
        }

        PopulateChannels();

        // Force the file to start on the instrument's own program, exactly like the renderer does.
        ReapplyProgramLock();

        if (!OpenMidiChecked(data))
            return;

        _currentFile = path;
        _nowPlayingLabel.Text = "Играет: " + Path.GetFileName(path);
        _playButton.Text = "⏸ Пауза";
    }

    private bool OpenMidiChecked(byte[] data)
    {
        try
        {
            if (_engine.OpenMidi(data, out var error))
                return true;

            _statusLabel.Text = error ?? "Не удалось открыть MIDI.";
            return false;
        }
        catch (Exception e)
        {
            _statusLabel.Text = "Ошибка воспроизведения: " + e.Message;
            return false;
        }
    }

    private void OpenGameFolder()
    {
        var folder = Path.Combine(Paths.GameData, "MIDI");
        if (!Directory.Exists(folder))
        {
            MessageBox.Show(this, "Папка игры не найдена: " + folder, "SS14 MIDI плеер");
            return;
        }

        _folder = folder;
        RefreshFiles();
    }

    private void ShowSoundfonts()
    {
        var loaded = _engine.LoadedSoundfonts;
        var text = loaded.Count == 0
            ? "Ни один саундфонт не загружен — звука не будет."
            : "Саундфонты загружены в том же порядке, что и в игре" + Environment.NewLine +
              "(каждый следующий перекрывает предыдущий):" + Environment.NewLine + Environment.NewLine +
              string.Join(Environment.NewLine, loaded.Select((x, i) => (i + 1) + ". " + x)) + Environment.NewLine + Environment.NewLine +
              "Свои саундфонты можно положить в " + Path.Combine(Paths.GameData, "soundfonts") +
              " — игра и плеер подхватят их одинаково.";

        MessageBox.Show(this, text, "Саундфонты", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    #endregion

    #region Instruments

    private void PopulateInstruments()
    {
        var filter = _instrumentSearch.Text.Trim();

        _shownInstruments = Catalog.Instruments
            .Where(x => filter.Length == 0
                        || x.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
                        || x.DisplayName.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
                        || x.Id.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        _updatingUi = true;
        _instrumentList.BeginUpdate();
        _instrumentList.Items.Clear();
        foreach (var instrument in _shownInstruments)
        {
            _instrumentList.Items.Add(instrument);
        }

        _instrumentList.EndUpdate();

        if (_instrument != null)
        {
            var index = _shownInstruments.FindIndex(x => x.Id == _instrument.Id);
            if (index >= 0)
                _instrumentList.SelectedIndex = index;
        }

        _updatingUi = false;
    }

    private void SelectInstrument(InstrumentDef instrument)
    {
        var index = _shownInstruments.FindIndex(x => x.Id == instrument.Id);
        if (index >= 0)
        {
            _updatingUi = true;
            _instrumentList.SelectedIndex = index;
            _updatingUi = false;
        }

        ApplyInstrument(instrument, true);
    }

    /// <summary>
    ///     Applies the prototype the way InstrumentSystem.UpdateRenderer does, then mirrors it into the UI.
    /// </summary>
    private void ApplyInstrument(InstrumentDef? instrument, bool resetOverrides)
    {
        if (instrument == null)
            return;

        _instrument = instrument;
        _updatingUi = true;

        if (resetOverrides)
        {
            _percussionBox.Checked = instrument.AllowPercussion;
            _programChangeBox.Checked = instrument.AllowProgramChange;
            _programBox.Value = instrument.Program;
            _bankBox.Value = instrument.Bank;
            _gmBox.SelectedIndex = instrument.Program;
        }

        _styleBox.Items.Clear();
        var styles = instrument.Styles;
        if (styles is { Count: > 0 })
        {
            foreach (var style in styles)
            {
                _styleBox.Items.Add(style);
            }

            _styleBox.Enabled = true;
            var current = styles.FindIndex(x => x.Program == (byte) _programBox.Value && x.Bank == (byte) _bankBox.Value);
            _styleBox.SelectedIndex = current >= 0 ? current : 0;
        }
        else
        {
            _styleBox.Items.Add("— у этого инструмента стилей нет —");
            _styleBox.SelectedIndex = 0;
            _styleBox.Enabled = false;
        }

        _engine.Limits.RespectMidiLimits = instrument.RespectMidiLimits;

        _instrumentInfo.Text = string.Join(Environment.NewLine, new[]
        {
            "Прототип: " + instrument.Id,
            "Program " + instrument.Program + " / Bank " + instrument.Bank + " — " + Catalog.ProgramName(instrument.Program),
            "Перкуссия: " + (instrument.AllowPercussion ? "да" : "нет")
            + " · Смена программы: " + (instrument.AllowProgramChange ? "да" : "нет")
            + " · Лимиты: " + (instrument.RespectMidiLimits ? "да" : "нет"),
            instrument.Styles is { Count: > 0 } ? "Стилей: " + instrument.Styles.Count : "Стилей нет",
        });

        _updatingUi = false;

        _engine.ApplyInstrument(
            (byte) _programBox.Value,
            (byte) _bankBox.Value,
            _percussionBox.Checked,
            _programChangeBox.Checked);

        SyncChannelChecks();
    }

    private void SetProgram(byte program, byte bank)
    {
        _updatingUi = true;
        _programBox.Value = program;
        _bankBox.Value = bank;
        _gmBox.SelectedIndex = program;

        if (_styleBox.Enabled && _instrument?.Styles != null)
        {
            var index = _instrument.Styles.FindIndex(x => x.Program == program && x.Bank == bank);
            _styleBox.SelectedIndex = index;
        }

        _updatingUi = false;

        _engine.MidiBank = bank;
        _engine.MidiProgram = program;
    }

    private void ReapplyProgramLock()
    {
        if (_programChangeBox.Checked)
            return;

        _engine.MidiBank = (byte) _bankBox.Value;
        _engine.MidiProgram = (byte) _programBox.Value;
    }

    #endregion

    #region Channels

    private void PopulateChannels()
    {
        _updatingUi = true;
        _channelList.BeginUpdate();
        _channelList.Items.Clear();

        for (var i = 0; i < Ss14MidiEngine.ChannelCount; i++)
        {
            var track = i < _tracks.Count ? _tracks[i] : null;
            var label = track?.Label(i, _trackNamesBox.Checked) ?? "MIDI канал " + i;

            if (i == Ss14MidiEngine.PercussionChannel)
                label += "  [перкуссия]";

            _channelList.Items.Add(label, !_engine.FilteredChannels[i]);
        }

        _channelList.EndUpdate();
        _updatingUi = false;
    }

    private void SyncChannelChecks()
    {
        if (_channelList.Items.Count == 0)
            return;

        _updatingUi = true;
        for (var i = 0; i < Ss14MidiEngine.ChannelCount; i++)
        {
            _channelList.SetItemChecked(i, !_engine.FilteredChannels[i]);
        }

        _updatingUi = false;
    }

    private void OnChannelCheck(object? sender, ItemCheckEventArgs e)
    {
        if (_updatingUi)
            return;

        var filtered = e.NewValue != CheckState.Checked;
        _engine.SetFilteredChannel(e.Index, filtered);

        if (e.Index == Ss14MidiEngine.PercussionChannel)
        {
            _updatingUi = true;
            _percussionBox.Checked = !filtered;
            _updatingUi = false;
        }
    }

    private void SetAllChannels(bool enabled)
    {
        for (var i = 0; i < Ss14MidiEngine.ChannelCount; i++)
        {
            _engine.SetFilteredChannel(i, !enabled);
        }

        _updatingUi = true;
        _percussionBox.Checked = enabled;
        _updatingUi = false;
        SyncChannelChecks();
    }

    #endregion

    #region Transport

    private void TogglePlay()
    {
        if (!_engine.IsFileOpen)
        {
            PlaySelectedFile();
            return;
        }

        if (_engine.IsPlaying)
        {
            _engine.Pause();
            _playButton.Text = "▶ Играть";
        }
        else
        {
            _engine.Resume();
            _playButton.Text = "⏸ Пауза";
        }
    }

    private void StopPlayback()
    {
        _engine.CloseMidi();
        _playButton.Text = "▶ Играть";
        _positionBar.Value = 0;
        _nowPlayingLabel.Text = _currentFile != null
            ? "Остановлено: " + Path.GetFileName(_currentFile)
            : "Файл не выбран";
    }

    private void OnPlaybackFinished()
    {
        if (_loopBox.Checked)
            return;

        _playButton.Text = "▶ Играть";
        _nowPlayingLabel.Text = _currentFile != null
            ? "Закончено: " + Path.GetFileName(_currentFile)
            : "Файл не выбран";
    }

    private void OnCramped()
    {
        _statusLabel.ForeColor = BadColor;
        _statusLabel.Text = "В игре здесь бы свело пальцы: сервер перестал бы передавать ноты, а игрока оглушило бы.";

        if (_stopOnCrampBox.Checked && _limitsBox.Checked)
            StopPlayback();
    }

    private void UpdateUi()
    {
        if (_engine.IsFileOpen && !_seeking)
        {
            var total = Math.Max(_engine.PlayerTotalTick, 1);
            var tick = Math.Clamp(_engine.PlayerTick, 0, total);

            _positionBar.Maximum = total;
            _positionBar.Value = tick;

            var bpm = Math.Max(_engine.Bpm, 1);
            _positionLabel.Text = FormatTime(tick, bpm) + " / " + FormatTime(total, bpm)
                                  + "   ·   тик " + tick + "/" + total + "   ·   " + bpm + " BPM";
        }

        UpdateLimitsLabel();
    }

    private string FormatTime(int ticks, int bpm)
    {
        var seconds = ticks / (double) _division * (60d / bpm);
        var span = TimeSpan.FromSeconds(double.IsFinite(seconds) ? seconds : 0);
        return ((int) span.TotalMinutes) + ":" + span.Seconds.ToString("00");
    }

    private void UpdateLimitsLabel()
    {
        var limits = _engine.Limits;

        if (!limits.Enabled)
        {
            _limitsLabel.ForeColor = MutedColor;
            _limitsLabel.Text = "Сейчас играет так, как слышит сам исполнитель — без лимитов.";
            return;
        }

        var text = "События/сек: " + limits.EventsPerSecond
                   + "   передано: " + limits.SentPerSecond
                   + "   очередь: " + limits.QueueLength
                   + "   задержка: " + limits.DelayMs + " мс";

        switch (limits.Status)
        {
            case LimitStatus.Stopped:
                _limitsLabel.ForeColor = BadColor;
                text += "   — оборвалось (судороги)";
                break;
            case LimitStatus.Cramping:
                _limitsLabel.ForeColor = BadColor;
                text += "   — пальцы сводит, вот-вот оборвётся";
                break;
            case LimitStatus.Lagging:
                _limitsLabel.ForeColor = WarnColor;
                text += "   — не влезает в лимит, отстаёт";
                break;
            default:
                _limitsLabel.ForeColor = AccentColor;
                text += "   — влезает в лимиты";
                break;
        }

        _limitsLabel.Text = text;
    }

    #endregion
}
