# Writes the launcher, notification and in-app images into the MoVibeSkua.Android project.
# Run design.py first; it renders the 2048px layers this cuts up.
import os
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
APP = os.path.join(HERE, "..", "..", "MoVibeSkua.Android")
RES = os.path.join(APP, "Resources")
DENSITIES = {"mdpi": 1, "hdpi": 1.5, "xhdpi": 2, "xxhdpi": 3, "xxxhdpi": 4}
S = 2048

fg = Image.open(os.path.join(HERE, "fg_2048.png")).convert("RGBA")
bg = Image.open(os.path.join(HERE, "bg_2048.png")).convert("RGBA")
mono = Image.open(os.path.join(HERE, "monogram.png")).convert("RGBA")


def white_silhouette(src, canvas, fit):
    """The monogram as solid white on transparency, centred in a square canvas, fit = share of it."""
    a = src.getchannel("A").point(lambda v: 255 if v > 90 else int(v * 255 / 90))
    scale = fit * canvas / max(src.size)
    size = (round(src.width * scale), round(src.height * scale))
    a = a.resize(size, Image.LANCZOS)
    out = Image.new("RGBA", (canvas, canvas), (255, 255, 255, 0))
    layer = Image.new("RGBA", size, (255, 255, 255, 255))
    layer.putalpha(a)
    out.alpha_composite(layer, ((canvas - size[0]) // 2, (canvas - size[1]) // 2))
    return out


mono_2048 = white_silhouette(mono, S, 0.56)
for name, k in DENSITIES.items():
    folder = os.path.join(RES, "mipmap-" + name)
    os.makedirs(folder, exist_ok=True)
    px = round(108 * k)
    fg.resize((px, px), Image.LANCZOS).save(os.path.join(folder, "ic_launcher_foreground.png"), optimize=True)
    bg.convert("RGB").resize((px, px), Image.LANCZOS).save(os.path.join(folder, "ic_launcher_background.png"), optimize=True)
    mono_2048.resize((px, px), Image.LANCZOS).save(os.path.join(folder, "ic_launcher_monochrome.png"), optimize=True)
    # Notification small icon: 24dp, white on transparency.
    nfolder = os.path.join(RES, "drawable-" + name)
    os.makedirs(nfolder, exist_ok=True)
    white_silhouette(mono, 512, 0.92).resize((round(24 * k),) * 2, Image.LANCZOS).save(os.path.join(nfolder, "ic_notification.png"), optimize=True)

anydpi = os.path.join(RES, "mipmap-anydpi-v26")
os.makedirs(anydpi, exist_ok=True)
xml = """<?xml version="1.0" encoding="utf-8"?>
<adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android">
    <background android:drawable="@mipmap/ic_launcher_background" />
    <foreground android:drawable="@mipmap/ic_launcher_foreground" />
    <monochrome android:drawable="@mipmap/ic_launcher_monochrome" />
</adaptive-icon>
"""
for name in ("ic_launcher.xml", "ic_launcher_round.xml"):
    with open(os.path.join(anydpi, name), "w", encoding="utf-8", newline="\n") as f:
        f.write(xml)

# In-app logo: the crest without the leather, cropped to what is drawn.
crest = fg.crop(fg.getchannel("A").point(lambda v: 255 if v > 10 else 0).getbbox())
side = max(crest.size)
square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
square.alpha_composite(crest, ((side - crest.width) // 2, (side - crest.height) // 2))
os.makedirs(os.path.join(APP, "Images"), exist_ok=True)
square.resize((192, 192), Image.LANCZOS).save(os.path.join(APP, "Images", "mvs-crest.png"), optimize=True)
print("exported")
