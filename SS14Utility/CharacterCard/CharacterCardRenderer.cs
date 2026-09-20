using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace SS14Utility.CharacterCard;

/// <summary>Draws a D&D-style character sheet card from <see cref="CharacterCardData" /> onto a Bitmap.</summary>
public static class CharacterCardRenderer
{
    public const int CardWidth = 1240;
    public const int CardHeight = 1754; // ~A4 @ 150 DPI

    private static readonly Color ParchmentLight = Color.FromArgb(250, 240, 217);
    private static readonly Color ParchmentDark = Color.FromArgb(210, 186, 142);
    private static readonly Color Ink = Color.FromArgb(66, 40, 20);
    private static readonly Color InkFaint = Color.FromArgb(120, 90, 60);
    private static readonly Color Gold = Color.FromArgb(150, 108, 35);
    private static readonly Color PanelFill = Color.FromArgb(180, 235, 222, 194);
    private static readonly Color StripeFill = Color.FromArgb(18, 0, 0, 0);

    private const string HeaderFontFamily = "Georgia";
    private const string BodyFontFamily = "Georgia";

    public static Bitmap Render(CharacterCardData data)
    {
        var bmp = new Bitmap(CardWidth, CardHeight);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        const int margin = 44;
        var frame = new Rectangle(margin, margin, CardWidth - margin * 2, CardHeight - margin * 2);

        DrawBackground(g);
        DrawOrnateBorder(g, frame);

        var cursorY = frame.Top + 26;
        cursorY = DrawHeader(g, frame, data, cursorY);

        var portraitBox = new Rectangle(frame.Left + 24, cursorY, 470, 470);
        DrawPortraitArea(g, portraitBox, data);

        var skillsBox = new Rectangle(portraitBox.Right + 30, cursorY, frame.Right - 24 - (portraitBox.Right + 30), 470);
        DrawSkillsPanel(g, skillsBox, data);

        var loreTop = portraitBox.Bottom + 30;
        var loreBox = new Rectangle(frame.Left + 24, loreTop, frame.Width - 48, frame.Bottom - 70 - loreTop);
        DrawLorePanel(g, loreBox, data);

        DrawFooter(g, frame);

        return bmp;
    }

