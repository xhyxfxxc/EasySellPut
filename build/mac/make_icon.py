#!/usr/bin/env python3
"""Mac app icon: redraws the Windows icon (blue tile, rising candles) on Apple's icon grid
(824 px tile in a 1024 px canvas, ~22% corner radius) and writes AppIcon.icns with every standard size."""
import sys
from PIL import Image, ImageDraw

SS = 4                      # supersampling for smooth edges
CANVAS, TILE = 1024, 824
TOP, BOTTOM = (20, 109, 218), (29, 74, 166)
WHITE, RED, GREEN = (255, 255, 255, 255), (255, 90, 90, 255), (80, 220, 140, 255)
# Candles in the 256 px source design: centre x, wick top/bottom, body top/bottom, colour.
CANDLES = [(61, 141, 191, 157, 175, RED), (107, 90, 178, 111, 150, GREEN),
           (153, 64, 140, 80, 119, GREEN), (199, 46, 114, 59, 88, GREEN)]


def draw(size_px):
    n = CANVAS * SS
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    off, tile = (CANVAS - TILE) // 2 * SS, TILE * SS
    grad = Image.new("RGBA", (tile, tile))
    gd = ImageDraw.Draw(grad)
    for y in range(tile):
        t = y / (tile - 1)
        gd.line([(0, y), (tile, y)], fill=tuple(round(a + (b - a) * t) for a, b in zip(TOP, BOTTOM)) + (255,))
    mask = Image.new("L", (tile, tile), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, tile - 1, tile - 1], radius=round(tile * 0.2237), fill=255)
    img.paste(grad, (off, off), mask)
    d = ImageDraw.Draw(img)
    k = tile / 256
    p = lambda v: off + v * k
    for cx, wt, wb, bt, bb, colour in CANDLES:
        d.rounded_rectangle([p(cx - 2.2), p(wt), p(cx + 2.2), p(wb)], radius=2.2 * k, fill=WHITE)
        d.rounded_rectangle([p(cx - 15), p(bt - 3), p(cx + 15), p(bb + 3)], radius=4 * k, fill=WHITE)
        d.rounded_rectangle([p(cx - 12), p(bt), p(cx + 12), p(bb)], radius=2.5 * k, fill=colour)
    return img.resize((size_px, size_px), Image.LANCZOS)


def main(out):
    big = draw(1024)
    sizes = [(16, 16), (32, 32), (64, 64), (128, 128), (256, 256), (512, 512), (1024, 1024)]
    big.save(out, format="ICNS", sizes=sizes)
    big.resize((256, 256), Image.LANCZOS).save(out.replace(".icns", "-preview.png"))


if __name__ == "__main__":
    main(sys.argv[1])
