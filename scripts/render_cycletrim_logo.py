"""Export CycleTrim branding for the module, documentation and small icons.

Requires Pillow. Run from any directory:
    python scripts/render_cycletrim_logo.py

The source artwork owns the geometry. Exports only resize and encode it so
the multicomponent bands, GPU cores and wordmark remain crisp and aligned.
"""

from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "mods/CycleTrim"
IMAGES = OUT / "docs/images"
SIZE = 512
ICON_SIZES = (512, 128, 64, 32)


def load_source(path):
    with Image.open(path) as source:
        if source.width != source.height:
            raise ValueError(f"Logo source must be square: {path}")
        return source.convert("RGB")


def render():
    brand = load_source(IMAGES / "logo-source.png")
    icon = load_source(IMAGES / "logo-icon-source.png")
    preview = brand.resize((SIZE, SIZE), Image.Resampling.LANCZOS)
    preview.save(OUT / "preview.png", optimize=True)

    # Keep the established GIF URL available to existing documentation links.
    preview.quantize(colors=256, dither=Image.Dither.NONE).save(
        OUT / "preview.gif", optimize=True
    )
    for size in ICON_SIZES:
        icon.resize((size, size), Image.Resampling.LANCZOS).save(
            IMAGES / f"logo-icon-{size}.png", optimize=True
        )


if __name__ == "__main__":
    render()
    print(OUT / "preview.png")
