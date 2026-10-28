# -*- coding: utf-8 -*-
"""Turn the whale-maid illustration into a multi-resolution launcher icon.

  1. flood-fill the white paper background so the character gets a real alpha
  2. compose her on a rounded navy card (gradient + soft glow + rim light)
  3. render every icon size from that 1024 master (small sizes zoom to the face)
  4. write app.ico by hand (PNG for 256, 32bpp BMP for the rest)
"""
import io
import struct
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageEnhance, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
ART = ROOT / "Assets" / "art.png"
ICO = ROOT / "Assets" / "app.ico"
LOGO = ROOT / "Assets" / "logo.png"
PREVIEW = ROOT / "tools" / "preview"

CARD_TOP = (27, 40, 88)      # #1B2858  navy from the artwork
CARD_BOT = (10, 16, 40)      # #0A1028
GLOW = (108, 139, 242)       # #6C8BF2


def character_rgba() -> Image.Image:
    im = Image.open(ART).convert("RGB")
    a = np.asarray(im).astype(np.int16)
    dist = np.abs(255 - a).max(axis=2)
    key = Image.fromarray(np.where(dist <= 26, 255, 0).astype(np.uint8)).copy()
    w, h = im.size
    for seed in ((0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)):
        ImageDraw.floodfill(key, seed, 128, thresh=0)
    bg = np.asarray(key) == 128
    alpha = np.where(bg, 0, 255).astype(np.uint8)
    out = Image.fromarray(np.dstack([np.asarray(im), alpha]), "RGBA")
    return out.crop(out.getbbox())


def card(size: int) -> Image.Image:
    ss = 4
    s = size * ss
    grad = Image.new("RGB", (1, s))
    for y in range(s):
        t = (y / max(1, s - 1)) ** 0.9
        grad.putpixel((0, y), tuple(
            int(CARD_TOP[i] + (CARD_BOT[i] - CARD_TOP[i]) * t) for i in range(3)))
    img = grad.resize((s, s)).convert("RGBA")

    glow = Image.new("L", (s, s), 0)
    gd = ImageDraw.Draw(glow)
    cx, cy, r = int(s * 0.5), int(s * 0.40), int(s * 0.50)
    gd.ellipse([cx - r, cy - r, cx + r, cy + r], fill=52)
    glow = glow.filter(ImageFilter.GaussianBlur(s * 0.12))
    img = Image.composite(Image.new("RGBA", (s, s), GLOW + (255,)), img,
                          glow.point(lambda v: min(255, v * 3)))

    rim = Image.new("L", (s, s), 0)
    ImageDraw.Draw(rim).rounded_rectangle(
        [2 * ss, 2 * ss, s - 2 * ss, s - 2 * ss],
        radius=int(s * 0.22), outline=255, width=3 * ss)
    rim = rim.filter(ImageFilter.GaussianBlur(ss))
    img = Image.composite(Image.new("RGBA", (s, s), (255, 255, 255, 255)), img,
                          rim.point(lambda v: v // 12))
    return img.resize((size, size), Image.LANCZOS)


def compose(size: int, art: Image.Image) -> Image.Image:
    zoom = 1.0 if size >= 48 else 1.22
    base = card(size)
    pad = 0.06 * size
    k = (size - 2 * pad) / max(art.size) * zoom
    nw, nh = max(1, round(art.size[0] * k)), max(1, round(art.size[1] * k))
    small = art.resize((nw, nh), Image.LANCZOS)
    canvas = base.copy()
    cx, cy = size * 0.5, size * (0.53 if zoom > 1 else 0.5)
    canvas.paste(small, (round(cx - nw / 2), round(cy - nh / 2)), small)
    mask = Image.new("L", base.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        [0, 0, size - 1, size - 1], radius=int(size * 0.22), fill=255)
    out = Image.new("RGBA", base.size, (0, 0, 0, 0))
    out.paste(canvas, (0, 0), mask)
    if size <= 64:
        out = out.filter(ImageFilter.UnsharpMask(radius=0.6, percent=80, threshold=2))
    if size <= 48:
        out = ImageEnhance.Color(out).enhance(1.06)
    return out


def bmp_entry(img: Image.Image) -> bytes:
    s = img.size[0]
    px = np.asarray(img.convert("RGBA"))
    bgra = px[:, :, [2, 1, 0, 3]][::-1].tobytes()
    andmask = bytes(((s + 31) // 32 * 4) * s)
    hdr = struct.pack("<IiiHHIIiiII", 40, s, s * 2, 1, 32, 0,
                      len(bgra) + len(andmask), 0, 0, 0, 0)
    return hdr + bgra + andmask


def write_ico(images, path):
    blobs = []
    for img in images:
        if img.size[0] >= 256:
            buf = io.BytesIO()
            img.save(buf, format="PNG", optimize=True)
            blobs.append(buf.getvalue())
        else:
            blobs.append(bmp_entry(img))
    offset = 6 + 16 * len(images)
    out = struct.pack("<HHH", 0, 1, len(images))
    for img, blob in zip(images, blobs):
        s = img.size[0]
        out += struct.pack("<BBBBHHII", s % 256, s % 256, 0, 0, 1, 32, len(blob), offset)
        offset += len(blob)
    for blob in blobs:
        out += blob
    Path(path).write_bytes(out)


def main():
    art = character_rgba()
    print("character bbox", art.size)
    art.save(PREVIEW / "char.png")
    imgs = []
    for s in (256, 128, 96, 64, 48, 40, 32, 24, 20, 16):
        img = compose(s, art)
        img.save(PREVIEW / f"icon{s}.png")
        imgs.append(img)
        print(f"{s:3d}  opacity={np.asarray(img)[:, :, 3].mean() / 255:.2f}")
    write_ico(imgs, ICO)
    compose(256, art).save(LOGO)
    print("ico bytes", ICO.stat().st_size)


if __name__ == "__main__":
    PREVIEW.mkdir(parents=True, exist_ok=True)
    main()