    private static void DrawBackground(Graphics g)
    {
        using (var baseBrush = new LinearGradientBrush(new Point(0, 0), new Point(CardWidth, CardHeight), ParchmentLight, ParchmentDark))
            g.FillRectangle(baseBrush, 0, 0, CardWidth, CardHeight);

        // Soft vignette toward the edges - fakes an aged-paper look without needing a texture asset.
        using var path = new GraphicsPath();
        path.AddEllipse(-CardWidth * 0.3f, -CardHeight * 0.25f, CardWidth * 1.6f, CardHeight * 1.5f);
        using var vignette = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb(0, 0, 0, 0),
            SurroundColors = new[] { Color.FromArgb(70, 60, 35, 10) },
        };
        g.FillRectangle(vignette, 0, 0, CardWidth, CardHeight);
    }

    private static void DrawOrnateBorder(Graphics g, Rectangle frame)
    {
        using var outerPen = new Pen(Ink, 5);
        g.DrawRectangle(outerPen, frame);

        var inner = Rectangle.Inflate(frame, -14, -14);
        using var innerPen = new Pen(Gold, 2);
        g.DrawRectangle(innerPen, inner);

        foreach (var (cx, cy) in new[]
                 {
                     (frame.Left, frame.Top), (frame.Right, frame.Top),
                     (frame.Left, frame.Bottom), (frame.Right, frame.Bottom),
                 })
            DrawCornerDiamond(g, cx, cy);
    }

    private static void DrawCornerDiamond(Graphics g, int cx, int cy)
    {
        const int r = 16;
        Span<Point> pts = stackalloc Point[4]
        {
            new Point(cx, cy - r), new Point(cx + r, cy), new Point(cx, cy + r), new Point(cx - r, cy),
        };
        using var brush = new SolidBrush(Gold);
        g.FillPolygon(brush, pts.ToArray());
        using var pen = new Pen(Ink, 2);
        g.DrawPolygon(pen, pts.ToArray());
    }

    private static int DrawHeader(Graphics g, Rectangle frame, CharacterCardData data, int y)
    {
        var name = string.IsNullOrWhiteSpace(data.Name) ? "Безымянный" : data.Name;

        using var nameFont = FitSingleLineFont(g, name, HeaderFontFamily, FontStyle.Bold, 54, frame.Width - 80);
        using var nameBrush = new SolidBrush(Ink);
        var nameSize = g.MeasureString(name, nameFont);
        g.DrawString(name, nameFont, nameBrush, frame.Left + (frame.Width - nameSize.Width) / 2, y);
        y += (int)nameSize.Height + 6;

        // Decorative rule with a diamond at the centre.
        var ruleY = y + 6;
        using (var rulePen = new Pen(Gold, 2))
        {
            g.DrawLine(rulePen, frame.Left + 60, ruleY, frame.Left + frame.Width / 2 - 16, ruleY);
            g.DrawLine(rulePen, frame.Left + frame.Width / 2 + 16, ruleY, frame.Right - 60, ruleY);
        }
        DrawCornerDiamond(g, frame.Left + frame.Width / 2, ruleY);
        y = ruleY + 22;

        var subtitleParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(data.Age)) subtitleParts.Add($"Возраст: {data.Age}");
        if (!string.IsNullOrWhiteSpace(data.Position)) subtitleParts.Add($"Должность: {data.Position}");
        var subtitle = string.Join("   •   ", subtitleParts);

        if (subtitle.Length > 0)
        {
            using var subFont = new Font(BodyFontFamily, 22, FontStyle.Italic);
            using var subBrush = new SolidBrush(InkFaint);
            var subSize = g.MeasureString(subtitle, subFont);
            g.DrawString(subtitle, subFont, subBrush, frame.Left + (frame.Width - subSize.Width) / 2, y);
            y += (int)subSize.Height + 14;
        }

        return y + 10;
    }

    private static void DrawPortraitArea(Graphics g, Rectangle box, CharacterCardData data)
    {
        var mainHeight = (int)(box.Height * 0.72);
        var mainBox = new Rectangle(box.Left, box.Top, box.Width, mainHeight);
        DrawFramedImage(g, mainBox, data.Front, "ФАС");

        var thumbY = mainBox.Bottom + 14;
        var thumbSize = box.Width / 3 - 10;
        var thumbs = new (Bitmap? img, string label)[] { (data.Back, "СПИНА"), (data.Left, "СЛЕВА"), (data.Right, "СПРАВА") };
        for (var i = 0; i < thumbs.Length; i++)
        {
            var tb = new Rectangle(box.Left + i * (thumbSize + 15), thumbY, thumbSize, box.Bottom - thumbY);
            DrawFramedImage(g, tb, thumbs[i].img, thumbs[i].label);
        }
    }

    private static void DrawFramedImage(Graphics g, Rectangle box, Bitmap? image, string label)
    {
        using (var fillBrush = new SolidBrush(PanelFill))
            g.FillRectangle(fillBrush, box);
        using (var pen = new Pen(Ink, 3))
            g.DrawRectangle(pen, box);

        var labelHeight = box.Height >= 200 ? 26 : 20;
        var imageBox = Rectangle.Inflate(box, -8, -8);
        imageBox.Height -= labelHeight;

        if (image != null)
            DrawFitted(g, image, imageBox);
        else
            DrawPlaceholder(g, imageBox);

        using var labelFont = new Font(BodyFontFamily, box.Height >= 200 ? 15 : 11, FontStyle.Bold);
        using var labelBrush = new SolidBrush(InkFaint);
        var format = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString(label, labelFont, labelBrush, new RectangleF(box.Left, imageBox.Bottom + 2, box.Width, labelHeight), format);
    }

    private static void DrawPlaceholder(Graphics g, Rectangle box)
    {
        using var pen = new Pen(Color.FromArgb(90, InkFaint), 1) { DashStyle = DashStyle.Dash };
        g.DrawRectangle(pen, Rectangle.Inflate(box, -4, -4));
        using var font = new Font(BodyFontFamily, 12, FontStyle.Italic);
        using var brush = new SolidBrush(Color.FromArgb(140, InkFaint));
        var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("нет фото", font, brush, box, format);
    }

    /// <summary>Draws `image` centered in `box`, preserving aspect ratio (letterboxed, never cropped).
    /// Nearest-neighbor when enlarging keeps pixel-art sprites crisp; smooth when shrinking.</summary>
    private static void DrawFitted(Graphics g, Bitmap image, Rectangle box)
    {
        var scale = Math.Min((float)box.Width / image.Width, (float)box.Height / image.Height);
        var w = image.Width * scale;
        var h = image.Height * scale;
        var x = box.Left + (box.Width - w) / 2;
        var y = box.Top + (box.Height - h) / 2;

        var prevMode = g.InterpolationMode;
        g.InterpolationMode = scale >= 1f ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
        g.DrawImage(image, new RectangleF(x, y, w, h));
        g.InterpolationMode = prevMode;
    }

    private static void DrawSkillsPanel(Graphics g, Rectangle box, CharacterCardData data)
    {
        DrawPanelChrome(g, box, "НАВЫКИ");

        var skills = data.Skills
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var contentBox = Rectangle.Inflate(box, -18, -18);
        contentBox.Y += 40;
        contentBox.Height -= 40;

        if (skills.Count == 0)
        {
            using var emptyFont = new Font(BodyFontFamily, 16, FontStyle.Italic);
            using var emptyBrush = new SolidBrush(InkFaint);
            g.DrawString("— не указаны —", emptyFont, emptyBrush, contentBox);
            return;
        }

        using var rowFont = new Font(BodyFontFamily, 17);
        using var textBrush = new SolidBrush(Ink);
        var rowHeight = Math.Max(28, Math.Min(40, contentBox.Height / skills.Count));
        // Short skill lists otherwise sit stranded at the top of a box sized for a much longer list.
        var startY = contentBox.Top + Math.Max(0, (contentBox.Height - skills.Count * rowHeight) / 2);

        for (var i = 0; i < skills.Count; i++)
        {
            var rowY = startY + i * rowHeight;
            if (rowY > contentBox.Bottom - 16) break;

            if (i % 2 == 0)
                using (var stripe = new SolidBrush(StripeFill))
                    g.FillRectangle(stripe, contentBox.Left, rowY, contentBox.Width, rowHeight);

            var bulletRect = new RectangleF(contentBox.Left + 6, rowY + rowHeight / 2f - 5, 10, 10);
            using (var bulletBrush = new SolidBrush(Gold))
                g.FillEllipse(bulletBrush, bulletRect);

            var textRect = new RectangleF(contentBox.Left + 28, rowY, contentBox.Width - 32, rowHeight);
            var format = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            g.DrawString(skills[i], rowFont, textBrush, textRect, format);
        }
    }

    private static void DrawLorePanel(Graphics g, Rectangle box, CharacterCardData data)
    {
        DrawPanelChrome(g, box, "ИСТОРИЯ ПЕРСОНАЖА");

        var contentBox = Rectangle.Inflate(box, -22, -22);
        contentBox.Y += 34;
        contentBox.Height -= 34;

        var text = string.IsNullOrWhiteSpace(data.Lore) ? "— лор не указан —" : data.Lore;
        using var font = FitParagraphFont(g, text, BodyFontFamily, FontStyle.Italic, 19, contentBox.Size);
        using var brush = new SolidBrush(Ink);
        // Vertically centered so short lore doesn't look stranded at the top of a box sized to also
        // fit much longer backstories.
        var format = new StringFormat { Trimming = StringTrimming.EllipsisWord, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, brush, contentBox, format);
    }

    private static void DrawPanelChrome(Graphics g, Rectangle box, string title)
    {
        using (var fillBrush = new SolidBrush(PanelFill))
            g.FillRectangle(fillBrush, box);
        using (var pen = new Pen(Ink, 3))
            g.DrawRectangle(pen, box);

        var bannerHeight = 34;
        var banner = new Rectangle(box.Left, box.Top, box.Width, bannerHeight);
        using (var bannerBrush = new LinearGradientBrush(banner, Gold, Color.FromArgb(255, 110, 78, 26), LinearGradientMode.Horizontal))
            g.FillRectangle(bannerBrush, banner);
        using (var pen = new Pen(Ink, 2))
            g.DrawRectangle(pen, banner);

        using var titleFont = new Font(HeaderFontFamily, 16, FontStyle.Bold);
        using var titleBrush = new SolidBrush(Color.White);
        var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(title, titleFont, titleBrush, banner, format);
    }

    private static void DrawFooter(Graphics g, Rectangle frame)
    {
        using var font = new Font(BodyFontFamily, 11, FontStyle.Italic);
        using var brush = new SolidBrush(Color.FromArgb(140, InkFaint));
        var format = new StringFormat { Alignment = StringAlignment.Far };
        g.DrawString("Сгенерировано в SS14 Utility", font, brush, new RectangleF(frame.Left, frame.Bottom - 22, frame.Width, 20), format);
    }

    /// <summary>Shrinks a font size until `text` fits on one line within `maxWidth`.</summary>
    private static Font FitSingleLineFont(Graphics g, string text, string family, FontStyle style, float startSize, float maxWidth)
    {
        var size = startSize;
        while (size > 18)
        {
            using var font = new Font(family, size, style);
            if (g.MeasureString(text, font).Width <= maxWidth)
                return new Font(family, size, style);
            size -= 2;
        }
        return new Font(family, size, style);
    }

    /// <summary>Shrinks a font size until `text` (word-wrapped) fits within `area`.</summary>
    private static Font FitParagraphFont(Graphics g, string text, string family, FontStyle style, float startSize, Size area)
    {
        var size = startSize;
        while (size > 11)
        {
            using var font = new Font(family, size, style);
            var measured = g.MeasureString(text, font, area.Width);
            if (measured.Height <= area.Height)
                return new Font(family, size, style);
            size -= 1;
        }
        return new Font(family, size, style);
    }
}
