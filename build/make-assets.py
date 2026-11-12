"""
MUTEit asset generator.

Builds every image and sound of the repository from the two logo sources and the Material Design Icons paths:

    assets/logo-source.png   circle logo ("M" cut out), white on transparent
    assets/glyph-source.png  "M" glyph, white on transparent

Requirements: Python 3.10+, Pillow, cairosvg, numpy (pip install pillow cairosvg numpy).

    python build/make-assets.py

Outputs (see docs/nfo/IMAGES.md):
    src/MUTEit.App/Assets/       muteit.ico, app-256.png, logo-mask.png, glyph-mask.png, menu/*.png, sounds/*.wav
    build/installer/images/      wizard-*.bmp, wizard-small-*.bmp
    preview/                     favicon.png, logo.png, preview-collage.png
"""

import io
import json
import math
import struct
import wave
from pathlib import Path

import cairosvg
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "src" / "MUTEit.App" / "Assets"
INSTALLER = ROOT / "build" / "installer" / "images"
PREVIEW = ROOT / "preview"
ICONS = json.loads((ROOT / "build" / "mdi-paths.json").read_text(encoding="utf-8"))

TEAL = (0x4F, 0xD1, 0xC5)
TEAL_DARK = (0x0F, 0x76, 0x6E)  # accent of the light theme: readable on white
RED = (0xE5, 0x48, 0x4D)
BG = (0x1A, 0x1D, 0x23)
BG2 = (0x22, 0x26, 0x2E)
RAISED = (0x2A, 0x2F, 0x38)
BORDER = (0x34, 0x3A, 0x45)
TEXT = (0xEC, 0xEF, 0xF4)
TEXT2 = (0xA7, 0xB0, 0xBE)

FONT_DIR = Path("/usr/share/fonts/opentype/inter")


