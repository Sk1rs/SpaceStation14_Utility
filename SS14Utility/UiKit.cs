namespace SS14Utility;

/// <summary>
///     Shared dark-theme layout helpers for the tab panels (originally written for ImageToolPanel,
///     reused by CharacterCardPanel so they don't duplicate the same code - and the same bugs).
/// </summary>
public static class UiKit
{
    public static readonly Color BgColor = Color.FromArgb(28, 30, 36);
    public static readonly Color FgColor = Color.FromArgb(224, 226, 232);
    public static readonly Color MutedColor = Color.FromArgb(150, 155, 168);
    public static readonly Color InputColor = Color.FromArgb(24, 26, 31);
    public static readonly Color BadColor = Color.FromArgb(214, 92, 92);

    // Dock.Top gives the box the parent's full width while AutoSize/GrowAndShrink lets its height
    // follow however tall its content (a wrapping FlowLayoutPanel) turns out to be at that width -
    // fixed pixel heights can't predict how many rows the buttons wrap to at different window sizes.
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

    /// <summary>
    ///     Keeps `box` tall enough for `flow`'s wrapped content. GroupBox.AutoSize measuring a Dock.Top
    ///     FlowLayoutPanel child looks right at first glance but under-measures once the box's actual
    ///     width (only known after Dock.Top stretches it) causes an extra wrap row, so height is instead
    ///     tracked explicitly from the flow panel's own (width-dependent) preferred size.
    /// </summary>
    public static void AttachAutoHeight(GroupBox box, FlowLayoutPanel flow)
    {
        // GroupBox reserves a fixed band above DisplayRectangle for its caption text, on top of
        // Padding - using only Padding.Top/Bottom (as an earlier version of this did) silently
        // under-sizes the box by the caption's height, clipping the last few pixels of content.
        // That gap is constant regardless of the box's current Height, so it's safe to measure once.
        var chrome = box.Height - box.DisplayRectangle.Height;

        void Sync()
        {
            // The parameterless PreferredSize ignores flow's actual current width, so a wrapping
            // FlowLayoutPanel always measures itself as if everything fit on one (very wide) row.
            // GetPreferredSize(width, 0) constrains the measurement to the real width so wrapped
            // rows are actually counted - 0 in the height slot means "unconstrained".
            if (flow.Width <= 0) return;
            var flowHeight = flow.GetPreferredSize(new Size(flow.Width, 0)).Height;
            if (flow.Height != flowHeight) flow.Height = flowHeight;
            var newHeight = flowHeight + chrome;
            if (box.Height == newHeight) return;
            box.Height = newHeight;

            // Resizing `box` from inside its own Layout event leaves the grandparent's Fill-docked
            // sibling (e.g. the preview panel below these group boxes) computed against the stale
            // pre-resize size. A same-frame PerformLayout() call here is a no-op - the grandparent's
            // own layout pass is still on the call stack above us, and PerformLayout re-entrancy
            // during an active pass is silently dropped - so the extra pass has to be deferred past
            // it via BeginInvoke. Before the control tree has a window handle (still under
            // construction), there's no active pass to re-enter, so a direct call is fine there.
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
