#!/usr/bin/env python3
"""LoupixDeck LibreHardwareMonitor plugin icon (processor package seen from above, matte, deep green).

Same shading as the other plugin icons. The subject nods to the LibreHardwareMonitor icon without
copying it: a dark package with a brushed metal heat spreader and gold markings, redrawn in the
family's soft, rounded style on a deep green tile.

Requires: pip install pillow numpy
Usage:    python make_icon.py [output_dir]
Writes icon_{256,128,64,32,16}.png (RGBA, transparent corners).
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SIZE = 256   # design size (px)
SS = 4       # supersampling
N = SIZE * SS
YY, XX = np.mgrid[0:N, 0:N].astype(np.float32)
XX = (XX + 0.5) / SS
YY = (YY + 0.5) / SS


def oklch(L, C, h, a=1.0):
    hr = math.radians(h)
    A, B = C * math.cos(hr), C * math.sin(hr)
    l = (L + 0.3963377774 * A + 0.2158037573 * B) ** 3
    m = (L - 0.1055613458 * A - 0.0638541728 * B) ** 3
    s = (L - 0.0894841775 * A - 1.2914855480 * B) ** 3
    lin = [4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
           -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
           -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s]
    out = [12.92 * c if c <= 0.0031308 else 1.055 * max(c, 0) ** (1 / 2.4) - 0.055 for c in lin]
    return (*[min(max(c, 0.0), 1.0) for c in out], a)


# Colors: a deep green tile sets this icon apart from the night blue and graphite ones
BG_HUE = 165
BG_TOP = oklch(0.31, 0.035, BG_HUE)
BG_BOTTOM = oklch(0.20, 0.03, BG_HUE)
EDGE = oklch(0.40, 0.035, BG_HUE)
PACKAGE = oklch(0.20, 0.01, 250)
GOLD = oklch(0.80, 0.12, 85)
GOLD_DARK = oklch(0.62, 0.11, 75)

# ---------- Masks ----------
def _mask(draw_fn):
    im = Image.new("L", (N, N), 0)
    draw_fn(ImageDraw.Draw(im))
    return np.asarray(im, dtype=np.float32) / 255.0


def circle(cx, cy, r):
    return _mask(lambda d: d.ellipse([(cx - r) * SS, (cy - r) * SS, (cx + r) * SS - 1, (cy + r) * SS - 1], fill=255))


def rrect(x, y, w, h, r):
    return _mask(lambda d: d.rounded_rectangle([x * SS, y * SS, (x + w) * SS - 1, (y + h) * SS - 1], radius=r * SS, fill=255))


def polygon(points):
    return _mask(lambda d: d.polygon([(x * SS, y * SS) for x, y in points], fill=255))


def blur(mask, px):
    if px <= 0:
        return mask
    im = Image.fromarray((np.clip(mask, 0, 1) * 255).astype(np.uint8))
    im = im.filter(ImageFilter.GaussianBlur(px / 2 * SS))  # CSS blur = 2*sigma
    return np.asarray(im, dtype=np.float32) / 255.0


def shift(mask, dx, dy, fill=0.0):
    out = np.full_like(mask, fill)
    sx, sy = int(round(dx * SS)), int(round(dy * SS))
    h, w = mask.shape
    out[max(sy, 0):h + min(sy, 0), max(sx, 0):w + min(sx, 0)] = mask[max(-sy, 0):h + min(-sy, 0), max(-sx, 0):w + min(-sx, 0)]
    return out


# ---------- Compositing ----------
canvas = np.zeros((N, N, 4), dtype=np.float32)  # straight RGBA


def paint(color, alpha):
    """color: RGBA tuple or HxWx3 array; alpha: HxW mask (multiplied by the color's alpha)."""
    global canvas
    if isinstance(color, tuple):
        rgb = np.array(color[:3], dtype=np.float32)[None, None, :]
        a = alpha * color[3]
    else:
        rgb, a = color, alpha
    a = a[..., None]
    ca = canvas[..., 3:4]
    oa = a + ca * (1 - a)
    orgb = (rgb * a + canvas[..., :3] * ca * (1 - a)) / np.maximum(oa, 1e-6)
    canvas = np.concatenate([orgb, oa], axis=-1)


def drop_shadow(shape, dx, dy, blur_px, color, clip):
    paint(color, blur(shift(shape, dx, dy), blur_px) * clip)


def inset_shadow(shape, dx, dy, blur_px, color):
    paint(color, blur(shift(1 - shape, dx, dy, fill=1.0), blur_px) * shape)


def linear_gradient(box, css_deg, stops):
    x, y, w, h = box
    th = math.radians(css_deg)
    dx, dy = math.sin(th), -math.cos(th)
    L = abs(w * dx) + abs(h * dy)
    t = ((XX - (x + w / 2)) * dx + (YY - (y + h / 2)) * dy) / L + 0.5
    t = np.clip(t, 0, 1)
    pos = [s[0] for s in stops]
    return np.stack([np.interp(t, pos, [s[1][i] for s in stops]) for i in range(3)], axis=-1).astype(np.float32)


# ---------- Draw ----------
C = 128
icon = rrect(0, 0, SIZE, SIZE, 58)

# Background: vertical gradient, soft light from the top, 1px inner edge
paint(linear_gradient((0, 0, SIZE, SIZE), 180, [(0, BG_TOP[:3]), (1, BG_BOTTOM[:3])]), icon)
t = np.clip(np.hypot((XX - C) / 1.4, YY + 30) / 190, 0, 1)
paint((1, 1, 1, 1.0), icon * (0.07 * (1 - t)))
paint(EDGE, icon - rrect(1, 1, SIZE - 2, SIZE - 2, 57))

# Package: dark substrate with a soft top edge
PS, PR = 172, 26
P0 = C - PS / 2
package = rrect(P0, P0, PS, PS, PR)
drop_shadow(package, 0, 16, 24, oklch(0.04, 0.04, 200, 0.80), icon)
drop_shadow(package, 0, 4, 3, oklch(0.06, 0.03, 200, 0.55), icon)
paint(linear_gradient((P0, P0, PS, PS), 160, [(0, oklch(0.26, 0.012, 250)[:3]), (1, PACKAGE[:3])]), package)
inset_shadow(package, 0, -3, 4, oklch(0.05, 0.02, 250, 0.45))
inset_shadow(package, 0, 2, 2, (1, 1, 1, 0.16))

# Gold markings: pin-one triangle, a row of contacts, two code lines
gold = np.zeros((N, N), dtype=np.float32)
gold = np.maximum(gold, polygon([(P0 + 18, P0 + PS - 18), (P0 + 18, P0 + PS - 40), (P0 + 40, P0 + PS - 18)]))
for k in range(4):
    gold = np.maximum(gold, rrect(P0 + PS - 22, P0 + 30 + k * 12, 8, 7, 2))
for k, w in enumerate((44, 30)):
    gold = np.maximum(gold, rrect(P0 + PS - 22 - w, P0 + PS - 32 + k * 10, w, 5, 2.5))
paint(linear_gradient((P0, P0, PS, PS), 160, [(0, GOLD[:3]), (1, GOLD_DARK[:3])]), gold)
inset_shadow(gold, 0, 1, 1, (1, 1, 1, 0.35))

# Heat spreader: brushed metal plate with a bevel
HS, HR = 104, 16
H0 = C - HS / 2 - 4
hs = rrect(H0, H0, HS, HS, HR)
drop_shadow(hs, 0, 6, 8, oklch(0.02, 0.02, 250, 0.75), package)
paint(linear_gradient((H0, H0, HS, HS), 150, [(0, oklch(0.92, 0.005, 250)[:3]), (0.55, oklch(0.78, 0.008, 250)[:3]), (1, oklch(0.66, 0.01, 250)[:3])]), hs)
rng = np.random.default_rng(7)
streaks = np.repeat(rng.normal(0, 1, (N, 1)), N, axis=1).astype(np.float32)
streaks = blur(np.clip(streaks * 0.5 + 0.5, 0, 1), 0.4)
paint((1, 1, 1, 1.0), hs * np.clip(streaks - 0.5, 0, 1) * 0.22)
paint((0, 0, 0, 1.0), hs * np.clip(0.5 - streaks, 0, 1) * 0.14)
hx, hy = H0 + HS * 0.30, H0 + HS * 0.22
t = np.clip(np.hypot(XX - hx, YY - hy) / (HS * 0.75), 0, 1)
paint((1, 1, 1, 1.0), hs * (0.30 * (1 - t) ** 2))
inset_shadow(hs, 0, -3, 4, oklch(0.35, 0.01, 250, 0.40))
inset_shadow(hs, 0, 2, 2, (1, 1, 1, 0.60))

# Clip to the icon shape
canvas[..., 3] *= icon

# ---------- Export ----------
if __name__ == "__main__":
    out_dir = sys.argv[1] if len(sys.argv) > 1 else "."
    os.makedirs(out_dir, exist_ok=True)
    big = Image.fromarray((np.clip(canvas, 0, 1) * 255 + 0.5).astype(np.uint8), "RGBA")
    for s in (256, 128, 64, 32, 16):
        path = os.path.join(out_dir, f"icon_{s}.png")
        big.resize((s, s), Image.LANCZOS).save(path)
        print("written:", path)
