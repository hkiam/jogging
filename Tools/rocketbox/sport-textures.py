#!/usr/bin/env python3
# Running clothes for the Rocketbox figures (MIT): recolours the clothing areas of a body texture –
# jeans become tights, printed or striped shirts plain running shirts – and keeps the big folds (blurred
# brightness of the original), skin and shoes. Writes <code>_body_sport.png next to the original;
# World/RealFigure gives runners the sport material, spectators keep their everyday clothes.
#   python3 Tools/rocketbox/sport-textures.py [--preview out.png]
# Regions are fractions of the texture (x0, y0, x1, y1), per figure (the UV layouts differ by gender).
import glob, os, sys
import numpy as np
from PIL import Image, ImageFilter

ROOT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "Rocketbox", "Avatars")
F_LEGS = [(0.0, 0.53, 0.32, 1.0), (0.68, 0.53, 1.0, 1.0)]
F_TOP = [(0.28, 0.0, 0.72, 0.78)]
M_LEGS = [(0.0, 0.0, 0.31, 0.42), (0.69, 0.0, 1.0, 0.42)]
M_SLEEVES = [(0.0, 0.43, 0.31, 0.62), (0.69, 0.43, 1.0, 0.62)]
M_TOP = [(0.31, 0.0, 0.69, 0.86)]
BLACK, NAVY = (0.07, 0.07, 0.085), (0.09, 0.11, 0.19)

# the shirt of every figure (mask for the shirt colours you unlock by level; World/RealFigure)
F_SLEEVES_LONG = [(0.0, 0.27, 0.30, 0.50), (0.70, 0.27, 1.0, 0.50)]
M_SLEEVES_LONG = [(0.0, 0.43, 0.31, 0.83), (0.69, 0.43, 1.0, 0.83)]
SHIRTS = {
    "Female_Adult_03": [(0.28, 0.0, 0.72, 0.25), (0.28, 0.48, 0.72, 0.80)],
    "Female_Adult_08": F_TOP, "Female_Adult_17": [(0.28, 0.0, 0.72, 0.84)],
    "Female_Adult_12": [(0.28, 0.0, 0.72, 1.0)] + F_SLEEVES_LONG,
    "Sports_Female_02": F_TOP,
    "Male_Adult_01": M_TOP + M_SLEEVES, "Male_Adult_09": M_TOP + M_SLEEVES, "Male_Adult_16": M_TOP + M_SLEEVES,
    "Male_Adult_10": M_TOP + M_SLEEVES_LONG, "Male_Adult_17": M_TOP + M_SLEEVES_LONG, "Male_Adult_18": M_TOP + M_SLEEVES_LONG,
    "Sports_Male_02": [(0.26, 0.0, 0.74, 0.86), (0.30, 0.86, 0.70, 0.97)], "Sports_Male_03": [(0.26, 0.0, 0.74, 0.86), (0.30, 0.86, 0.70, 0.97)],
    "Sports_Male_04": M_TOP,
}
SHIRT_OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "Rocketbox", "Resources", "Shirt")

# figure → [(regions, colour)]; figures not listed keep their clothes (already sporty)
RECIPES = {
    "Female_Adult_08": [(F_TOP, (0.90, 0.36, 0.30)), (F_LEGS, BLACK)],                 # coral shirt, tights
    "Female_Adult_12": [([(0.0, 0.50, 0.32, 0.76), (0.68, 0.50, 1.0, 0.76)], BLACK)],  # jeans shorts → running shorts
    "Female_Adult_17": [(F_LEGS, NAVY)],                                               # green tee stays
    "Male_Adult_01": [(M_TOP + M_SLEEVES, (0.08, 0.52, 0.58))],                        # striped shirt → teal; shorts stay
    "Male_Adult_09": [(M_LEGS, BLACK), (M_TOP + M_SLEEVES, (0.93, 0.45, 0.10))],      # orange shirt, tights
    "Male_Adult_16": [(M_LEGS, BLACK), (M_TOP + M_SLEEVES, (0.18, 0.34, 0.66))],      # plain blue shirt (no print)
    "Male_Adult_17": [(M_LEGS, BLACK)],                                                # track jacket stays
    "Male_Adult_18": [(M_TOP + [(0.0, 0.43, 0.31, 0.83), (0.69, 0.43, 1.0, 0.83)], (0.72, 0.12, 0.13))],  # grey hoodie → red
    # football kits → running kit: plain shirt, white socks
    "Sports_Male_02": [([(0.26, 0.0, 0.74, 0.86), (0.30, 0.86, 0.70, 0.97)], (0.10, 0.30, 0.62)), ([(0.0, 0.31, 0.31, 0.56), (0.69, 0.31, 1.0, 0.56)], (0.92, 0.92, 0.92))],
    "Sports_Male_03": [([(0.26, 0.0, 0.74, 0.86), (0.30, 0.86, 0.70, 0.97)], (0.85, 0.80, 0.15)), ([(0.0, 0.31, 0.31, 0.56), (0.69, 0.31, 1.0, 0.56)], (0.92, 0.92, 0.92))],
}


