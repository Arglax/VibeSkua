# MoVibeSkua launcher icon: the user's gold MVS monogram on an AQW-style crest.
# Canvas = the 108dp adaptive-icon square; keep the crest inside the central 66dp safe circle.
# monogram.png is the gold monogram cut from the user's reference image. Then run export.py.
import math
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

S = 2048
C = S / 2
OUTLINE = (26, 13, 5)
HERE = os.path.dirname(os.path.abspath(__file__))

GOLD = [(0.0, (74, 46, 10)), (0.35, (150, 104, 30)), (0.6, (214, 164, 58)), (0.82, (247, 210, 110)), (1.0, (255, 246, 205))]
STEEL = [(0.0, (46, 52, 60)), (0.4, (112, 124, 136)), (0.7, (180, 190, 200)), (1.0, (242, 246, 250))]
CRIMSON = [(0.0, (58, 8, 10)), (0.5, (120, 20, 22)), (1.0, (178, 40, 36))]
LEATHER = [(0.0, (40, 22, 12)), (1.0, (92, 58, 34))]


def ramp(t, stops):
    t = np.clip(t, 0, 1)
    out = np.zeros(t.shape + (3,), np.float32)
    for (t0, c0), (t1, c1) in zip(stops, stops[1:]):
        k = np.clip((t - t0) / (t1 - t0), 0, 1)[..., None]
        seg = (t >= t0) & (t <= t1)
        val = np.array(c0, np.float32) * (1 - k) + np.array(c1, np.float32) * k
        out = np.where(seg[..., None], val, out)
    return out


def mask_of(size, draw):
    m = Image.new("L", size, 0)
    draw(ImageDraw.Draw(m))
    return m


def shade(mask, radius, light=(-0.6, -0.8)):
    """Lambert shading of the mask treated as a rounded height field (the bevel)."""
    h = np.asarray(mask.filter(ImageFilter.GaussianBlur(radius)), np.float32) / 255
    gy, gx = np.gradient(h)
    k = radius * 1.4
    nx, ny, nz = -gx * k, -gy * k, np.ones_like(h)
    n = np.sqrt(nx * nx + ny * ny + nz * nz)
    lx, ly = light
    lz = 0.9
    ln = math.sqrt(lx * lx + ly * ly + lz * lz)
    return (nx * lx + ny * ly + nz * lz) / (n * ln)


def layer(mask, rgb):
    a = np.asarray(mask, np.float32)
    return Image.fromarray(np.dstack([np.clip(rgb, 0, 255), a]).astype(np.uint8), "RGBA")


def grow(mask, px):
    """Round dilation by about px pixels."""
    return mask.filter(ImageFilter.GaussianBlur(px / 2)).point(lambda v: 255 if v > 8 else 0).filter(ImageFilter.GaussianBlur(1.5))


def outlined(img, px, color=OUTLINE):
    out = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ring = grow(img.getchannel("A"), px)
    out.alpha_composite(layer(ring, np.broadcast_to(np.array(color, np.float32), (img.size[1], img.size[0], 3))))
    out.alpha_composite(img)
    return out


def shadow(img, blur, offset, opacity):
    a = img.getchannel("A").filter(ImageFilter.GaussianBlur(blur)).point(lambda v: int(v * opacity))
    sh = Image.new("RGBA", img.size, (8, 4, 2, 0))
    sh.putalpha(a)
    out = Image.new("RGBA", img.size, (0, 0, 0, 0))
    out.alpha_composite(sh, offset)
    return out


