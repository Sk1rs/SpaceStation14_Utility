#!/bin/python3
'''Transforms images into SS14 markup text.'''

from sys import argv, exit
import re
import webbrowser
from PIL import Image, ImageOps, ImageEnhance, ImageFilter, ImageChops

cli = False

try:
    # Try importing PyQt6 for GUI.
    from PyQt6.QtWidgets import QApplication, QMainWindow, QDialog, QWidget, QPushButton, QVBoxLayout, QHBoxLayout, QTextEdit, QLabel, QCheckBox, QFileDialog, QSpinBox, QColorDialog, QScrollArea, QSlider, QGroupBox, QComboBox, QLineEdit
    from PyQt6.QtCore import Qt, QTimer, QRect
    from PyQt6.QtGui import QImage, QPixmap, QPainter, QColor, QShortcut, QKeySequence, QTransform
except:
    # Create QMainWindow/QDialog classes so python doesnt get angry when creating Window/dialog classes
    class QMainWindow:
        def __init__(self):
            pass
    class QDialog:
        def __init__(self, *args, **kwargs):
            pass
    class QWidget:
        def __init__(self, *args, **kwargs):
            pass
    print("Forced CLI Mode: Cant load PyQt6.")
    cli = True

STYLE_SHEET = """
QWidget {
    background-color: #1e1f26;
    color: #e8e8ec;
    font-family: 'Segoe UI', sans-serif;
    font-size: 13px;
}
QGroupBox {
    background-color: #262832;
    border: 1px solid #3a3d4a;
    border-radius: 8px;
    margin-top: 12px;
    padding: 10px 6px 6px 6px;
    font-weight: 600;
}
QGroupBox::title {
    subcontrol-origin: margin;
    subcontrol-position: top left;
    left: 10px;
    padding: 0 6px;
    color: #8fb4ff;
}
QPushButton {
    background-color: #33364a;
    border: 1px solid #454863;
    border-radius: 6px;
    padding: 5px 10px;
    color: #e8e8ec;
}
QPushButton:hover {
    background-color: #40435c;
    border-color: #5a5f82;
}
QPushButton:pressed {
    background-color: #2a2c3b;
}
QPushButton:checked {
    background-color: #4f6df5;
    border-color: #6c8bff;
    color: #ffffff;
}
QCheckBox::indicator {
    width: 15px;
    height: 15px;
    border-radius: 4px;
    border: 1px solid #565a75;
    background: #2a2c3b;
}
QCheckBox::indicator:checked {
    background: #4f6df5;
    border-color: #6c8bff;
}
QSpinBox {
    background-color: #2a2c3b;
    border: 1px solid #454863;
    border-radius: 5px;
    padding: 2px 4px;
}
QSlider::groove:horizontal {
    height: 4px;
    background: #3a3d4a;
    border-radius: 2px;
}
QSlider::handle:horizontal {
    width: 14px;
    height: 14px;
    margin: -6px 0;
    border-radius: 7px;
    background: #6c8bff;
}
QTextEdit {
    background-color: #16171d;
    border: 1px solid #3a3d4a;
    border-radius: 8px;
    color: #d7e2ff;
    selection-background-color: #4f6df5;
}
QScrollArea {
    background-color: #16171d;
    border: 1px solid #3a3d4a;
    border-radius: 8px;
}
QScrollBar:vertical, QScrollBar:horizontal {
    background: #1e1f26;
    border: none;
}
QScrollBar::handle {
    background: #454863;
    border-radius: 4px;
}
QLabel a {
    color: #8fb4ff;
}
"""

# Standard SS14 paper symbol limit is 10000 (at least in RMC-14)
symbol_limit = 10050
use_limit = True

full_color = False
grayscale = False
invert = False
brightness = 100
contrast = 100
posterize_bits = 8
dither = False
hue_shift = 0
recolor_map = {}
active_palette = None

useful_video_url = "https://youtu.be/9FCF2Y4lIWk?si=LEDw75eOhhTPN_Ua"
original_repo_url = "https://github.com/Tunguso4ka/SSfyImage"
modder_url = "https://github.com/Sk1rs"
disclaimer_seconds = 3

image_size = {"w":0, "h":0}
original_size = {"w":0, "h":0}
resize_size = {"w":0, "h":0}

# Standard SS14 paper size, in pixels (2 characters wide, so 21x26 fits nicely)
paper_size = {"w":21, "h":26}

path = ""
pixel = "██"
symbol_mode = "solid"  # "solid" (always `pixel`) or "shading" (glyph density follows brightness)
SHADE_RAMP = "░▒▓█"  # sparse -> dense, used by shading mode

SYMBOL_PRESETS = [
    "██", "▓▓", "▒▒", "░░",
    "▲▲", "▼▼", "◄◄", "►►", "△△", "▽▽",
    "◆◆", "◇◇", "●●", "○○", "◐◐", "◑◑",
    "■■", "□□", "▪▪", "▫▫",
    "★★", "☆☆", "♦♦", "♥♥", "♠♠", "♣♣",
    "••", "××", "##", "@@", "%%", "&&",
]

# PIL image coming from the built-in draw editor. When set, it's used instead of `path`.
custom_image = None


class color:
    def __init__(self, given):

        self.r = f'{given[0]:02x}'
        self.g = f'{given[1]:02x}'
        self.b = f'{given[2]:02x}'

        if len(given) > 3:
            self.a = f'{given[3]:02x}'
        else:
            self.a = "ff"

        if not full_color:
            self.r = self.r[0]
            self.g = self.g[0]
            self.b = self.b[0]
            self.a = self.a[0]

    def get_color(self):
        '''returns rgb or rrggbb color'''
        local = f"{self.r}{self.g}{self.b}"
        if self.a not in ["f", "ff"]:
            local += f"{self.a}"
        return local

    def get_rgba(self):
        '''returns the (r, g, b, a) tuple this pixel will actually render as in-game'''
        def expand(h):
            return int(h * 2, 16) if len(h) == 1 else int(h, 16)
        return (expand(self.r), expand(self.g), expand(self.b), expand(self.a))