def skin(a):
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    mx, mn = a.max(-1), a.min(-1)
    s = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    return (r > g) & (g > b) & (r - b > 0.12) & (s > 0.18) & (s < 0.68) & (mx > 0.32)


def recolour(path, recipe):
    img = Image.open(path).convert("RGB")
    a = np.asarray(img).astype(np.float32) / 255
    h, w = a.shape[:2]
    lum = 0.3 * a[..., 0] + 0.59 * a[..., 1] + 0.11 * a[..., 2]
    soft = np.asarray(Image.fromarray((lum * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(w / 28))).astype(np.float32) / 255
    out = a.copy()
    keep = skin(a) | (a.max(-1) < 0.03)  # skin and the empty background stay
    for regions, colour in recipe:
        mask = np.zeros((h, w), bool)
        for x0, y0, x1, y1 in regions:
            mask[int(y0 * h):int(y1 * h), int(x0 * w):int(x1 * w)] = True
        mask &= ~keep
        if not mask.any():
            continue
        shade = np.clip(soft / max(1e-3, soft[mask].mean()), 0.82, 1.18)       # big folds, no print or wash
        fine = 1.0 + 0.04 * np.tanh((lum / np.maximum(soft, 1e-3) - 1.0) * 3.0)  # a little fabric grain, no print
        c = np.array(colour, np.float32)
        out[mask] = np.clip(c[None, :] * (shade * fine)[mask][:, None], 0, 1)
    return Image.fromarray((out * 255).astype(np.uint8)), img


def shirt_mask(path, regions, original):
    """R = shirt, G = its shading (0.5 + G = brightness relative to the shirt's mean) – 512 px is plenty.
    Skin is found on the original texture (a coral running shirt would look like skin)."""
    a = np.asarray(Image.open(path).convert("RGB")).astype(np.float32) / 255
    orig = np.asarray(Image.open(original).convert("RGB")).astype(np.float32) / 255
    h, w = a.shape[:2]
    lum = 0.3 * a[..., 0] + 0.59 * a[..., 1] + 0.11 * a[..., 2]
    soft = np.asarray(Image.fromarray((lum * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(w / 28))).astype(np.float32) / 255
    mask = np.zeros((h, w), bool)
    for x0, y0, x1, y1 in regions:
        mask[int(y0 * h):int(y1 * h), int(x0 * w):int(x1 * w)] = True
    mask &= ~(skin(orig) | (orig.max(-1) < 0.03))
    out = np.zeros((h, w, 3), np.float32)
    if mask.any():
        shade = np.clip(soft / max(1e-3, soft[mask].mean()), 0.82, 1.18) * (1.0 + 0.04 * np.tanh((lum / np.maximum(soft, 1e-3) - 1.0) * 3.0))
        out[..., 0] = mask
        out[..., 1] = np.where(mask, np.clip(shade - 0.5, 0, 1), 0)
    return Image.fromarray((out * 255).astype(np.uint8)).resize((512, 512), Image.BILINEAR)


def main():
    preview = sys.argv[sys.argv.index("--preview") + 1] if "--preview" in sys.argv else None
    tiles = []
    for name, recipe in RECIPES.items():
        found = glob.glob(os.path.join(ROOT, name, "Textures", "*_body_color.png"))
        if not found:
            print("fehlt:", name); continue
        src = found[0]
        new, old = recolour(src, recipe)
        dst = src.replace("_body_color.png", "_body_sport.png")
        new.save(dst)
        print(name, "→", os.path.basename(dst))
        tiles.append(np.hstack([np.asarray(old.resize((256, 256))), np.asarray(new.resize((256, 256)))]))
    os.makedirs(SHIRT_OUT, exist_ok=True)
    for name, regions in SHIRTS.items():
        tex = glob.glob(os.path.join(ROOT, name, "Textures", "*_body_sport.png")) or glob.glob(os.path.join(ROOT, name, "Textures", "*_body_color.png"))
        if not tex:
            print("fehlt:", name); continue
        code = os.path.basename(tex[0]).split("_body_")[0]
        original = os.path.join(os.path.dirname(tex[0]), code + "_body_color.png")
        shirt_mask(tex[0], regions, original).save(os.path.join(SHIRT_OUT, code + "_body.png"))
    print("Shirt-Masken:", len(SHIRTS))
    if preview and tiles:
        rows = [np.hstack(tiles[i:i + 2]) if i + 1 < len(tiles) else np.hstack([tiles[i], np.zeros_like(tiles[i])]) for i in range(0, len(tiles), 2)]
        Image.fromarray(np.vstack(rows)).save(preview)


if __name__ == "__main__":
    main()
