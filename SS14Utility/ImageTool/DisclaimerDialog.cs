namespace SS14Utility.ImageTool;

/// <summary>Shown once on startup: what the image tool is based on, and who ported/modified it.</summary>
public sealed class DisclaimerDialog : Form
{
    private const string OriginalRepoUrl = "https://github.com/Tunguso4ka/SSfyImage";
    private const string ModderUrl = "https://github.com/Sk1rs";
    private int _secondsLeft = 3;
    private readonly Button _continueButton = new() { Dock = DockStyle.Bottom, Height = 32 };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };

    public DisclaimerDialog()
    {
        Text = "SS14 Utility — Картинка → текст";
        Size = new Size(420, 220);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(30, 31, 38);
        ForeColor = Color.FromArgb(232, 232, 236);

        var label = new LinkLabel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
            Text = "Модуль \"Картинка → текст\" основан на SSfyImage автора Tunguso4ka.\n\n"
                   + "Портирован на C# и доработан Sk1rs.",
            LinkColor = Color.FromArgb(140, 180, 255),
        };
        label.Links.Add(label.Text.IndexOf("SSfyImage", StringComparison.Ordinal), "SSfyImage".Length, OriginalRepoUrl);
        label.Links.Add(label.Text.LastIndexOf("Sk1rs", StringComparison.Ordinal), "Sk1rs".Length, ModderUrl);
        label.LinkClicked += (_, e) =>
        {
            if (e.Link?.LinkData is string url)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        };
        Controls.Add(label);

        _continueButton.Enabled = false;
        _continueButton.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        Controls.Add(_continueButton);

        UpdateButtonText();
        _timer.Tick += (_, _) =>
        {
            _secondsLeft--;
            if (_secondsLeft <= 0) _timer.Stop();
            UpdateButtonText();
        };
        _timer.Start();

        FormClosing += (_, e) =>
        {
            if (!_continueButton.Enabled) e.Cancel = true;
        };
    }

    private void UpdateButtonText()
    {
        if (_secondsLeft > 0)
        {
            _continueButton.Text = $"Продолжить ({_secondsLeft})";
        }
        else
        {
            _continueButton.Text = "Продолжить";
            _continueButton.Enabled = true;
        }
    }
}
