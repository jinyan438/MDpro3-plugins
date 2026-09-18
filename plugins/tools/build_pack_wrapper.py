"""Build pack layers from the user's Master Duel reference (requires Pillow).

Run with --reference SCREENSHOT once; subsequent builds use the extracted printing.
"""

import argparse
import math
from pathlib import Path
import random
import uuid

from PIL import Image, ImageDraw, ImageFilter


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "tools/pack_wrapper_source"
OUTPUT = ROOT / "MDPro3Plugins/Resources/MDPro3Plugins/PackBrowser"
SIZE = (483, 1004)
WIDTH, HEIGHT = SIZE


def polygon_mask(polygons):
    scale = 3
    mask = Image.new("L", (WIDTH * scale, HEIGHT * scale))
    draw = ImageDraw.Draw(mask)
    for polygon in polygons:
        draw.polygon([(round(x * scale), round(y * scale)) for x, y in polygon], fill=255)
    return mask.resize(SIZE, Image.Resampling.LANCZOS)


def extract(reference):
    image = Image.open(reference).convert("RGBA")
    if image.size != (3170, 1478):
        raise ValueError("Expected the original 3170 x 1478 reference screenshot")
    pack = image.crop((1371, 383, 1854, 1387))
    SOURCE.mkdir(parents=True, exist_ok=True)
    pack.crop((0, 0, WIDTH, 42)).save(SOURCE / "FoilStrip.png")
    mask = polygon_mask([
        # Lower foil and the wide indigo stripe; the monster area is excluded.
        [(0, 720), (238, 980), (483, 718), (483, 1004), (0, 1004)],
        # The actual crystal triangle and wordmark, including their bevel/shadow.
        [(130, 760), (351, 760), (243, 941)],
        [(158, 746), (165, 731), (188, 728), (203, 735), (219, 727),
         (238, 733), (259, 730), (273, 735), (289, 729), (303, 737),
         (312, 751), (317, 767), (329, 780), (311, 786), (275, 782),
         (248, 790), (224, 781), (206, 788), (185, 779), (157, 778)],
        [(34, 797), (61, 819), (88, 796), (95, 817), (108, 797),
         (136, 797), (136, 801), (157, 797), (367, 797), (367, 791),
         (384, 783), (384, 797), (408, 797), (416, 787), (430, 792),
         (431, 830), (445, 830), (445, 848), (383, 848), (363, 843),
         (291, 846), (280, 860), (272, 845), (65, 845), (38, 862)],
    ])
    pack.putalpha(mask)
    clean = Image.new("RGBA", SIZE)
    clean.alpha_composite(pack)
    clean.save(SOURCE / "ReferencePrint.png", optimize=True)


def foil():
    strip = Image.open(SOURCE / "FoilStrip.png").convert("RGB")
    image = Image.new("RGBA", SIZE)
    pixels = image.load()
    grain = random.Random(21)
    for y in range(HEIGHT):
        for x in range(WIDTH):
            top = max(0.0, 1.0 - y / 380.0)
            gold = max(0.0, min(1.0, (y - 520.0) / 240.0))
            metal = 0.9 + 0.1 * math.sin(x / WIDTH * math.pi) + grain.uniform(-0.012, 0.012)
            mesh = (strip.getpixel((x, y % 40))[0] - 98) * top * 0.7
            base = (23 + 58 * top + 199 * gold, 32 + 41 * top + 153 * gold, 48 - 22 * top - 24 * gold)
            pixels[x, y] = tuple(max(0, min(255, int(c * metal + mesh))) for c in base) + (255,)
            if y < 200:
                source = strip.getpixel((x, y % 40))
                pixels[x, y] = tuple(round(c * (1 - y / 950)) for c in source) + (255,)
    return image


def save(name, image):
    image.save(OUTPUT / f"{name}.png", optimize=True)
    guid = uuid.uuid5(uuid.NAMESPACE_URL, f"mdpro3-plugins/pack-browser/{name}").hex
    (OUTPUT / f"{name}.png.meta").write_text(f"""fileFormatVersion: 2
guid: {guid}
TextureImporter:
  serializedVersion: 13
  mipmaps:
    enableMipMap: 0
    sRGBTexture: 1
  isReadable: 0
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  alphaUsage: 1
  alphaIsTransparency: 1
  textureType: 0
  textureShape: 1
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    textureFormat: -1
    textureCompression: 0
    overridden: 0
""", encoding="utf-8")


def build():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    backing = foil()
    save("Foil", backing)
    printed = Image.open(SOURCE / "ReferencePrint.png").convert("RGBA")
    draw = ImageDraw.Draw(printed)
    draw.rectangle((0, 0, WIDTH - 1, HEIGHT - 1), outline="#84784c", width=2)
    draw.line((2, 1, WIDTH - 3, 1), fill="#c3ba8d", width=2)
    draw.line((2, 2, 2, HEIGHT - 3), fill=(219, 213, 163, 175), width=1)
    save("Wrapper", printed)

    gloss = Image.new("RGBA", SIZE)
    pixels = gloss.load()
    for y in range(HEIGHT):
        for x in range(WIDTH):
            crease = 12 * math.exp(-((x - 9) / 5) ** 2) + 9 * math.exp(-((x - 465) / 7) ** 2)
            pixels[x, y] = (246, 249, 222, round(crease))
    save("Gloss", gloss)

    # White-gold core and broad bloom outside the printed pack edge.
    padding = 48
    size = (WIDTH + 2 * padding, HEIGHT + 2 * padding)
    line = Image.new("L", size)
    ImageDraw.Draw(line).rectangle((padding - 5, padding - 5, WIDTH + padding + 4, HEIGHT + padding + 4), outline=255, width=9)
    glow = Image.new("RGBA", size)
    for radius, color, gain in ((23, (255, 167, 0), 1.7), (10, (255, 207, 18), 2.0), (3, (255, 245, 130), 1.8), (0.7, (255, 255, 226), 1.0)):
        layer = Image.new("RGBA", size, (*color, 0))
        layer.putalpha(line.filter(ImageFilter.GaussianBlur(radius)).point(lambda a: min(255, round(a * gain))))
        glow = Image.alpha_composite(glow, layer)
    save("Glow", glow)
    print(f"Generated reference-matched pack layers in {OUTPUT}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--reference", type=Path)
    args = parser.parse_args()
    if args.reference:
        extract(args.reference)
    build()
