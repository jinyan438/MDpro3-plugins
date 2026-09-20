"""Export the native PSD's SR lettering and badge with an emerald foil palette.

Requires psd-tools and Pillow; the source PSD is read-only.
"""

import argparse
from pathlib import Path

from PIL import Image
from psd_tools import PSDImage


def build(source, output):
    psd = PSDImage.open(source)
    for layer in psd:
        layer.visible = layer.name == "R"
    group = next(layer for layer in psd if layer.name == "R")
    for layer in group:
        if layer.kind == "type":
            layer.visible = layer.name == "SR"
    badge = psd.composite(force=True).convert("RGBA")
    hsv = badge.convert("RGB").convert("HSV")
    h, s, v = hsv.split()
    h = h.point(lambda value: (value - 65) % 256)
    badge = Image.merge("HSV", (h, s, v)).convert("RGBA")
    badge.putalpha(psd.composite(force=True).convert("RGBA").getchannel("A"))
    output.parent.mkdir(parents=True, exist_ok=True)
    badge.resize((128, 128), Image.Resampling.LANCZOS).save(output)


if __name__ == "__main__":
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, default=root.parent / "MDPro3/Assets/Texture/PSD/Card Rarity Icon.psd")
    parser.add_argument("--output", type=Path, default=root / "MDPro3Plugins/Resources/MDPro3Plugins/StoryMode/Icon_Rarity_SR.png")
    args = parser.parse_args()
    build(args.source, args.output)