def apply_dither(im):
    '''Floyd-Steinberg dithers RGB channels to the same 16-level buckets color.__init__ truncates to,
    so quantization banding (from the non-#RRGGBB mode) turns into a much smoother-looking pattern instead.'''
    im = im.convert("RGBA")
    w, h = im.size
    pixels = [list(p) for p in im.getdata()]

    def index(x, y):
        return y * w + x

    for y in range(h):
        for x in range(w):
            i = index(x, y)
            if pixels[i][3] == 0:
                continue
            for c in range(3):
                old = max(0, min(255, pixels[i][c]))
                bucket = min(15, int(old) // 16)
                new = bucket * 17
                err = old - new
                pixels[i][c] = new
                if x + 1 < w:
                    pixels[index(x + 1, y)][c] += err * 7 / 16
                if x - 1 >= 0 and y + 1 < h:
                    pixels[index(x - 1, y + 1)][c] += err * 3 / 16
                if y + 1 < h:
                    pixels[index(x, y + 1)][c] += err * 5 / 16
                if x + 1 < w and y + 1 < h:
                    pixels[index(x + 1, y + 1)][c] += err * 1 / 16

    out = Image.new("RGBA", (w, h))
    out.putdata([(int(max(0, min(255, r))), int(max(0, min(255, g))), int(max(0, min(255, b))), a)
                  for r, g, b, a in pixels])
    return out


def apply_recolor(im, mapping):
    '''Replaces every pixel matching a key in `mapping` (exact RGBA match) with its mapped color.'''
    im = im.convert("RGBA")
    px = im.load()
    w, h = im.size
    for y in range(h):
        for x in range(w):
            c = px[x, y]
            new_c = mapping.get(c)
            if new_c is not None:
                px[x, y] = new_c
    return im


def extract_palette(pil_image, n_colors):
    '''Median-cut quantizes a reference image down to its n_colors most representative colors.'''
    rgb = pil_image.convert("RGB")
    quantized = rgb.quantize(colors=n_colors, method=Image.Quantize.MEDIANCUT)
    flat = quantized.getpalette()[:n_colors * 3]
    return [tuple(flat[i:i + 3]) for i in range(0, len(flat), 3)]


def apply_palette(im, palette):
    '''Snaps every pixel's RGB to the nearest color in `palette` (a list of (r,g,b) tuples), keeping alpha.'''
    im = im.convert("RGBA")
    alpha = im.getchannel("A")

    pal_img = Image.new("P", (1, 1))
    flat = []
    for r, g, b in palette:
        flat.extend([r, g, b])
    if not flat:
        return im
    while len(flat) < 256 * 3:
        flat.extend(flat[-3:])
    pal_img.putpalette(flat[:256 * 3])

    quantized = im.convert("RGB").quantize(palette=pal_img, dither=Image.Dither.NONE)
    result = quantized.convert("RGBA")
    result.putalpha(alpha)
    return result


# A handful of well-known retro hardware colors, used only for the "style preview" - a fun
# approximation, not a byte-accurate emulator (real per-tile palette limits etc. are ignored).
GAMEBOY_PALETTE = [(15, 56, 15), (48, 98, 48), (139, 172, 15), (155, 188, 15)]
NES_PALETTE = [
    (124, 124, 124), (0, 0, 252), (0, 0, 188), (68, 40, 188), (148, 0, 132), (168, 0, 32), (168, 16, 0), (136, 20, 0),
    (80, 48, 0), (0, 120, 0), (0, 104, 0), (0, 88, 0), (0, 64, 88), (0, 0, 0),
    (188, 188, 188), (0, 120, 248), (0, 88, 248), (104, 68, 252), (216, 0, 204), (228, 0, 88), (248, 56, 0), (228, 92, 16),
    (172, 124, 0), (0, 184, 0), (0, 168, 0), (0, 168, 68), (0, 136, 136),
    (248, 248, 248), (60, 188, 252), (104, 136, 252), (152, 120, 248), (248, 120, 248), (248, 88, 152), (248, 120, 88), (252, 160, 68),
    (248, 184, 0), (184, 248, 24), (88, 216, 84), (88, 248, 152), (0, 232, 216), (120, 120, 120),
    (252, 252, 252), (164, 228, 252), (184, 184, 248), (216, 184, 248), (248, 184, 248), (248, 164, 192), (240, 208, 176), (252, 224, 168),
    (248, 216, 120), (216, 248, 120), (184, 248, 184), (184, 248, 216), (0, 252, 252), (248, 216, 248),
]


def style_gameboy(im):
    return apply_palette(im, GAMEBOY_PALETTE)


def style_nes(im):
    return apply_palette(im, NES_PALETTE)


def style_genesis(im):
    '''Approximates the Mega Drive's 9-bit RGB (3 bits per channel).'''
    im = im.convert("RGBA")
    alpha = im.getchannel("A")
    result = ImageOps.posterize(im.convert("RGB"), 3).convert("RGBA")
    result.putalpha(alpha)
    return result


def style_crt(im):
    '''Cheap arcade-CRT look: darkened scanlines every other row.'''
    im = im.convert("RGBA")
    alpha = im.getchannel("A")
    rgb = im.convert("RGB")
    px = rgb.load()
    for y in range(0, rgb.height, 2):
        for x in range(rgb.width):
            r, g, b = px[x, y]
            px[x, y] = (r // 2, g // 2, b // 2)
    result = rgb.convert("RGBA")
    result.putalpha(alpha)
    return result


HARDWARE_STYLES = {
    "None": None,
    "Game Boy": style_gameboy,
    "NES": style_nes,
    "Genesis": style_genesis,
    "Arcade CRT": style_crt,
}


def _silhouette_falloff(alpha_channel, radius=3):
    '''Cheap approximate distance-to-edge: 0 right at the silhouette edge (and outside it),
    ramping up to full strength `radius` pixels inward - gives sprites a rounded-bump look.'''
    mask = alpha_channel.point(lambda p: 255 if p > 0 else 0)
    falloff = Image.new("L", mask.size, 0)
    current = mask
    step = 255 // radius
    for _ in range(radius):
        current = current.filter(ImageFilter.MinFilter(3))
        falloff = ImageChops.add(falloff, current.point(lambda p: step if p > 0 else 0))
    return falloff


def _sobel(height_img):
    '''Manual 3x3 Sobel gradient (images here are small enough that a plain per-pixel loop is fine).'''
    w, h = height_img.size
    px = height_img.load()

    def get(x, y):
        x = min(w - 1, max(0, x))
        y = min(h - 1, max(0, y))
        return px[x, y]

    gx = [[0] * w for _ in range(h)]
    gy = [[0] * w for _ in range(h)]
    for y in range(h):
        for x in range(w):
            gx[y][x] = (get(x + 1, y - 1) + 2 * get(x + 1, y) + get(x + 1, y + 1)) - \
                       (get(x - 1, y - 1) + 2 * get(x - 1, y) + get(x - 1, y + 1))
            gy[y][x] = (get(x - 1, y + 1) + 2 * get(x, y + 1) + get(x + 1, y + 1)) - \
                       (get(x - 1, y - 1) + 2 * get(x, y - 1) + get(x + 1, y - 1))
    return gx, gy


def generate_normal_map(source_image, strength=3.5, use_silhouette=True):
    '''Builds a tangent-space normal map (RGB) from a pixel-art image's brightness (and optionally
    silhouette rounding) - an export-only asset for lighting in an external game engine. SS14 has no
    use for this itself.'''
    im = source_image.convert("RGBA")
    w, h = im.size
    alpha = im.getchannel("A")
    luminance = im.convert("L")

    if use_silhouette:
        falloff = _silhouette_falloff(alpha)
        lum_px, fall_px = luminance.load(), falloff.load()
        height = Image.new("L", (w, h), 0)
        height_px = height.load()
        for y in range(h):
            for x in range(w):
                height_px[x, y] = (lum_px[x, y] * fall_px[x, y]) // 255
    else:
        height = luminance

    gx, gy = _sobel(height)

    result = Image.new("RGBA", (w, h))
    result_px = result.load()
    alpha_px = alpha.load()
    for y in range(h):
        for x in range(w):
            if alpha_px[x, y] == 0:
                result_px[x, y] = (128, 128, 255, 0)
                continue
            nx = -gx[y][x] * strength / 255
            ny = -gy[y][x] * strength / 255
            nz = 1.0
            length = (nx * nx + ny * ny + nz * nz) ** 0.5
            nx, ny, nz = nx / length, ny / length, nz / length
            r = max(0, min(255, int((nx * 0.5 + 0.5) * 255)))
            g = max(0, min(255, int((ny * 0.5 + 0.5) * 255)))
            b = max(0, min(255, int((nz * 0.5 + 0.5) * 255)))
            result_px[x, y] = (r, g, b, alpha_px[x, y])
    return result


def transform():
    '''Opens image, transforms it into ss14 markup text and builds a preview of the output.'''
    text = ""
    line = ""

    pr_pixel = ""

    try:
        if custom_image is not None:
            im = custom_image.convert("RGBA")
        else:
            im = Image.open(path).convert("RGBA")
    except:
        return f"Error while loading {path}.", None

    target_w = resize_size["w"] or im.width
    target_h = resize_size["h"] or im.height
    if (target_w, target_h) != im.size:
        # Keep drawings crisp (blocky) instead of blurring them like a resized photo.
        resample = Image.Resampling.NEAREST if custom_image is not None else Image.Resampling.LANCZOS
        im = im.resize((target_w, target_h), resample)

    if grayscale:
        alpha = im.getchannel("A")
        im = ImageOps.grayscale(im).convert("RGBA")
        im.putalpha(alpha)

    if invert:
        alpha = im.getchannel("A")
        im = ImageOps.invert(im.convert("RGB")).convert("RGBA")
        im.putalpha(alpha)

    if brightness != 100:
        alpha = im.getchannel("A")
        im = ImageEnhance.Brightness(im.convert("RGB")).enhance(brightness / 100).convert("RGBA")
        im.putalpha(alpha)

    if contrast != 100:
        alpha = im.getchannel("A")
        im = ImageEnhance.Contrast(im.convert("RGB")).enhance(contrast / 100).convert("RGBA")
        im.putalpha(alpha)

    if posterize_bits < 8:
        alpha = im.getchannel("A")
        im = ImageOps.posterize(im.convert("RGB"), posterize_bits).convert("RGBA")
        im.putalpha(alpha)

    if hue_shift != 0:
        alpha = im.getchannel("A")
        h_band, s_band, v_band = im.convert("RGB").convert("HSV").split()
        offset = round(hue_shift / 360 * 255) % 256
        h_band = h_band.point(lambda p: (p + offset) % 256)
        im = Image.merge("HSV", (h_band, s_band, v_band)).convert("RGB").convert("RGBA")
        im.putalpha(alpha)

    if active_palette:
        im = apply_palette(im, active_palette)

    if dither and not full_color:
        im = apply_dither(im)

    if recolor_map:
        im = apply_recolor(im, recolor_map)

    pixels = list(im.getdata())
    image_size["w"], image_size["h"] = im.size

    preview = Image.new("RGBA", im.size, (0, 0, 0, 0))

    blank_unit = ' ' * (2 if symbol_mode == "shading" else len(pixel))

    ind = 1
    row = 0
    # TODO rewrite that
    for i in pixels:
        cur_pixel = color(i)

        # Add one pixel (two boxes)
        if cur_pixel.a in ['0', '00']:
            line += blank_unit
        else:
            preview.putpixel((ind - 1, row), cur_pixel.get_rgba())

            if symbol_mode == "shading":
                luminance = (i[0] * 299 + i[1] * 587 + i[2] * 114) // 1000
                shade_index = min(len(SHADE_RAMP) - 1, (255 - luminance) * len(SHADE_RAMP) // 256)
                glyph = SHADE_RAMP[shade_index] * 2
            else:
                glyph = pixel

            if cur_pixel.get_color() == pr_pixel:
                line += glyph
            else:
                line += "[color=#" + cur_pixel.get_color() + "]" + glyph
                pr_pixel = cur_pixel.get_color()

        # Compare next pixel pos with img width
        if ind != image_size["w"]:
            ind += 1
            continue

        # Break for loop if text bigger then limit (6000 symbols)
        if len(text + line) > symbol_limit and use_limit:
            break

        text += line + "\n"
        row += 1

        line = ""
        ind = 1

    return text, preview.crop((0, 0, image_size["w"], row))


_color_tag_re = re.compile(r'\[color=#([0-9a-fA-F]{3,8})\]')


def _expand_hex_channel(chunk):
    return int(chunk * (2 // len(chunk)), 16)


def _parse_color_tag(hex_str):
    '''Reverses color.get_color(): turns a #rgb/#rgba/#rrggbb/#rrggbbaa hex string back into (r,g,b,a).'''
    n = len(hex_str)
    step = 1 if n in (3, 4) else 2 if n in (6, 8) else None
    if step is None:
        return None
    channels = [hex_str[i:i + step] for i in range(0, n, step)]
    values = [_expand_hex_channel(c) for c in channels]
    while len(values) < 4:
        values.append(255)
    return tuple(values[:4])


def parse_ss14_text(text):
    '''Reverses transform(): turns previously generated SS14 markup text back into a pixel image.'''
    text = text.replace("\r\n", "\n").replace("\r", "\n")
    lines = text.split("\n")
    if lines and lines[-1] == "":
        lines.pop()
    if not lines:
        return None

    unit = len(pixel)
    # None means "no color established yet" - a bare (untagged) glyph before any tag is invalid,
    # which is what lets this reject unrelated/foreign text instead of misreading it as an image.
    current_color = None
    rows = []

    for line in lines:
        row = []
        pos = 0
        while pos < len(line):
            match = _color_tag_re.match(line, pos)
            if match:
                parsed = _parse_color_tag(match.group(1))
                if parsed is None:
                    return None
                current_color = parsed
                pos = match.end()
                if line[pos:pos + unit] == " " * unit or len(line) - pos < unit:
                    return None
                row.append(current_color)
                pos += unit
            elif line[pos:pos + unit] == " " * unit:
                row.append(None)
                pos += unit
            elif current_color is not None:
                # Any other glyph continues the current run's color - the exact symbol used
                # (solid block, custom character, or a shading-mode density glyph) doesn't matter.
                row.append(current_color)
                pos += unit
            else:
                return None
        rows.append(row)

    # Width is however many pixels were actually parsed out of each row, not raw character count
    # (tag text like "[color=#fff]" would otherwise massively inflate a naive length-based estimate).
    width = max((len(row) for row in rows), default=0)
    height = len(rows)
    if width == 0 or height == 0:
        return None

    im = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    for y, row in enumerate(rows):
        for x, color in enumerate(row):
            if color is not None:
                im.putpixel((x, y), color)

    return im


class ClickableLabel(QLabel):
    '''A QLabel that reports clicks via an `on_click(QPoint)` callback attribute.'''
    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.on_click = None

    def mousePressEvent(self, event):
        if self.on_click:
            self.on_click(event.position().toPoint())


class Window(QMainWindow):
    '''Main and only window'''
    def __init__(self):
        super().__init__()

        self.current_preview = None
        self._preview_pixmap = None
        self._recolor_source = None
        self.preview_style = "None"

        #Source group - where the image comes from
        group_source = QGroupBox("Source")
        layout_source = QHBoxLayout(group_source)

        button_open = QPushButton("Open file")
        button_open.clicked.connect(self.button_open_clicked)
        layout_source.addWidget(button_open)

        button_open_gif = QPushButton("Pick GIF frame")
        button_open_gif.clicked.connect(self.button_open_gif_clicked)
        layout_source.addWidget(button_open_gif)

        button_paste = QPushButton("Paste (Ctrl+V)")
        button_paste.clicked.connect(self.button_paste_clicked)
        layout_source.addWidget(button_paste)
        QShortcut(QKeySequence("Ctrl+V"), self).activated.connect(self.button_paste_clicked)

        button_draw = QPushButton("Draw")
        button_draw.clicked.connect(self.button_draw_clicked)
        layout_source.addWidget(button_draw)

        #Adjustments group - color/tone controls
        group_adjust = QGroupBox("Adjustments")
        layout_adjust = QHBoxLayout(group_adjust)

        checkbox_fullcolor = QCheckBox("Use #RRGGBB")
        checkbox_fullcolor.setChecked(full_color)
        checkbox_fullcolor.checkStateChanged.connect(self.checkbox_fullcolor_checked)
        layout_adjust.addWidget(checkbox_fullcolor)

        checkbox_dither = QCheckBox("Dither")
        checkbox_dither.setChecked(dither)
        checkbox_dither.checkStateChanged.connect(self.checkbox_dither_checked)
        layout_adjust.addWidget(checkbox_dither)

        checkbox_grayscale = QCheckBox("B&&W")
        checkbox_grayscale.setChecked(grayscale)
        checkbox_grayscale.checkStateChanged.connect(self.checkbox_grayscale_checked)
        layout_adjust.addWidget(checkbox_grayscale)

        checkbox_invert = QCheckBox("Invert")
        checkbox_invert.setChecked(invert)
        checkbox_invert.checkStateChanged.connect(self.checkbox_invert_checked)
        layout_adjust.addWidget(checkbox_invert)

        layout_adjust.addWidget(QLabel("Brightness:"))
        self.slider_brightness = QSlider(Qt.Orientation.Horizontal)
        self.slider_brightness.setRange(50, 150)
        self.slider_brightness.setValue(brightness)
        self.slider_brightness.setFixedWidth(100)
        self.slider_brightness.valueChanged.connect(self.slider_brightness_changed)
        layout_adjust.addWidget(self.slider_brightness)

        layout_adjust.addWidget(QLabel("Contrast:"))
        self.slider_contrast = QSlider(Qt.Orientation.Horizontal)
        self.slider_contrast.setRange(50, 150)
        self.slider_contrast.setValue(contrast)
        self.slider_contrast.setFixedWidth(100)
        self.slider_contrast.valueChanged.connect(self.slider_contrast_changed)
        layout_adjust.addWidget(self.slider_contrast)

        layout_adjust.addWidget(QLabel("Posterize:"))
        self.spin_posterize = QSpinBox()
        self.spin_posterize.setRange(1, 8)
        self.spin_posterize.setValue(posterize_bits)
        self.spin_posterize.valueChanged.connect(self.spin_posterize_changed)
        layout_adjust.addWidget(self.spin_posterize)

        layout_adjust.addWidget(QLabel("Hue shift:"))
        self.slider_hue = QSlider(Qt.Orientation.Horizontal)
        self.slider_hue.setRange(-180, 180)
        self.slider_hue.setValue(hue_shift)
        self.slider_hue.setFixedWidth(100)
        self.slider_hue.valueChanged.connect(self.slider_hue_changed)
        layout_adjust.addWidget(self.slider_hue)

        button_palette = QPushButton("Palette from image...")
        button_palette.clicked.connect(self.button_palette_clicked)
        layout_adjust.addWidget(button_palette)

        self.label_palette_status = QLabel("Palette: none")
        layout_adjust.addWidget(self.label_palette_status)

        button_palette_clear = QPushButton("Clear palette")
        button_palette_clear.clicked.connect(self.button_palette_clear_clicked)
        layout_adjust.addWidget(button_palette_clear)

        button_reset_adjust = QPushButton("Reset")
        button_reset_adjust.clicked.connect(self.button_reset_adjust_clicked)
        layout_adjust.addWidget(button_reset_adjust)

        #Recolor group - click a color in the preview, replace it project-wide
        group_recolor = QGroupBox("Recolor")
        layout_recolor = QHBoxLayout(group_recolor)

        layout_recolor.addWidget(QLabel("Click the preview to pick a color:"))

        self.button_recolor_from = QPushButton()
        self.button_recolor_from.setFixedSize(28, 28)
        self.button_recolor_from.setEnabled(False)
        layout_recolor.addWidget(self.button_recolor_from)

        layout_recolor.addWidget(QLabel("→"))

        self._recolor_target = QColor(255, 0, 0, 255)
        self.button_recolor_to = QPushButton()
        self.button_recolor_to.setFixedSize(28, 28)
        self.button_recolor_to.setStyleSheet(f"background-color: {self._recolor_target.name()};")
        self.button_recolor_to.clicked.connect(self.button_recolor_pick_target_clicked)
        layout_recolor.addWidget(self.button_recolor_to)

        button_recolor_apply = QPushButton("Replace")
        button_recolor_apply.clicked.connect(self.button_recolor_apply_clicked)
        layout_recolor.addWidget(button_recolor_apply)

        button_recolor_clear = QPushButton("Clear all")
        button_recolor_clear.clicked.connect(self.button_recolor_clear_clicked)
        layout_recolor.addWidget(button_recolor_clear)

        self.label_recolor_count = QLabel("0 active")
        layout_recolor.addWidget(self.label_recolor_count)

        #Symbol group - what character(s) represent a pixel
        group_symbol = QGroupBox("Symbol")
        layout_symbol = QHBoxLayout(group_symbol)

        layout_symbol.addWidget(QLabel("Symbol:"))
        self.edit_symbol = QLineEdit(pixel)
        self.edit_symbol.setFixedWidth(60)
        self.edit_symbol.textChanged.connect(self.edit_symbol_changed)
        layout_symbol.addWidget(self.edit_symbol)

        layout_symbol.addWidget(QLabel("Presets:"))
        self.combo_symbol_preset = QComboBox()
        self.combo_symbol_preset.addItem("...")
        self.combo_symbol_preset.addItems(SYMBOL_PRESETS)
        self.combo_symbol_preset.currentTextChanged.connect(self.combo_symbol_preset_changed)
        layout_symbol.addWidget(self.combo_symbol_preset)

        self.checkbox_shading = QCheckBox("Shading mode (combined: density follows brightness)")
        self.checkbox_shading.checkStateChanged.connect(self.checkbox_shading_checked)
        layout_symbol.addWidget(self.checkbox_shading)

        #Size group - output pixel dimensions
        group_size = QGroupBox("Size")
        layout_size = QHBoxLayout(group_size)

        layout_size.addWidget(QLabel("W:"))
        self.spin_w = QSpinBox()
        self.spin_w.setRange(1, 500)
        self.spin_w.valueChanged.connect(self.spin_w_changed)
        layout_size.addWidget(self.spin_w)

        layout_size.addWidget(QLabel("H:"))
        self.spin_h = QSpinBox()
        self.spin_h.setRange(1, 500)
        self.spin_h.valueChanged.connect(self.spin_h_changed)
        layout_size.addWidget(self.spin_h)

        self.checkbox_keep_ratio = QCheckBox("Keep ratio")
        self.checkbox_keep_ratio.setChecked(True)
        layout_size.addWidget(self.checkbox_keep_ratio)

        for step in (2, 4):
            button_scale = QPushButton(f"+{step}")
            button_scale.clicked.connect(lambda checked, step=step: self.button_scale_clicked(step))
            layout_size.addWidget(button_scale)

        button_fit = QPushButton(f"Fit to paper ({paper_size['w']}x{paper_size['h']})")
        button_fit.clicked.connect(self.button_fit_clicked)
        layout_size.addWidget(button_fit)

        button_fit_limit = QPushButton("Fit to limit")
        button_fit_limit.clicked.connect(self.button_fit_limit_clicked)
        layout_size.addWidget(button_fit_limit)

        button_reset_size = QPushButton("Reset size")
        button_reset_size.clicked.connect(self.button_reset_size_clicked)
        layout_size.addWidget(button_reset_size)

        #Output group - refresh, export and sharing
        group_output = QGroupBox("Output")
        layout_output = QHBoxLayout(group_output)

        button_reload = QPushButton("Reload")
        button_reload.clicked.connect(self.button_reload_clicked)
        layout_output.addWidget(button_reload)

        checkbox_limit = QCheckBox("Use limit")
        checkbox_limit.setChecked(use_limit)
        checkbox_limit.checkStateChanged.connect(self.checkbox_limit_checked)
        layout_output.addWidget(checkbox_limit)

        button_copy = QPushButton("Copy")
        button_copy.clicked.connect(self.button_copy_clicked)
        layout_output.addWidget(button_copy)

        button_save_text = QPushButton("Save text")
        button_save_text.clicked.connect(self.button_save_text_clicked)
        layout_output.addWidget(button_save_text)

        button_load_text = QPushButton("Load text")
        button_load_text.clicked.connect(self.button_load_text_clicked)
        layout_output.addWidget(button_load_text)

        button_save_preview = QPushButton("Save preview")
        button_save_preview.clicked.connect(self.button_save_preview_clicked)
        layout_output.addWidget(button_save_preview)

        button_normal_map = QPushButton("Normal map...")
        button_normal_map.clicked.connect(self.button_normal_map_clicked)
        layout_output.addWidget(button_normal_map)

        button_video = QPushButton("Useful video")
        button_video.clicked.connect(self.button_video_clicked)
        layout_output.addWidget(button_video)

        #Content layout (text output + output preview)
        layout_content = QHBoxLayout()

        self.text_edit = QTextEdit()
        self.text_edit.setLineWrapMode(QTextEdit.LineWrapMode.NoWrap)
        layout_content.addWidget(self.text_edit, 1)

        layout_preview_panel = QVBoxLayout()

        style_row = QHBoxLayout()
        style_row.addWidget(QLabel("Style preview:"))
        self.combo_style = QComboBox()
        self.combo_style.addItems(HARDWARE_STYLES.keys())
        self.combo_style.currentTextChanged.connect(self.combo_style_changed)
        style_row.addWidget(self.combo_style)
        layout_preview_panel.addLayout(style_row)

        self.label_preview = ClickableLabel("No preview, yet")
        self.label_preview.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.label_preview.setMinimumSize(220, 220)
        self.label_preview.setStyleSheet(
            "background-color: #2a2c3b; color: #9aa0c0; border: 1px solid #3a3d4a; border-radius: 8px;")
        self.label_preview.on_click = self.preview_clicked
        layout_preview_panel.addWidget(self.label_preview, 1)

        layout_content.addLayout(layout_preview_panel)

        #Main layout
        layout_main = QVBoxLayout()
        layout_main.addWidget(group_source)
        layout_main.addWidget(group_adjust)
        layout_main.addWidget(group_recolor)
        layout_main.addWidget(group_symbol)
        layout_main.addWidget(group_size)
        layout_main.addWidget(group_output)
        layout_main.addLayout(layout_content)

        self.label_info = QLabel("Image is not loaded, yet")
        self.label_info.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout_main.addWidget(self.label_info)

        widget = QWidget()
        widget.setLayout(layout_main)
        self.setCentralWidget(widget)
        self.setWindowTitle("SS14fyImage")
        self.setGeometry(0, 0, 900, 650)
        self.setAcceptDrops(True)
        self.show()

    def dragEnterEvent(self, event):
        if event.mimeData().hasUrls():
            event.acceptProposedAction()

    def dropEvent(self, event):
        '''Lets the user drag an image file straight onto the window instead of using Open file.'''
        urls = event.mimeData().urls()
        if not urls:
            return
        local_path = urls[0].toLocalFile()
        if local_path:
            self._load_path(local_path)

    def update_label(self, length):
        '''Updates info label with image size and text length'''
        self.label_info.setText(f"{image_size['w']}x{image_size['h']} {length}/{symbol_limit}")
        self.label_info.setStyleSheet("color: #ff5555;" if length > symbol_limit else "")

    def update_textedit(self, text):
        '''Updates text edit with transformed image.'''
        self.text_edit.setText(text)

    def update_preview(self, preview):
        '''Updates the output preview with the exact image that will be rendered in-game
        (or, if a hardware style is selected, a fun stylized approximation for display only -
        Save preview and the actual SS14 text output always use the true, unstyled image).'''
        if preview is None or preview.width == 0 or preview.height == 0:
            self._preview_pixmap = None
            self.label_preview.setText("No preview, yet")
            return

        styled = preview
        style_fn = HARDWARE_STYLES.get(self.preview_style)
        if style_fn:
            try:
                styled = style_fn(preview)
            except:
                styled = preview

        data = styled.tobytes("raw", "RGBA")
        image = QImage(data, styled.width, styled.height, QImage.Format.Format_RGBA8888).copy()
        self._preview_pixmap = QPixmap.fromImage(image)
        self._rescale_preview()

    def combo_style_changed(self, text):
        self.preview_style = text
        self.update_preview(self.current_preview)

    def _rescale_preview(self):
        '''Rescales the stored full-resolution preview pixmap to fit the current panel size.'''
        if self._preview_pixmap is None:
            return
        area = self.label_preview.size()
        if area.width() <= 0 or area.height() <= 0:
            return
        scaled = self._preview_pixmap.scaled(area, Qt.AspectRatioMode.KeepAspectRatio, Qt.TransformationMode.FastTransformation)
        self.label_preview.setPixmap(scaled)

    def resizeEvent(self, event):
        '''Keep the preview correctly scaled when the window (and preview panel) is resized.'''
        super().resizeEvent(event)
        self._rescale_preview()

    def refresh(self):
        '''Reruns transform() and updates every widget with the result.'''
        text, preview = transform()
        self.current_preview = preview
        self.update_label(len(text))
        self.update_textedit(text)
        self.update_preview(preview)

    def button_reload_clicked(self):
        self.refresh()

    def checkbox_limit_checked(self, state):
        global use_limit
        use_limit = state == Qt.CheckState.Checked

    def checkbox_fullcolor_checked(self, state):
        global full_color
        full_color = state == Qt.CheckState.Checked

    def checkbox_grayscale_checked(self, state):
        global grayscale
        grayscale = state == Qt.CheckState.Checked
        if path:
            self.refresh()

    def checkbox_invert_checked(self, state):
        global invert
        invert = state == Qt.CheckState.Checked
        if path:
            self.refresh()

    def checkbox_dither_checked(self, state):
        global dither
        dither = state == Qt.CheckState.Checked
        if path:
            self.refresh()

    def edit_symbol_changed(self, text):
        global pixel
        if not text:
            return
        pixel = text
        if path:
            self.refresh()

    def combo_symbol_preset_changed(self, text):
        if text and text != "...":
            self.edit_symbol.setText(text)

    def checkbox_shading_checked(self, state):
        global symbol_mode
        symbol_mode = "shading" if state == Qt.CheckState.Checked else "solid"
        self.edit_symbol.setEnabled(symbol_mode == "solid")
        if path:
            self.refresh()

    def slider_brightness_changed(self, value):
        global brightness
        brightness = value
        if path:
            self.refresh()

    def slider_contrast_changed(self, value):
        global contrast
        contrast = value
        if path:
            self.refresh()

    def spin_posterize_changed(self, value):
        global posterize_bits
        posterize_bits = value
        if path:
            self.refresh()

    def slider_hue_changed(self, value):
        global hue_shift
        hue_shift = value
        if path:
            self.refresh()

    def button_reset_adjust_clicked(self):
        global brightness, contrast, posterize_bits, hue_shift
        brightness, contrast, posterize_bits, hue_shift = 100, 100, 8, 0
        self.slider_brightness.blockSignals(True)
        self.slider_contrast.blockSignals(True)
        self.spin_posterize.blockSignals(True)
        self.slider_hue.blockSignals(True)
        self.slider_brightness.setValue(100)
        self.slider_contrast.setValue(100)
        self.spin_posterize.setValue(8)
        self.slider_hue.setValue(0)
        self.slider_brightness.blockSignals(False)
        self.slider_contrast.blockSignals(False)
        self.spin_posterize.blockSignals(False)
        self.slider_hue.blockSignals(False)
        if path:
            self.refresh()

    def preview_clicked(self, pos):
        '''Picks the color under the click for the Recolor tool.'''
        if self.current_preview is None:
            return
        pixmap = self.label_preview.pixmap()
        if pixmap is None or pixmap.isNull():
            return

        label_w, label_h = self.label_preview.width(), self.label_preview.height()
        pm_w, pm_h = pixmap.width(), pixmap.height()
        offset_x = (label_w - pm_w) / 2
        offset_y = (label_h - pm_h) / 2
        local_x, local_y = pos.x() - offset_x, pos.y() - offset_y
        if not (0 <= local_x < pm_w and 0 <= local_y < pm_h):
            return

        src_w, src_h = self.current_preview.size
        ix = min(src_w - 1, max(0, int(local_x / pm_w * src_w)))
        iy = min(src_h - 1, max(0, int(local_y / pm_h * src_h)))
        color = self.current_preview.getpixel((ix, iy))

        self._recolor_source = color
        r, g, b, a = color
        self.button_recolor_from.setStyleSheet(f"background-color: rgba({r},{g},{b},{a});")
        self.button_recolor_from.setEnabled(True)

    def button_recolor_pick_target_clicked(self):
        color = QColorDialog.getColor(self._recolor_target, self, "Pick replacement color",
                                       QColorDialog.ColorDialogOption.ShowAlphaChannel)
        if color.isValid():
            self._recolor_target = color
            self.button_recolor_to.setStyleSheet(f"background-color: {color.name()};")

    def button_recolor_apply_clicked(self):
        if self._recolor_source is None:
            return
        target = (self._recolor_target.red(), self._recolor_target.green(),
                  self._recolor_target.blue(), self._recolor_target.alpha())
        recolor_map[self._recolor_source] = target
        self.label_recolor_count.setText(f"{len(recolor_map)} active")
        if path:
            self.refresh()

    def button_recolor_clear_clicked(self):
        recolor_map.clear()
        self.label_recolor_count.setText("0 active")
        if path:
            self.refresh()

    def button_palette_clicked(self):
        global active_palette
        dialog = PaletteDialog(self)
        if dialog.exec() == QDialog.DialogCode.Accepted and dialog.selected_palette:
            active_palette = dialog.selected_palette
            self.label_palette_status.setText(f"Palette: {len(active_palette)} colors")
            if path:
                self.refresh()

    def button_palette_clear_clicked(self):
        global active_palette
        active_palette = None
        self.label_palette_status.setText("Palette: none")
        if path:
            self.refresh()

    def button_open_clicked(self):
        selected = QFileDialog.getOpenFileName(self)[0]
        if not selected:
            return
        self._load_path(selected)

    def button_open_gif_clicked(self):
        selected, _ = QFileDialog.getOpenFileName(self, "Open image", "",
                                                    "Images (*.gif *.webp *.png *.apng);;All files (*.*)")
        if not selected:
            return

        try:
            im = Image.open(selected)
        except:
            return

        try:
            if getattr(im, "n_frames", 1) <= 1:
                # Not actually animated - just load it the normal way.
                self._load_path(selected)
                return

            dialog = FramePickerDialog(self, im)
            if dialog.exec() == QDialog.DialogCode.Accepted and dialog.selected_frame is not None:
                self.use_custom_image(dialog.selected_frame, f"(frame {dialog.selected_index} of {selected})")
        finally:
            im.close()

    def _load_path(self, selected):
        '''Loads a plain (non-animated) image file by path.'''
        global path, custom_image
        path = selected
        custom_image = None

        try:
            with Image.open(path) as im:
                original_size["w"], original_size["h"] = im.size
        except:
            original_size["w"], original_size["h"] = 1, 1

        w = min(original_size["w"], self.spin_w.maximum())
        h = min(original_size["h"], self.spin_h.maximum())

        self.spin_w.blockSignals(True)
        self.spin_h.blockSignals(True)
        self.spin_w.setValue(w)
        self.spin_h.setValue(h)
        self.spin_w.blockSignals(False)
        self.spin_h.blockSignals(False)

        resize_size["w"], resize_size["h"] = w, h
        self.refresh()

    def button_copy_clicked(self):
        QApplication.clipboard().setText(self.text_edit.toPlainText())

    def button_video_clicked(self):
        webbrowser.open(useful_video_url)

    def button_draw_clicked(self):
        dialog = DrawDialog(self, self.spin_w.value() or 16, self.spin_h.value() or 16, self.current_preview)
        if dialog.exec() == QDialog.DialogCode.Accepted:
            self.use_custom_image(dialog.applied_image, "(drawing)")

    def button_paste_clicked(self):
        image = QApplication.clipboard().image()
        if image.isNull():
            return

        image = image.convertToFormat(QImage.Format.Format_RGBA8888)
        ptr = image.bits()
        ptr.setsize(image.sizeInBytes())
        pil_image = Image.frombuffer("RGBA", (image.width(), image.height()), bytes(ptr),
                                      "raw", "RGBA", image.bytesPerLine(), 1).copy()
        self.use_custom_image(pil_image, "(clipboard)")

    def use_custom_image(self, pil_image, source_label):
        global path, custom_image
        custom_image = pil_image
        path = source_label
        original_size["w"], original_size["h"] = pil_image.size

        w = min(pil_image.width, self.spin_w.maximum())
        h = min(pil_image.height, self.spin_h.maximum())
        self.spin_w.blockSignals(True)
        self.spin_h.blockSignals(True)
        self.spin_w.setValue(w)
        self.spin_h.setValue(h)
        self.spin_w.blockSignals(False)
        self.spin_h.blockSignals(False)

        resize_size["w"], resize_size["h"] = w, h
        self.refresh()

    def button_save_text_clicked(self):
        text = self.text_edit.toPlainText()
        if not text:
            return
        filename, _ = QFileDialog.getSaveFileName(self, "Save text", "", "Text files (*.txt)")
        if not filename:
            return
        with open(filename, "w", encoding="utf-8") as f:
            f.write(text)

    def button_load_text_clicked(self):
        filename, _ = QFileDialog.getOpenFileName(self, "Load text", "", "Text files (*.txt)")
        if not filename:
            return
        try:
            with open(filename, "r", encoding="utf-8") as f:
                text = f.read()
        except:
            return

        parsed = parse_ss14_text(text)
        if parsed is None:
            # Doesn't look like valid SS14 markup - just show the raw text, no preview to derive.
            self.current_preview = None
            self.update_preview(None)
            self.update_textedit(text)
            self.label_info.setText(f"Loaded from file (not valid markup, no preview): {len(text)}/{symbol_limit}")
            return

        self.use_custom_image(parsed, f"(loaded from {filename})")

    def button_save_preview_clicked(self):
        if self.current_preview is None:
            return
        filename, _ = QFileDialog.getSaveFileName(self, "Save preview", "", "PNG files (*.png)")
        if not filename:
            return
        self.current_preview.save(filename)

    def button_normal_map_clicked(self):
        if self.current_preview is None:
            return
        dialog = NormalMapDialog(self, self.current_preview)
        dialog.exec()

    def button_reset_size_clicked(self):
        if not original_size["w"] or not original_size["h"]:
            return
        w = min(original_size["w"], self.spin_w.maximum())
        h = min(original_size["h"], self.spin_h.maximum())
        self.set_resize(w, h)

    def button_scale_clicked(self, step):
        '''Grows both width and height by `step`, keeping the current aspect ratio.'''
        w, h = self.spin_w.value(), self.spin_h.value()
        if not w or not h:
            return

        scale = (w + step) / w
        self.set_resize(w + step, max(1, round(h * scale)))

    def button_fit_clicked(self):
        w, h = original_size["w"], original_size["h"]
        if not w or not h:
            return

        ratio = min(paper_size["w"] / w, paper_size["h"] / h, 1)
        self.set_resize(max(1, round(w * ratio)), max(1, round(h * ratio)))

    def button_fit_limit_clicked(self):
        '''Shrinks the current size (keeping its aspect ratio) until the FULL image fits under symbol_limit.'''
        global use_limit
        base_w, base_h = self.spin_w.value(), self.spin_h.value()
        if not base_w or not base_h:
            return

        def full_text_len_at_scale(scale):
            global use_limit
            w = max(1, round(base_w * scale))
            h = max(1, round(base_h * scale))
            saved_resize = dict(resize_size)
            saved_limit = use_limit
            resize_size["w"], resize_size["h"] = w, h
            # Measure the TRUE, untruncated length regardless of the "Use limit" checkbox,
            # otherwise a truncated probe always looks like it "fits" and the search never shrinks.
            use_limit = False
            text, _ = transform()
            resize_size["w"], resize_size["h"] = saved_resize["w"], saved_resize["h"]
            use_limit = saved_limit
            return len(text), w, h

        length, w, h = full_text_len_at_scale(1.0)
        if length <= symbol_limit:
            return

        lo, hi = 0.0, 1.0
        for _ in range(20):
            mid = (lo + hi) / 2
            length, w, h = full_text_len_at_scale(mid)
            if length <= symbol_limit:
                lo = mid
            else:
                hi = mid

        _, w, h = full_text_len_at_scale(lo)
        self.set_resize(w, h)

    def spin_w_changed(self, value):
        height = self.spin_h.value()
        if self.checkbox_keep_ratio.isChecked() and original_size["w"]:
            height = max(1, round(value * original_size["h"] / original_size["w"]))
        self.set_resize(value, height)

    def spin_h_changed(self, value):
        width = self.spin_w.value()
        if self.checkbox_keep_ratio.isChecked() and original_size["h"]:
            width = max(1, round(value * original_size["w"] / original_size["h"]))
        self.set_resize(width, value)

    def set_resize(self, w, h):
        '''Applies a new output size to the spin boxes and reruns the transform.'''
        self.spin_w.blockSignals(True)
        self.spin_h.blockSignals(True)
        self.spin_w.setValue(w)
        self.spin_h.setValue(h)
        # Spin boxes clamp to their range, so read back what was actually applied.
        w, h = self.spin_w.value(), self.spin_h.value()
        self.spin_w.blockSignals(False)
        self.spin_h.blockSignals(False)

        resize_size["w"], resize_size["h"] = w, h
        self.refresh()


class FramePickerDialog(QDialog):
    '''Lets the user scrub through an animated image (GIF/APNG/WEBP) and pick one frame to use.'''
    def __init__(self, parent, im):
        super().__init__(parent)
        self.setWindowTitle("Pick a frame")
        self.im = im
        self.frame_count = im.n_frames
        self.current_frame = None
        self.selected_frame = None
        self.selected_index = 0

        layout = QVBoxLayout()

        self.label_preview = QLabel()
        self.label_preview.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.label_preview.setMinimumSize(300, 300)
        self.label_preview.setStyleSheet(
            "background-color: #2a2c3b; border: 1px solid #3a3d4a; border-radius: 8px;")
        layout.addWidget(self.label_preview, 1)

        controls = QHBoxLayout()

        button_prev = QPushButton("< Prev")
        button_prev.clicked.connect(lambda: self.spin_frame.setValue(max(0, self.spin_frame.value() - 1)))
        controls.addWidget(button_prev)

        controls.addWidget(QLabel("Frame:"))
        self.spin_frame = QSpinBox()
        self.spin_frame.setRange(0, self.frame_count - 1)
        self.spin_frame.valueChanged.connect(self.show_frame)
        controls.addWidget(self.spin_frame)

        controls.addWidget(QLabel(f"/ {self.frame_count - 1}"))

        button_next = QPushButton("Next >")
        button_next.clicked.connect(lambda: self.spin_frame.setValue(min(self.frame_count - 1, self.spin_frame.value() + 1)))
        controls.addWidget(button_next)

        layout.addLayout(controls)

        button_apply = QPushButton("Use this frame")
        button_apply.clicked.connect(self.apply_clicked)
        layout.addWidget(button_apply)

        self.setLayout(layout)
        self.resize(400, 450)

        self.show_frame(0)

    def show_frame(self, index):
        self.im.seek(index)
        self.current_frame = self.im.convert("RGBA").copy()

        data = self.current_frame.tobytes("raw", "RGBA")
        qimage = QImage(data, self.current_frame.width, self.current_frame.height, QImage.Format.Format_RGBA8888).copy()
        pixmap = QPixmap.fromImage(qimage)

        scaled = pixmap.scaled(self.label_preview.size(), Qt.AspectRatioMode.KeepAspectRatio, Qt.TransformationMode.FastTransformation)
        self.label_preview.setPixmap(scaled)

    def apply_clicked(self):
        self.selected_index = self.spin_frame.value()
        self.selected_frame = self.current_frame
        self.accept()


class PaletteDialog(QDialog):
    '''Extracts a small representative palette from any reference image, to apply to the current one.'''
    def __init__(self, parent):
        super().__init__(parent)
        self.setWindowTitle("Palette from reference")
        self.selected_palette = None
        self.current_palette = []
        self.reference_image = None

        layout = QVBoxLayout()

        toolbar = QHBoxLayout()

        button_load = QPushButton("Load reference image...")
        button_load.clicked.connect(self.load_reference)
        toolbar.addWidget(button_load)

        toolbar.addWidget(QLabel("Colors:"))
        self.spin_colors = QSpinBox()
        self.spin_colors.setRange(2, 64)
        self.spin_colors.setValue(16)
        self.spin_colors.valueChanged.connect(self.refresh_palette)
        toolbar.addWidget(self.spin_colors)

        for preset in (8, 16, 32):
            button_preset = QPushButton(str(preset))
            button_preset.clicked.connect(lambda checked, n=preset: self.spin_colors.setValue(n))
            toolbar.addWidget(button_preset)

        layout.addLayout(toolbar)

        self.swatches_layout = QHBoxLayout()
        self.swatches_layout.addWidget(QLabel("No reference loaded, yet"))
        self.swatches_layout.addStretch()
        layout.addLayout(self.swatches_layout)

        button_use = QPushButton("Use this palette")
        button_use.clicked.connect(self.use_clicked)
        layout.addWidget(button_use)

        self.setLayout(layout)
        self.resize(560, 200)

    def load_reference(self):
        filename, _ = QFileDialog.getOpenFileName(self, "Load reference image", "",
                                                    "Images (*.png *.jpg *.jpeg *.bmp *.gif *.webp);;All files (*.*)")
        if not filename:
            return
        try:
            self.reference_image = Image.open(filename).convert("RGB")
        except:
            return
        self.refresh_palette()

    def refresh_palette(self):
        if self.reference_image is None:
            return
        self.current_palette = extract_palette(self.reference_image, self.spin_colors.value())
        self._rebuild_swatches()

    def _rebuild_swatches(self):
        while self.swatches_layout.count():
            item = self.swatches_layout.takeAt(0)
            widget = item.widget()
            if widget:
                widget.deleteLater()

        for r, g, b in self.current_palette:
            swatch = QLabel()
            swatch.setFixedSize(22, 22)
            swatch.setStyleSheet(f"background-color: rgb({r},{g},{b}); border: 1px solid #555;")
            self.swatches_layout.addWidget(swatch)
        self.swatches_layout.addStretch()

    def use_clicked(self):
        if self.current_palette:
            self.selected_palette = self.current_palette
            self.accept()


class NormalMapDialog(QDialog):
    '''Generates a tangent-space normal map from the current output image - an export-only asset
    for real-time lighting in an external game engine. SS14's paper text can't use this itself.'''
    def __init__(self, parent, source_image):
        super().__init__(parent)
        self.setWindowTitle("Normal Map Generator")
        self.source_image = source_image.convert("RGBA")
        self.result_image = None

        layout = QVBoxLayout()

        info = QLabel(
            "Generates an RGB normal map from this image's shape and brightness, for use as a "
            "lighting asset in an external game engine. This is an export-only tool - SS14 paper "
            "text has no use for it.")
        info.setWordWrap(True)
        layout.addWidget(info)

        controls = QHBoxLayout()

        controls.addWidget(QLabel("Strength:"))
        self.slider_strength = QSlider(Qt.Orientation.Horizontal)
        self.slider_strength.setRange(1, 100)
        self.slider_strength.setValue(35)
        self.slider_strength.valueChanged.connect(self.regenerate)
        controls.addWidget(self.slider_strength)

        self.checkbox_silhouette = QCheckBox("Round the silhouette edges")
        self.checkbox_silhouette.setChecked(True)
        self.checkbox_silhouette.checkStateChanged.connect(self.regenerate)
        controls.addWidget(self.checkbox_silhouette)

        layout.addLayout(controls)

        previews = QHBoxLayout()

        self.label_source = QLabel()
        self.label_source.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.label_source.setMinimumSize(200, 200)
        self.label_source.setStyleSheet("background-color: #2a2c3b; border: 1px solid #3a3d4a; border-radius: 8px;")
        previews.addWidget(self.label_source)

        self.label_result = QLabel()
        self.label_result.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.label_result.setMinimumSize(200, 200)
        self.label_result.setStyleSheet("background-color: #2a2c3b; border: 1px solid #3a3d4a; border-radius: 8px;")
        previews.addWidget(self.label_result)

        layout.addLayout(previews, 1)

        button_save = QPushButton("Save normal map as PNG...")
        button_save.clicked.connect(self.save_clicked)
        layout.addWidget(button_save)

        self.setLayout(layout)
        self.resize(520, 470)

        self._show_pixmap(self.label_source, self.source_image)
        self.regenerate()

    def _show_pixmap(self, label, pil_image):
        data = pil_image.convert("RGBA").tobytes("raw", "RGBA")
        qimage = QImage(data, pil_image.width, pil_image.height, QImage.Format.Format_RGBA8888).copy()
        pixmap = QPixmap.fromImage(qimage)
        scaled = pixmap.scaled(label.size(), Qt.AspectRatioMode.KeepAspectRatio, Qt.TransformationMode.FastTransformation)
        label.setPixmap(scaled)

    def regenerate(self):
        strength = self.slider_strength.value() / 10
        use_silhouette = self.checkbox_silhouette.isChecked()
        self.result_image = generate_normal_map(self.source_image, strength, use_silhouette)
        self._show_pixmap(self.label_result, self.result_image)

    def save_clicked(self):
        if self.result_image is None:
            return
        filename, _ = QFileDialog.getSaveFileName(self, "Save normal map", "", "PNG files (*.png)")
        if not filename:
            return
        self.result_image.save(filename)


class PixelCanvas(QWidget):
    '''A grid of pixels that can be painted by click (and click-drag).'''
    def __init__(self, w, h, cell_size=20):
        super().__init__()
        self.cell_size = cell_size
        self.color = QColor(0, 0, 0, 255)
        self.tile_mode = False
        self.image = QImage(w, h, QImage.Format.Format_ARGB32)
        self.image.fill(Qt.GlobalColor.transparent)
        self._apply_fixed_size()

        self.undo_stack = []
        self.on_color_picked = None
        self.tool = "pencil"
        self.filled_shapes = False
        self.symmetry = False
        self._drag_base = None
        self._drag_start = None
        self.iso_grid = False
        self.iso_tile_w = 4

    @classmethod
    def from_pil(cls, pil_image):
        '''Builds a canvas pre-filled with an existing PIL image, so it can be touched up.'''
        pil_image = pil_image.convert("RGBA")
        data = pil_image.tobytes("raw", "RGBA")
        qimage = QImage(data, pil_image.width, pil_image.height, QImage.Format.Format_RGBA8888).copy()

        canvas = cls(pil_image.width, pil_image.height)
        canvas.image = qimage.convertToFormat(QImage.Format.Format_ARGB32)
        return canvas

    def _apply_fixed_size(self):
        factor = 3 if self.tile_mode else 1
        self.setFixedSize(self.image.width() * self.cell_size * factor, self.image.height() * self.cell_size * factor)

    def set_tile_mode(self, enabled):
        self.tile_mode = enabled
        self._apply_fixed_size()
        self.update()

    def resize_canvas(self, w, h):
        '''Resizes the canvas, keeping existing pixels anchored to the top-left corner.'''
        new_image = QImage(w, h, QImage.Format.Format_ARGB32)
        new_image.fill(Qt.GlobalColor.transparent)
        painter = QPainter(new_image)
        painter.drawImage(0, 0, self.image)
        painter.end()

        self.image = new_image
        self._apply_fixed_size()
        self.update()

    def clear(self):
        self._push_undo()
        self.image.fill(Qt.GlobalColor.transparent)
        self.update()

    def flip_horizontal(self):
        self._push_undo()
        self.image = self.image.mirrored(True, False)
        self.update()

    def flip_vertical(self):
        self._push_undo()
        self.image = self.image.mirrored(False, True)
        self.update()

    def rotate_90(self):
        self._push_undo()
        self.image = self.image.transformed(QTransform().rotate(90), Qt.TransformationMode.FastTransformation)
        self._apply_fixed_size()
        self.update()

    def cell_at(self, pos):
        x, y = pos.x() // self.cell_size, pos.y() // self.cell_size
        if self.tile_mode:
            # The widget shows a 3x3 repeat of the canvas; wrap any click back into the real 0..w-1/0..h-1 range
            # so painting across a tile seam is possible and wraps around, like a proper seamless-tile editor.
            x = (x - self.image.width()) % self.image.width()
            y = (y - self.image.height()) % self.image.height()
        return x, y

    def paint_cell(self, x, y, erase):
        if 0 <= x < self.image.width() and 0 <= y < self.image.height():
            self.image.setPixelColor(x, y, QColor(0, 0, 0, 0) if erase else self.color)
            self.update()

    def paint_cell_symmetric(self, x, y, erase):
        self.paint_cell(x, y, erase)
        if self.symmetry:
            self.paint_cell(self.image.width() - 1 - x, y, erase)

    def _line_points(self, x0, y0, x1, y1):
        '''Bresenham's line algorithm.'''
        points = []
        dx = abs(x1 - x0)
        sx = 1 if x0 < x1 else -1
        dy = -abs(y1 - y0)
        sy = 1 if y0 < y1 else -1
        err = dx + dy
        x, y = x0, y0
        while True:
            points.append((x, y))
            if x == x1 and y == y1:
                break
            e2 = 2 * err
            if e2 >= dy:
                err += dy
                x += sx
            if e2 <= dx:
                err += dx
                y += sy
        return points

    def _rect_points(self, x0, y0, x1, y1):
        xmin, xmax = min(x0, x1), max(x0, x1)
        ymin, ymax = min(y0, y1), max(y0, y1)
        points = []
        if self.filled_shapes:
            for x in range(xmin, xmax + 1):
                for y in range(ymin, ymax + 1):
                    points.append((x, y))
        else:
            for x in range(xmin, xmax + 1):
                points.append((x, ymin))
                points.append((x, ymax))
            for y in range(ymin, ymax + 1):
                points.append((xmin, y))
                points.append((xmax, y))
        return points

    def _draw_shape_preview(self, x, y):
        '''Redraws the base image plus a live preview of the in-progress line/rect.'''
        self.image = self._drag_base.copy()
        x0, y0 = self._drag_start
        points = self._line_points(x0, y0, x, y) if self.tool == "line" else self._rect_points(x0, y0, x, y)
        w, h = self.image.width(), self.image.height()
        for px, py in points:
            if 0 <= px < w and 0 <= py < h:
                self.image.setPixelColor(px, py, self.color)
                if self.symmetry:
                    self.image.setPixelColor(w - 1 - px, py, self.color)
        self.update()

    def flood_fill(self, x, y, new_color):
        w, h = self.image.width(), self.image.height()
        if not (0 <= x < w and 0 <= y < h):
            return
        target = self.image.pixelColor(x, y)
        if target == new_color:
            return

        self._push_undo()
        stack = [(x, y)]
        visited = set()
        while stack:
            cx, cy = stack.pop()
            if not (0 <= cx < w and 0 <= cy < h) or (cx, cy) in visited:
                continue
            if self.image.pixelColor(cx, cy) != target:
                continue
            visited.add((cx, cy))
            self.image.setPixelColor(cx, cy, new_color)
            stack.extend([(cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1)])
        self.update()

    def _push_undo(self):
        self.undo_stack.append(self.image.copy())
        if len(self.undo_stack) > 50:
            self.undo_stack.pop(0)

    def undo(self):
        if not self.undo_stack:
            return
        self.image = self.undo_stack.pop()
        self._apply_fixed_size()
        self.update()

    def pick_color_at(self, x, y):
        if 0 <= x < self.image.width() and 0 <= y < self.image.height():
            picked = self.image.pixelColor(x, y)
            if picked.alpha() > 0:
                self.color = picked
                if self.on_color_picked:
                    self.on_color_picked(picked)

    def mousePressEvent(self, event):
        x, y = self.cell_at(event.position().toPoint())
        if event.button() == Qt.MouseButton.MiddleButton:
            self.pick_color_at(x, y)
            return
        if event.button() == Qt.MouseButton.RightButton:
            self._push_undo()
            self.paint_cell_symmetric(x, y, True)
            return
        if self.tool == "fill":
            self.flood_fill(x, y, self.color)
        elif self.tool in ("line", "rect"):
            self._drag_base = self.image.copy()
            self._drag_start = (x, y)
            self._draw_shape_preview(x, y)
        else:
            self._push_undo()
            self.paint_cell_symmetric(x, y, False)

    def mouseMoveEvent(self, event):
        x, y = self.cell_at(event.position().toPoint())
        if event.buttons() & Qt.MouseButton.RightButton:
            self.paint_cell_symmetric(x, y, True)
        elif self.tool == "pencil" and event.buttons() & Qt.MouseButton.LeftButton:
            self.paint_cell_symmetric(x, y, False)
        elif self.tool in ("line", "rect") and event.buttons() & Qt.MouseButton.LeftButton and self._drag_start is not None:
            self._draw_shape_preview(x, y)

    def mouseReleaseEvent(self, event):
        if event.button() == Qt.MouseButton.LeftButton and self._drag_base is not None:
            self.undo_stack.append(self._drag_base)
            if len(self.undo_stack) > 50:
                self.undo_stack.pop(0)
            self._drag_base = None
            self._drag_start = None

    def paintEvent(self, event):
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.SmoothPixmapTransform, False)

        w, h = self.image.width(), self.image.height()

        if self.tile_mode:
            for ty in (-1, 0, 1):
                for tx in (-1, 0, 1):
                    self._paint_tile_copy(painter, tx * w, ty * h, w, h, dim=(tx, ty) != (0, 0))
            # Highlight the real (center) tile so it's obvious which copy is the actual canvas.
            painter.setPen(QColor(80, 200, 255, 220))
            painter.drawRect(w * self.cell_size, h * self.cell_size, w * self.cell_size - 1, h * self.cell_size - 1)
            iso_offset = (w, h)
        else:
            self._paint_tile_copy(painter, 0, 0, w, h, dim=False)
            iso_offset = (0, 0)

        if self.iso_grid:
            self._draw_iso_grid(painter, w, h, iso_offset)

    def _paint_tile_copy(self, painter, cell_offset_x, cell_offset_y, w, h, dim):
        '''Draws one copy of the canvas at a cell offset - used to render the 3x3 tile-mode preview.'''
        ox, oy = cell_offset_x * self.cell_size, cell_offset_y * self.cell_size

        light, dark = QColor(200, 200, 200), QColor(160, 160, 160)
        for gy in range(h):
            for gx in range(w):
                painter.fillRect(ox + gx * self.cell_size, oy + gy * self.cell_size, self.cell_size, self.cell_size,
                                  light if (gx + gy) % 2 == 0 else dark)

        painter.drawImage(QRect(ox, oy, w * self.cell_size, h * self.cell_size), self.image)

        if dim:
            painter.fillRect(ox, oy, w * self.cell_size, h * self.cell_size, QColor(10, 10, 15, 120))
        else:
            painter.setPen(QColor(0, 0, 0, 60))
            for gx in range(w + 1):
                painter.drawLine(ox + gx * self.cell_size, oy, ox + gx * self.cell_size, oy + h * self.cell_size)
            for gy in range(h + 1):
                painter.drawLine(ox, oy + gy * self.cell_size, ox + w * self.cell_size, oy + gy * self.cell_size)

    def _draw_iso_grid(self, painter, w, h, offset=(0, 0)):
        '''Draws a 2:1 isometric diamond guide grid on top - a visual aid only, doesn't affect pixels.'''
        tw = max(2, self.iso_tile_w)
        th = max(1, tw // 2)
        px_w, px_h = w * self.cell_size, h * self.cell_size
        dx, dy = tw * self.cell_size, th * self.cell_size
        if dx <= 0:
            return

        ox, oy = offset[0] * self.cell_size, offset[1] * self.cell_size
        # Generous overshoot in both directions - QPainter clips to the widget bounds for us.
        span_steps = (px_w // dx) + (px_h // max(dy, 1)) + 2

        painter.setPen(QColor(80, 200, 255, 140))
        x = -span_steps * dx
        while x <= px_w + span_steps * dx:
            painter.drawLine(int(ox + x), int(oy), int(ox + x + span_steps * dx), int(oy + span_steps * dy))
            painter.drawLine(int(ox + x), int(oy), int(ox + x - span_steps * dx), int(oy + span_steps * dy))
            x += dx

    def to_pil(self):
        '''Converts the drawn grid into a PIL image.'''
        img = self.image.convertToFormat(QImage.Format.Format_RGBA8888)
        ptr = img.bits()
        ptr.setsize(img.sizeInBytes())
        return Image.frombuffer("RGBA", (img.width(), img.height()), bytes(ptr), "raw", "RGBA", img.bytesPerLine(), 1).copy()


class DrawDialog(QDialog):
    '''A small pixel-art editor used to draw an image (or touch up the current output) by hand.'''
    def __init__(self, parent, initial_w, initial_h, initial_image=None):
        super().__init__(parent)
        self.setWindowTitle("Draw")
        self.applied_image = None
        self.recent_colors = []

        if initial_image is not None and initial_image.width and initial_image.height:
            self.canvas = PixelCanvas.from_pil(initial_image)
        else:
            self.canvas = PixelCanvas(max(1, min(initial_w, 100)), max(1, min(initial_h, 100)))

        layout = QVBoxLayout()

        self.canvas.on_color_picked = self._on_color_picked

        toolbar = QHBoxLayout()

        toolbar.addWidget(QLabel("Left: paint/fill. Right: erase. Middle: pick color."))

        self.button_color = QPushButton()
        self.button_color.setFixedWidth(50)
        self._update_color_button()
        self.button_color.clicked.connect(self.pick_color)
        toolbar.addWidget(self.button_color)

        self.button_tool_pencil = QPushButton("Pencil")
        self.button_tool_pencil.setCheckable(True)
        self.button_tool_pencil.setChecked(True)
        self.button_tool_pencil.clicked.connect(lambda: self.set_tool("pencil"))
        toolbar.addWidget(self.button_tool_pencil)

        self.button_tool_fill = QPushButton("Fill")
        self.button_tool_fill.setCheckable(True)
        self.button_tool_fill.clicked.connect(lambda: self.set_tool("fill"))
        toolbar.addWidget(self.button_tool_fill)

        self.button_tool_line = QPushButton("Line")
        self.button_tool_line.setCheckable(True)
        self.button_tool_line.clicked.connect(lambda: self.set_tool("line"))
        toolbar.addWidget(self.button_tool_line)

        self.button_tool_rect = QPushButton("Rect")
        self.button_tool_rect.setCheckable(True)
        self.button_tool_rect.clicked.connect(lambda: self.set_tool("rect"))
        toolbar.addWidget(self.button_tool_rect)

        button_undo = QPushButton("Undo")
        button_undo.clicked.connect(self.canvas.undo)
        toolbar.addWidget(button_undo)
        QShortcut(QKeySequence("Ctrl+Z"), self).activated.connect(self.canvas.undo)

        button_clear = QPushButton("Clear")
        button_clear.clicked.connect(self.canvas.clear)
        toolbar.addWidget(button_clear)

        layout.addLayout(toolbar)

        toolbar2 = QHBoxLayout()

        button_flip_h = QPushButton("Flip H")
        button_flip_h.clicked.connect(self.canvas.flip_horizontal)
        toolbar2.addWidget(button_flip_h)

        button_flip_v = QPushButton("Flip V")
        button_flip_v.clicked.connect(self.canvas.flip_vertical)
        toolbar2.addWidget(button_flip_v)

        button_rotate = QPushButton("Rotate 90°")
        button_rotate.clicked.connect(self.rotate_clicked)
        toolbar2.addWidget(button_rotate)

        button_zoom_out = QPushButton("Zoom -")
        button_zoom_out.clicked.connect(lambda: self.zoom_canvas(-4))
        toolbar2.addWidget(button_zoom_out)

        button_zoom_in = QPushButton("Zoom +")
        button_zoom_in.clicked.connect(lambda: self.zoom_canvas(4))
        toolbar2.addWidget(button_zoom_in)

        self.checkbox_filled = QCheckBox("Filled")
        self.checkbox_filled.checkStateChanged.connect(self.checkbox_filled_checked)
        toolbar2.addWidget(self.checkbox_filled)

        self.checkbox_symmetry = QCheckBox("Symmetry")
        self.checkbox_symmetry.checkStateChanged.connect(self.checkbox_symmetry_checked)
        toolbar2.addWidget(self.checkbox_symmetry)

        self.checkbox_iso = QCheckBox("Iso grid")
        self.checkbox_iso.checkStateChanged.connect(self.checkbox_iso_checked)
        toolbar2.addWidget(self.checkbox_iso)

        toolbar2.addWidget(QLabel("Tile:"))
        self.spin_iso_tile = QSpinBox()
        self.spin_iso_tile.setRange(2, 20)
        self.spin_iso_tile.setValue(self.canvas.iso_tile_w)
        self.spin_iso_tile.valueChanged.connect(self.spin_iso_tile_changed)
        toolbar2.addWidget(self.spin_iso_tile)

        self.checkbox_tile_mode = QCheckBox("Seamless tile mode")
        self.checkbox_tile_mode.checkStateChanged.connect(self.checkbox_tile_mode_checked)
        toolbar2.addWidget(self.checkbox_tile_mode)

        toolbar2.addWidget(QLabel("W:"))
        self.spin_w = QSpinBox()
        self.spin_w.setRange(1, 100)
        self.spin_w.setValue(self.canvas.image.width())
        toolbar2.addWidget(self.spin_w)

        toolbar2.addWidget(QLabel("H:"))
        self.spin_h = QSpinBox()
        self.spin_h.setRange(1, 100)
        self.spin_h.setValue(self.canvas.image.height())
        toolbar2.addWidget(self.spin_h)

        button_resize = QPushButton("Resize canvas")
        button_resize.clicked.connect(self.resize_canvas_clicked)
        toolbar2.addWidget(button_resize)

        layout.addLayout(toolbar2)

        self.swatches_layout = QHBoxLayout()
        self.swatches_layout.addWidget(QLabel("Recent colors:"))
        self.swatches_layout.addStretch()
        layout.addLayout(self.swatches_layout)

        scroll_area = QScrollArea()
        scroll_area.setWidget(self.canvas)
        layout.addWidget(scroll_area, 1)

        button_apply = QPushButton("Use this drawing")
        button_apply.clicked.connect(self.apply_clicked)
        layout.addWidget(button_apply)

        self.setLayout(layout)
        self.resize(650, 600)

    def _update_color_button(self):
        self.button_color.setStyleSheet(f"background-color: {self.canvas.color.name()};")

    def set_tool(self, tool):
        self.canvas.tool = tool
        self.button_tool_pencil.setChecked(tool == "pencil")
        self.button_tool_fill.setChecked(tool == "fill")
        self.button_tool_line.setChecked(tool == "line")
        self.button_tool_rect.setChecked(tool == "rect")

    def checkbox_filled_checked(self, state):
        self.canvas.filled_shapes = state == Qt.CheckState.Checked

    def checkbox_symmetry_checked(self, state):
        self.canvas.symmetry = state == Qt.CheckState.Checked

    def checkbox_iso_checked(self, state):
        self.canvas.iso_grid = state == Qt.CheckState.Checked
        self.canvas.update()

    def spin_iso_tile_changed(self, value):
        self.canvas.iso_tile_w = value
        if self.canvas.iso_grid:
            self.canvas.update()

    def checkbox_tile_mode_checked(self, state):
        self.canvas.set_tile_mode(state == Qt.CheckState.Checked)

    def pick_color(self):
        color = QColorDialog.getColor(self.canvas.color, self, "Pick color", QColorDialog.ColorDialogOption.ShowAlphaChannel)
        if color.isValid():
            self.canvas.color = color
            self._update_color_button()
            self._add_recent_color(color)

    def _on_color_picked(self, color):
        self._update_color_button()
        self._add_recent_color(color)

    def _add_recent_color(self, color):
        key = color.name(QColor.NameFormat.HexArgb)
        self.recent_colors = [c for c in self.recent_colors if c.name(QColor.NameFormat.HexArgb) != key]
        self.recent_colors.insert(0, QColor(color))
        self.recent_colors = self.recent_colors[:10]
        self._rebuild_swatches()

    def _rebuild_swatches(self):
        while self.swatches_layout.count():
            item = self.swatches_layout.takeAt(0)
            widget = item.widget()
            if widget:
                widget.deleteLater()

        self.swatches_layout.addWidget(QLabel("Recent colors:"))
        for color in self.recent_colors:
            button = QPushButton()
            button.setFixedSize(24, 24)
            button.setStyleSheet(f"background-color: {color.name()}; border: 1px solid #555;")
            button.clicked.connect(lambda checked, c=color: self._select_swatch(c))
            self.swatches_layout.addWidget(button)
        self.swatches_layout.addStretch()

    def _select_swatch(self, color):
        self.canvas.color = QColor(color)
        self._update_color_button()

    def rotate_clicked(self):
        self.canvas.rotate_90()
        self._sync_size_spinboxes()

    def zoom_canvas(self, delta):
        self.canvas.cell_size = max(4, min(60, self.canvas.cell_size + delta))
        self.canvas._apply_fixed_size()
        self.canvas.update()

    def _sync_size_spinboxes(self):
        self.spin_w.blockSignals(True)
        self.spin_h.blockSignals(True)
        self.spin_w.setValue(self.canvas.image.width())
        self.spin_h.setValue(self.canvas.image.height())
        self.spin_w.blockSignals(False)
        self.spin_h.blockSignals(False)

    def resize_canvas_clicked(self):
        self.canvas.resize_canvas(self.spin_w.value(), self.spin_h.value())

    def apply_clicked(self):
        self.applied_image = self.canvas.to_pil()
        self.accept()


class DisclaimerDialog(QDialog):
    '''Shown once on startup: what this program is based on, and who modified it.'''
    def __init__(self):
        super().__init__()
        self.setWindowTitle("SS14fyImage")
        self.setFixedSize(400, 200)

        layout = QVBoxLayout()

        label = QLabel(
            "<b>SS14fyImage</b><br><br>"
            f"This program is based on <a href='{original_repo_url}'>SSfyImage</a> "
            "by Tunguso4ka.<br><br>"
            f"Modified by <a href='{modder_url}'>Sk1rs</a>."
        )
        label.setWordWrap(True)
        label.setOpenExternalLinks(True)
        label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.addWidget(label)

        self.seconds_left = disclaimer_seconds

        self.button_continue = QPushButton()
        self.button_continue.setEnabled(False)
        self.button_continue.clicked.connect(self.accept)
        layout.addWidget(self.button_continue)

        self.setLayout(layout)

        self.update_button_text()
        self.timer = QTimer(self)
        self.timer.timeout.connect(self.tick)
        self.timer.start(1000)

    def update_button_text(self):
        if self.seconds_left > 0:
            self.button_continue.setText(f"Continue ({self.seconds_left})")
        else:
            self.button_continue.setText("Continue")
            self.button_continue.setEnabled(True)

    def tick(self):
        self.seconds_left -= 1
        if self.seconds_left <= 0:
            self.timer.stop()
        self.update_button_text()

    def closeEvent(self, event):
        '''Only allow closing (via the X button too) once the wait is over.'''
        if self.button_continue.isEnabled():
            event.accept()
        else:
            event.ignore()


if __name__ == "__main__":
    if len(argv) > 1:
        path = argv[1]
    if cli:
        if path == "":
            path = input('Path to image: ')
        text, _ = transform()
        print(f"{path}: {image_size['w']}x{image_size['h']} {len(text)}/{symbol_limit}\n\n{text}")
        input() # For Windows compatibility probably
        exit()

    App = QApplication(argv)
    App.setStyleSheet(STYLE_SHEET)
    DisclaimerDialog().exec()
    window = Window()
    exit(App.exec())
