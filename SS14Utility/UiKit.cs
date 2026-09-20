namespace SS14Utility;

public static class UiKit
{
    public static readonly Color BgColor = Color.FromArgb(28, 30, 36);
    public static readonly Color FgColor = Color.FromArgb(224, 226, 232);
    public static readonly Color MutedColor = Color.FromArgb(150, 155, 168);
    public static readonly Color InputColor = Color.FromArgb(24, 26, 31);
    public static readonly Color BadColor = Color.FromArgb(214, 92, 92);

    public static GroupBox MakeGroup(string title) => new()
    {
        Text = title,
        Dock = DockStyle.Top,
        ForeColor = FgColor,
        Padding = new Padding(6, 4, 6, 8),
    };

    public static FlowLayoutPanel MakeFlow() => new()
    {
        Dock = DockStyle.Top,
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = true,
    };

    public static void AttachAutoHeight(GroupBox box, FlowLayoutPanel flow)
    {
        var chrome = box.Height - box.DisplayRectangle.Height;

        void Sync()
        {
            if (flow.Width <= 0) return;
            var flowHeight = flow.GetPreferredSize(new Size(flow.Width, 0)).Height;
            if (flow.Height != flowHeight) flow.Height = flowHeight;
            var newHeight = flowHeight + chrome;
            if (box.Height == newHeight) return;
            box.Height = newHeight;

            var grandparent = box.Parent;
            if (grandparent == null) return;
            if (grandparent.IsHandleCreated)
                grandparent.BeginInvoke(new Action(() =>
                {
                    if (!grandparent.IsDisposed) grandparent.PerformLayout();
                }));
            else
                grandparent.PerformLayout();
        }
        flow.Layout += (_, _) => Sync();
        box.Layout += (_, _) => Sync();
        Sync();
    }

    public static Button MakeButton(string text, EventHandler onClick)
    {
        var b = new Button { Text = text, AutoSize = true, Margin = new Padding(3) };
        StyleButton(b);
        b.Click += onClick;
        return b;
    }

    public static void StyleButton(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.BackColor = Color.FromArgb(52, 56, 66);
        b.ForeColor = FgColor;
        b.FlatAppearance.BorderColor = Color.FromArgb(70, 76, 90);
        b.UseVisualStyleBackColor = false;
    }

    public static CheckBox MakeCheck(string text) => new() { Text = text, AutoSize = true, ForeColor = FgColor, Margin = new Padding(3, 8, 3, 3) };

    public static Label MakeLabel(string text) => new() { Text = text, AutoSize = true, ForeColor = FgColor, Padding = new Padding(3, 8, 0, 0) };

    public static TrackBar MakeSlider(int min, int max, int value) => new() { Minimum = min, Maximum = max, Value = value, Width = 100, TickStyle = TickStyle.None, Margin = new Padding(3) };

    public static NumericUpDown MakeNumeric(int min, int max, int value)
    {
        var n = new NumericUpDown { Minimum = min, Maximum = max, Value = value, Width = 55, Margin = new Padding(3, 6, 3, 3) };
        n.BackColor = InputColor;
        n.ForeColor = FgColor;
        n.BorderStyle = BorderStyle.FixedSingle;
        return n;
    }

    public static void StyleInput(TextBox box)
    {
        box.BackColor = InputColor;
        box.ForeColor = FgColor;
        box.BorderStyle = BorderStyle.FixedSingle;
    }

    public static void StyleCombo(ComboBox box)
    {
        box.BackColor = InputColor;
        box.ForeColor = FgColor;
        box.FlatStyle = FlatStyle.Flat;
    }
}