def font(weight: str, size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(str(FONT_DIR / f"Inter-{weight}.otf"), size)


def square(img: Image.Image) -> Image.Image:
    """Pads to a square canvas (keeps the image centred)."""
    w, h = img.size
    s = max(w, h)
    out = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    out.paste(img, ((s - w) // 2, (s - h) // 2))
    return out


def mask_of(img: Image.Image) -> Image.Image:
    """Alpha channel of a white-on-transparent source."""
    return img.convert("RGBA").split()[3]


def tint(mask: Image.Image, color, size: int) -> Image.Image:
    m = mask.resize((size, size), Image.LANCZOS)
    out = Image.new("RGBA", (size, size), color + (0,))
    out.putalpha(m)
    return out


def white_mask_png(mask: Image.Image, size: int) -> Image.Image:
    return tint(mask, (255, 255, 255), size)


def mdi_png(name: str, size: int) -> Image.Image:
    svg = f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" width="{size}" height="{size}"><path fill="#FFFFFF" d="{ICONS[name]}"/></svg>'
    return Image.open(io.BytesIO(cairosvg.svg2png(bytestring=svg.encode()))).convert("RGBA")


def gradient(size, top, bottom) -> Image.Image:
    w, h = size
    arr = np.zeros((h, w, 3), dtype=np.uint8)
    for y in range(h):
        t = y / max(1, h - 1)
        arr[y, :, :] = [round(top[i] + (bottom[i] - top[i]) * t) for i in range(3)]
    return Image.fromarray(arr, "RGB")


def rounded(size: int, radius: int, fill, outline=None) -> Image.Image:
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((0, 0, size - 1, size - 1), radius=radius, fill=fill, outline=outline, width=max(1, size // 64))
    return img


# ---------------------------------------------------------------------------- app icon and logo images

def build_app_images(logo_mask: Image.Image, glyph_mask: Image.Image) -> None:
    ASSETS.mkdir(parents=True, exist_ok=True)
    white_mask_png(logo_mask, 256).save(ASSETS / "logo-mask.png")
    white_mask_png(glyph_mask, 256).save(ASSETS / "glyph-mask.png")

    teal256 = tint(logo_mask, TEAL, 256)
    teal256.save(ASSETS / "app-256.png")

    # Windows icon: teal circle logo, the cut-out "M" stays transparent (works on light and dark backgrounds).
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    frames = [tint(logo_mask, TEAL, s) for s in sizes]
    frames[-1].save(ASSETS / "muteit.ico", sizes=[(s, s) for s in sizes], append_images=frames[:-1])

    PREVIEW.mkdir(parents=True, exist_ok=True)
    teal256.save(PREVIEW / "favicon.png")
    tint(logo_mask, TEAL, 512).save(PREVIEW / "logo.png")


# ---------------------------------------------------------------------------- tray menu images (white masks, tinted at runtime)

MENU = {
    "settings": "mdiCog",
    "mic": "mdiMicrophone",
    "mic-off": "mdiMicrophoneOff",
    "mode": "mdiKeyboardOutline",
    "device": "mdiMicrophoneVariant",
    "overlay": "mdiPictureInPictureBottomRight",
    "taskbar": "mdiDockBottom",
    "autostart": "mdiRocketLaunch",
    "exit": "mdiExitToApp",
    "check": "mdiCheck",
    "sound": "mdiVolumeHigh",
}


def build_overlay_masks() -> None:
    """Microphone symbols of the desktop overlay (white masks, tinted at runtime)."""
    target = ASSETS / "overlay"
    target.mkdir(parents=True, exist_ok=True)
    mdi_png("mdiMicrophone", 256).save(target / "mic.png")
    mdi_png("mdiMicrophoneOff", 256).save(target / "mic-off.png")


def build_menu_images() -> None:
    target = ASSETS / "menu"
    target.mkdir(parents=True, exist_ok=True)
    for file, icon in MENU.items():
        mdi_png(icon, 32).save(target / f"{file}.png")


# ---------------------------------------------------------------------------- notification sounds

def tone_sequence(freqs, rate=48000, tone_ms=75, gap_ms=18, volume=0.42) -> bytes:
    samples = []
    for index, f in enumerate(freqs):
        n = int(rate * tone_ms / 1000)
        for i in range(n):
            t = i / rate
            attack = min(1.0, i / (rate * 0.004))
            release = min(1.0, (n - i) / (rate * 0.045))
            env = attack * release
            v = math.sin(2 * math.pi * f * t) + 0.18 * math.sin(4 * math.pi * f * t)
            samples.append(v * env * volume / 1.18)
        if index < len(freqs) - 1:
            samples.extend([0.0] * int(rate * gap_ms / 1000))
    samples.extend([0.0] * int(rate * 0.03))
    return b"".join(struct.pack("<h", int(max(-1, min(1, s)) * 32767)) for s in samples)


def build_sounds() -> None:
    target = ASSETS / "sounds"
    target.mkdir(parents=True, exist_ok=True)
    for name, freqs in (("mute", (880.0, 587.33)), ("unmute", (587.33, 880.0))):
        with wave.open(str(target / f"{name}.wav"), "wb") as w:
            w.setnchannels(1)
            w.setsampwidth(2)
            w.setframerate(48000)
            w.writeframes(tone_sequence(freqs))


# ---------------------------------------------------------------------------- installer images

def diagonal_gradient(size, top_left, bottom_right) -> Image.Image:
    """Smooth diagonal gradient (top left → bottom right)."""
    w, h = size
    yy, xx = np.mgrid[0:h, 0:w]
    t = (xx / max(1, w - 1) * 0.35 + yy / max(1, h - 1) * 0.65)[..., None]
    a = np.array(top_left, dtype=float)
    b = np.array(bottom_right, dtype=float)
    return Image.fromarray((a + (b - a) * t).round().astype(np.uint8), "RGB")


def glow(size: int, color, strength: float) -> Image.Image:
    """Soft radial glow behind the logo."""
    yy, xx = np.mgrid[0:size, 0:size]
    r = np.sqrt((xx - size / 2) ** 2 + (yy - size / 2) ** 2) / (size / 2)
    alpha = (np.clip(1 - r, 0, 1) ** 2 * 255 * strength).astype(np.uint8)
    img = Image.new("RGBA", (size, size), color + (0,))
    img.putalpha(Image.fromarray(alpha))
    return img


WIZARD_TOP = (0x15, 0x18, 0x1D)
WIZARD_BOTTOM = (0x0E, 0x3B, 0x38)


def build_wizard(logo_mask: Image.Image) -> None:
    """Welcome / finish image (WizardImageFile) and small header image (WizardSmallImageFile), no text."""
    INSTALLER.mkdir(parents=True, exist_ok=True)
    for scale, (w, h), small in (
        (100, (164, 314), 55), (125, (205, 392), 69), (150, (246, 471), 82), (175, (287, 550), 96),
        (200, (328, 628), 110), (225, (369, 706), 124), (250, (410, 785), 138),
    ):
        img = diagonal_gradient((w, h), WIZARD_TOP, WIZARD_BOTTOM).convert("RGBA")
        size = int(w * 0.56)
        cx, cy = w // 2, int(h * 0.40)
        g = glow(int(size * 1.9), TEAL, 0.22)
        img.alpha_composite(g, (cx - g.width // 2, cy - g.height // 2))
        img.alpha_composite(tint(logo_mask, TEAL, size), (cx - size // 2, cy - size // 2))
        img.convert("RGB").save(INSTALLER / f"wizard-{scale}.bmp")

        # Header image (top right): Teal logo on white, the background of the wizard header.
        tile = Image.new("RGBA", (small, small), (255, 255, 255, 255))
        inner = int(small * 0.78)
        tile.alpha_composite(tint(logo_mask, TEAL_DARK, inner), ((small - inner) // 2, (small - inner) // 2))
        tile.convert("RGB").save(INSTALLER / f"wizard-small-{scale}.bmp")


# ---------------------------------------------------------------------------- preview banner

def badge(size: int, color, slash: bool) -> Image.Image:
    s = size * 4
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse((0, 0, s - 1, s - 1), fill=color + (255,))
    if slash:
        w = s // 7
        d.line((s * 0.28, s * 0.28, s * 0.72, s * 0.72), fill=(255, 255, 255, 255), width=w)
    return img.resize((size, size), Image.LANCZOS)


def tray_glyph(glyph_mask: Image.Image, size: int, color, state_color, slash: bool) -> Image.Image:
    img = tint(glyph_mask, TEXT if color is None else color, size)
    b = int(size * 0.5)
    hole = Image.new("L", (size, size), 0)
    ImageDraw.Draw(hole).ellipse((size - b - size * 0.06, size - b - size * 0.06, size + size * 0.06, size + size * 0.06), fill=255)
    alpha = img.split()[3]
    alpha = Image.fromarray(np.minimum(np.array(alpha), 255 - np.array(hole)).astype(np.uint8))
    img.putalpha(alpha)
    img.alpha_composite(badge(b, state_color, slash), (size - b, size - b))
    return img


def build_collage(logo_mask: Image.Image, glyph_mask: Image.Image) -> None:
    W, H = 1600, 820
    img = gradient((W, H), BG, BG2).convert("RGBA")
    d = ImageDraw.Draw(img)

    logo = tint(logo_mask, TEAL, 300)
    img.alpha_composite(logo, (120, 150))

    d.text((480, 150), "MUTEit", font=font("SemiBold", 132), fill=TEXT)
    d.text((486, 318), "Microphone mute control for the Windows system tray", font=font("Medium", 36), fill=TEXT)
    d.text((486, 372), "Hotkey · push-to-talk · overlay · taskbar switch · local-first", font=font("Regular", 28), fill=TEXT2)

    chips = (("Live", TEAL, False), ("Muted", RED, False))
    x = 486
    for label, color, slash in chips:
        w = 250
        d.rounded_rectangle((x, 450, x + w, 540), radius=18, fill=RAISED + (255,), outline=BORDER + (255,), width=2)
        img.alpha_composite(tray_glyph(glyph_mask, 52, TEXT, color, slash), (x + 24, 469))
        d.text((x + 96, 474), label, font=font("Medium", 32), fill=TEXT)
        x += w + 24

    # Overlay preview
    ov = 90
    d.rounded_rectangle((x, 450, x + 240, 540), radius=18, fill=RAISED + (255,), outline=BORDER + (255,), width=2)
    disc = tint(logo_mask, RED, 64)
    img.alpha_composite(disc, (x + 20, 463))
    d.text((x + 100, 474), "Overlay", font=font("Medium", 32), fill=TEXT)

    # Modes
    y = 590
    for i, mode in enumerate(("Toggle", "Push-to-talk", "Push-to-mute")):
        tx = 486 + i * 270
        d.rounded_rectangle((tx, y, tx + 250, y + 64), radius=32, fill=(0x1F, 0x36, 0x34, 255) if i == 0 else RAISED + (255,),
                            outline=(TEAL if i == 0 else BORDER) + (255,), width=2)
        tw = d.textlength(mode, font=font("Medium", 26))
        d.text((tx + (250 - tw) / 2, y + 16), mode, font=font("Medium", 26), fill=TEAL if i == 0 else TEXT)

    for i, c in enumerate((BG, RAISED, TEAL, RED)):
        sx = 486 + i * 70
        d.rounded_rectangle((sx, 700, sx + 54, 754), radius=12, fill=c + (255,), outline=BORDER + (255,), width=2)
    d.text((486 + 4 * 70 + 20, 712), "MIT · .NET 10 · Windows 10 / 11", font=font("Regular", 28), fill=TEXT2)

    img.convert("RGB").save(PREVIEW / "preview-collage.png", optimize=True)


def main() -> None:
    logo_mask = mask_of(square(Image.open(ROOT / "assets" / "logo-source.png")))
    glyph_mask = mask_of(square(Image.open(ROOT / "assets" / "glyph-source.png")))
    build_app_images(logo_mask, glyph_mask)
    build_menu_images()
    build_overlay_masks()
    build_sounds()
    build_wizard(logo_mask)
    build_collage(logo_mask, glyph_mask)
    print("assets written")


if __name__ == "__main__":
    main()
