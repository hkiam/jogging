#!/usr/bin/env python3
# Builds the app icons from the artwork in Assets/Art/Icon/Source~/Vorlage.png (a rounded tile on white):
#   AppIcon.png            1024 px square, full bleed, no alpha — iPad (iOS rounds it itself), Android
#                          adaptive background, default for every platform
#   AppIconMac.png         1024 px, the rounded tile on transparent, 824 px wide like Apple's macOS grid
#   AppIconForeground.png  transparent Android adaptive foreground (the motif is the background layer)
# Usage: python3 Tools/make-icon.py   (needs Pillow)
import os
from PIL import Image, ImageDraw, ImageFilter, ImageChops

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Art", "Icon")
src = Image.open(os.path.join(ROOT, "Source~", "Vorlage.png")).convert("RGB")
W, H = src.size
px = src.load()

def white(p): return min(p) > 238 and max(p) - min(p) < 14

# the tile's bounding box, measured through the centre
cx, cy = W // 2, H // 2
left = next(x for x in range(W) if not white(px[x, cy]))
right = next(x for x in range(W - 1, -1, -1) if not white(px[x, cy]))
top = next(y for y in range(H) if not white(px[cx, y]))
bottom = next(y for y in range(H - 1, -1, -1) if not white(px[cx, y]))

# background = the white connected to the image corners (flood fill on a threshold image)
thr = Image.new("L", (W, H), 0)
tp = thr.load()
for y in range(H):
    for x in range(W):
        if white(px[x, y]): tp[x, y] = 255
for corner in [(0, 0), (W - 1, 0), (0, H - 1), (W - 1, H - 1)]:
    ImageDraw.floodfill(thr, corner, 128)
bg = thr.point(lambda v: 255 if v == 128 else 0)          # 255 = outside the tile
tile_alpha = ImageChops.invert(bg).filter(ImageFilter.GaussianBlur(1.2))

# --- macOS: the rounded tile on transparent, 824/1024 like Apple's icon grid ---
tile = src.convert("RGBA"); tile.putalpha(tile_alpha)
tile = tile.crop((left, top, right + 1, bottom + 1))
mac = Image.new("RGBA", (1024, 1024), (0, 0, 0, 0))
t = tile.resize((824, round(824 * tile.height / tile.width)), Image.LANCZOS)
mac.paste(t, ((1024 - t.width) // 2, (1024 - t.height) // 2), t)
mac.save(os.path.join(ROOT, "AppIconMac.png"))

# --- full bleed: crop inside the glossy rim, fill the white corners from the neighbouring picture ---
rim = 14
box = (left + rim, top + rim, right + 1 - rim, bottom + 1 - rim)
sq = src.crop(box)
# corners plus the tile's glossy rim (~18 px inside its edge), which would show as a light line
hole = bg.crop(box).filter(ImageFilter.GaussianBlur(9)).point(lambda v: 255 if v > 6 else 0)
keep = ImageChops.invert(hole)
# normalized blur: average of the kept pixels around each hole pixel, widened until every hole is covered
filled = sq.copy()
for radius in (12, 30, 70):
    num = Image.composite(sq, Image.new("RGB", sq.size, 0), keep).filter(ImageFilter.GaussianBlur(radius))
    den = keep.filter(ImageFilter.GaussianBlur(radius))
    n, dd = num.load(), den.load()
    f = filled.load(); h = hole.load()
    for y in range(sq.height):
        for x in range(sq.width):
            if h[x, y] and dd[x, y] > 8:
                r_, g_, b_ = n[x, y]; k = 255.0 / dd[x, y]
                f[x, y] = (min(255, int(r_ * k)), min(255, int(g_ * k)), min(255, int(b_ * k)))
    keep = ImageChops.lighter(keep, den.point(lambda v: 255 if v > 8 else 0))
    sq = filled.copy()
full = filled.resize((1024, 1024), Image.LANCZOS)
full.save(os.path.join(ROOT, "AppIcon.png"))

Image.new("RGBA", (1024, 1024), (0, 0, 0, 0)).save(os.path.join(ROOT, "AppIconForeground.png"))
print(f"Kachel {right - left + 1}×{bottom - top + 1} px → AppIcon.png, AppIconMac.png, AppIconForeground.png")
