#!/usr/bin/env python3
"""Generate the original 640x360 Rimworld Statistics Workshop preview.

Uses Pillow and the system DejaVu fonts at build time. Font files are not distributed.
No copyrighted RimWorld artwork, screenshots, or third-party assets are embedded.
"""
import argparse
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

W, H = 640, 360
FONT = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
FONT_BOLD = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()

    im = Image.new("RGB", (W, H), "#121c27")
    px = im.load()
    for y in range(H):
        for x in range(W):
            t = (x / W) * 0.32 + (y / H) * 0.68
            px[x, y] = (int(17 + 9*t), int(27 + 13*t), int(37 + 15*t))
    d = ImageDraw.Draw(im)

    for x in range(0, 650, 32):
        d.line((x, 0, x, H), fill=(30, 43, 54), width=1)
    for y in range(0, 365, 32):
        d.line((0, y, W, y), fill=(30, 43, 54), width=1)

    d.rectangle((0, 0, 12, H), fill=(180, 127, 69))
    d.rectangle((25, 24, 615, 336), outline=(87, 107, 118), width=2)
    d.rectangle((24, 24, 615, 30), fill=(180, 127, 69))
    d.rounded_rectangle((380, 69, 598, 295), 20, fill=(29, 44, 56),
                        outline=(76, 104, 113), width=2)
    for y in (142, 217):
        d.line((401, y, 575, y), fill=(61, 83, 95), width=2)
    for y in (106, 181, 256):
        d.rounded_rectangle((402, y-16, 436, y+18), 7,
                            fill=(56, 92, 96), outline=(91, 147, 141), width=2)

    mono = (218, 231, 221)
    d.line((412, 91, 412, 121), fill=mono, width=3)
    d.line((421, 91, 421, 121), fill=mono, width=3)
    d.line((407, 114, 425, 96), fill=(210, 163, 101), width=3)
    d.polygon([(406, 182), (418, 171), (431, 182)], fill=(207, 216, 207))
    d.rectangle((409, 182, 427, 196), outline=(207, 216, 207), width=2)
    d.line((412, 243, 412, 271), fill=mono, width=3)
    d.polygon([(412, 244), (432, 248), (412, 256)], fill=(204, 144, 81))

    def txt(x, y, value, size, fill, heavy=False):
        font = ImageFont.truetype(FONT_BOLD if heavy else FONT, size)
        d.text((x, y), value, font=font, fill=fill)

    txt(49, 57, "RIMWORLD", 19, (192, 156, 109), True)
    txt(46, 96, "STATISTICS", 43, (238, 240, 228), True)
    d.line((50, 165, 330, 165), fill=(187, 134, 72), width=4)
    txt(49, 190, "EVERY COLONY.", 19, (193, 215, 205), True)
    txt(49, 221, "ONE HISTORY.", 19, (193, 215, 205), True)
    txt(49, 292, "RIMWORLD 1.7", 13, (154, 170, 172))
    txt(448, 93, "TOTAL DAYS", 16, (238, 240, 228), True)
    txt(448, 119, "Lifetime", 12, (153, 180, 178))
    txt(448, 168, "COLONIES", 16, (238, 240, 228), True)
    txt(448, 195, "All saves", 12, (153, 180, 178))
    txt(448, 244, "ENDINGS", 16, (238, 240, 228), True)
    txt(448, 271, "Recorded", 12, (153, 180, 178))

    im = im.quantize(colors=120, method=Image.Quantize.FASTOCTREE)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    im.save(args.out, "PNG", optimize=True)
    if args.out.stat().st_size >= 1_000_000:
        raise RuntimeError("Steam Workshop preview exceeds 1MB")
    print(f"PREVIEW_OK 640x360 {args.out.stat().st_size} bytes {args.out}")


if __name__ == "__main__":
    main()