# ---------------------------------------------------------------- background (separate adaptive layer)
def background():
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    r = np.sqrt((xx - C) ** 2 + (yy - C * 0.92) ** 2) / (S * 0.72)
    base = ramp(1 - r, [(0.0, (14, 8, 4)), (0.45, (38, 20, 10)), (1.0, (74, 40, 20))])
    rng = np.random.default_rng(7)
    grain = Image.fromarray((rng.random((S // 4, S // 4)) * 255).astype(np.uint8)).resize((S, S), Image.BICUBIC)
    grain = (np.asarray(grain.filter(ImageFilter.GaussianBlur(3)), np.float32) / 255 - 0.5) * 22
    fine = (rng.random((S, S)).astype(np.float32) - 0.5) * 10
    rgb = base + grain[..., None] + fine[..., None]
    # Faint purple magic behind the crest.
    glow = np.exp(-(((xx - C) ** 2 + (yy - C) ** 2) / (2 * (S * 0.2) ** 2)))[..., None]
    rgb = rgb + glow * np.array([46, 14, 58], np.float32)
    return Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8), "RGB").convert("RGBA")


# ---------------------------------------------------------------- sword, drawn tip-up on its own canvas
def sword(length):
    w, h = 420, length
    cx = w / 2
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    blade_top, guard_y = 40, h - 420
    blade = mask_of((w, h), lambda d: d.polygon(
        [(cx, blade_top), (cx + 54, blade_top + 170), (cx + 46, guard_y), (cx - 46, guard_y), (cx - 54, blade_top + 170)], fill=255))
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    # Diamond cross-section: the lit half and the shadowed half of the blade.
    t = np.where(xx < cx, 0.85, 0.42) + (shade(blade, 6) - 0.6) * 0.6
    t = t - np.clip((yy - blade_top) / (guard_y - blade_top), 0, 1) * 0.12
    steel = ramp(t, STEEL)
    fuller = (np.abs(xx - cx) < 7) & (yy > blade_top + 230) & (yy < guard_y - 40)
    steel[fuller] *= 0.55
    img.alpha_composite(layer(blade, steel))

    guard = mask_of((w, h), lambda d: d.rounded_rectangle((cx - 175, guard_y - 6, cx + 175, guard_y + 52), 26, fill=255))
    gem_guard = mask_of((w, h), lambda d: d.ellipse((cx - 30, guard_y - 8, cx + 30, guard_y + 52), fill=255))
    grip = mask_of((w, h), lambda d: d.rounded_rectangle((cx - 34, guard_y + 52, cx + 34, h - 120), 12, fill=255))
    pommel = mask_of((w, h), lambda d: d.ellipse((cx - 62, h - 150, cx + 62, h - 26), fill=255))
    gem = mask_of((w, h), lambda d: d.ellipse((cx - 28, h - 116, cx + 28, h - 60), fill=255))

    wraps = ((yy - guard_y) % 34 < 9).astype(np.float32)
    leather = ramp(0.3 + (shade(grip, 8) - 0.6) * 0.9 - wraps * 0.35, LEATHER)
    img.alpha_composite(outlined(layer(grip, leather), 10))
    for part in (guard, pommel):
        img.alpha_composite(outlined(layer(part, ramp(0.15 + shade(part, 14) * 0.85, GOLD)), 10))
    for part in (gem_guard, gem):
        img.alpha_composite(layer(part, ramp(0.1 + shade(part, 10) * 0.95, CRIMSON)))
    return outlined(img, 12)


# ---------------------------------------------------------------- heater shield
def shield_mask(cx, top, width, height, inset=0):
    w = width / 2 - inset
    t = top + inset
    bottom = top + height - inset * 1.25
    pts = [(cx - w + 40, t), (cx + w - 40, t), (cx + w, t + 40)]
    side = t + height * 0.40
    pts.append((cx + w, side))
    for i in range(1, 41):
        k = i / 40
        # Cubic from the side down to the point.
        p0, p1, p2, p3 = (cx + w, side), (cx + w, side + (bottom - side) * 0.55), (cx + w * 0.45, bottom - 40), (cx, bottom)
        x = (1 - k) ** 3 * p0[0] + 3 * (1 - k) ** 2 * k * p1[0] + 3 * (1 - k) * k * k * p2[0] + k ** 3 * p3[0]
        y = (1 - k) ** 3 * p0[1] + 3 * (1 - k) ** 2 * k * p1[1] + 3 * (1 - k) * k * k * p2[1] + k ** 3 * p3[1]
        pts.append((x, y))
    left = [(2 * cx - x, y) for (x, y) in reversed(pts[2:])]
    pts = pts[:2] + pts[2:] + left
    pts.append((cx - w, t + 40))
    return mask_of((S, S), lambda d: d.polygon(pts, fill=255))


def shield(cx, top, width, height):
    out = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    outer = shield_mask(cx, top, width, height)
    inner = shield_mask(cx, top, width, height, inset=58)
    rim_rgb = ramp(0.12 + shade(outer, 16) * 0.88, GOLD)
    out.alpha_composite(layer(outer, rim_rgb))
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    r = np.sqrt((xx - cx) ** 2 + (yy - (top + height * 0.38)) ** 2) / (width * 0.75)
    field = ramp(1 - r, CRIMSON)
    # Inner shadow along the rim, so the field sits below it.
    edge = np.asarray(inner.filter(ImageFilter.GaussianBlur(26)), np.float32) / 255
    field = field * (0.45 + 0.55 * edge[..., None])
    # Bevel ridge between rim and field.
    out.alpha_composite(layer(grow(inner, 6), np.broadcast_to(np.array(OUTLINE, np.float32), (S, S, 3))))
    out.alpha_composite(layer(inner, field))
    # Rivets on the rim.
    w = width / 2 - 29
    rivets = [(cx - w + 60, top + 29), (cx, top + 29), (cx + w - 60, top + 29),
              (cx - w, top + height * 0.36), (cx + w, top + height * 0.36)]
    for (x, y) in rivets:
        m = mask_of((S, S), lambda d: d.ellipse((x - 17, y - 17, x + 17, y + 17), fill=255))
        out.alpha_composite(layer(m, ramp(0.05 + shade(m, 5) * 1.0, GOLD)))
    return outlined(out, 16)


def build():
    fg = Image.new("RGBA", (S, S), (0, 0, 0, 0))

    # Crossed swords, tips up, hilts down, behind the shield.
    blade = sword(1500)
    for angle, dx in ((38, -1), (-38, 1)):
        rot = blade.rotate(angle, resample=Image.BICUBIC, expand=True)
        x = int(C - rot.width / 2 + dx * 40)
        y = int(C - rot.height / 2 - 30)
        fg.alpha_composite(shadow(rot, 18, (0, 22), 0.55).crop((0, 0, rot.width, rot.height)), (x, y))
        fg.alpha_composite(rot, (x, y))

    width, height = 880, 1010
    top = int(C - height / 2 + 10)
    sh = shield(C, top, width, height)
    fg.alpha_composite(shadow(sh, 30, (0, 30), 0.7))
    fg.alpha_composite(sh)

    mono = Image.open(os.path.join(HERE, "monogram.png")).convert("RGBA")
    target_w = 660
    mono = mono.resize((target_w, round(mono.height * target_w / mono.width)), Image.LANCZOS)
    pad = 60
    canvas = Image.new("RGBA", (mono.width + pad * 2, mono.height + pad * 2), (0, 0, 0, 0))
    canvas.alpha_composite(mono, (pad, pad))
    canvas = outlined(canvas, 14)
    mx = int(C - canvas.width / 2 + 8)
    my = int(top + height * 0.45 - canvas.height / 2)
    fg.alpha_composite(shadow(canvas, 12, (0, 14), 0.8), (mx, my))
    fg.alpha_composite(canvas, (mx, my))
    return fg


if __name__ == "__main__":
    bg = background()
    fg = build()
    bg.save(os.path.join(HERE, "bg_2048.png"))
    fg.save(os.path.join(HERE, "fg_2048.png"))
    full = bg.copy()
    full.alpha_composite(fg)
    full.save(os.path.join(HERE, "full_2048.png"))
    print("done")
