using SS14Utility.CharacterCard;
using SS14Utility.ImageTool;
using SS14Utility.Midi;

namespace SS14Utility;

public sealed class AppMainForm : Form
{
    private readonly MidiPlayerPanel _midiPanel = new();
    private readonly ImageToolPanel _imagePanel = new();
    private readonly CharacterCardPanel _cardPanel = new();

    public AppMainForm()
    {
        Text = "SS14 Utility";
        MinimumSize = new Size(1000, 660);
        Size = new Size(1240, 780);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        var tabs = new TabControl { Dock = DockStyle.Fill };

        var midiTab = new TabPage("MIDI-плеер");
        midiTab.Controls.Add(_midiPanel);
        tabs.TabPages.Add(midiTab);

        var imageTab = new TabPage("Картинка → текст");
        imageTab.Controls.Add(_imagePanel);
        tabs.TabPages.Add(imageTab);

        var cardTab = new TabPage("Карточка персонажа");
        cardTab.Controls.Add(_cardPanel);
        tabs.TabPages.Add(cardTab);

        Controls.Add(tabs);

        FormClosing += (_, _) => _midiPanel.Shutdown();
    }
}
