"""Animate the approved CycleTrim identity. Requires Pillow and NumPy.

Only the three-material cell moves; the C and GPU pins remain fixed.
Run from any directory with: python scripts/render_cycletrim_logo.py
"""
from pathlib import Path
import math
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "mods/CycleTrim"
SIZE = 512
COUNT = 80


def render():
    base = Image.open(OUT / "docs/images/logo-source.png").convert("RGB").resize(
        (SIZE, SIZE), Image.Resampling.LANCZOS)
    pixels = np.asarray(base).astype(float)
    y, x = np.mgrid[:SIZE, :SIZE]
    # Interior-only deformation keeps the cell silhouette and all branding intact.
    u = np.clip((x - 309) / 109, 0, 1)
    v = np.clip((y - 111) / 110, 0, 1)
    envelope = np.sin(math.pi*u)**2 * np.sin(math.pi*v)**2
    frames = []
    for i in range(COUNT):
        phase = 2*math.pi*i/COUNT
        sx = np.clip(x + 10*envelope*np.sin(phase + 2*math.pi*v), 0, SIZE-1)
        sy = np.clip(y + 13*envelope*np.sin(phase + 2*math.pi*u + 0.8), 0, SIZE-1)
        x0, y0 = sx.astype(int), sy.astype(int)
        x1, y1 = np.minimum(x0+1,SIZE-1), np.minimum(y0+1,SIZE-1)
        dx, dy = (sx-x0)[...,None], (sy-y0)[...,None]
        rgb = ((pixels[y0,x0]*(1-dx)+pixels[y0,x1]*dx)*(1-dy)
             + (pixels[y1,x0]*(1-dx)+pixels[y1,x1]*dx)*dy)
        frames.append(Image.fromarray(np.rint(rgb).astype("uint8")))
    # A common palette prevents frame-to-frame color shimmer.
    palette = base.quantize(colors=192)
    indexed = [f.quantize(palette=palette,dither=Image.Dither.NONE) for f in frames]
    indexed[0].save(OUT / "preview.gif", save_all=True, append_images=indexed[1:],
                    duration=50, loop=0, optimize=True, disposal=1)
    base.save(OUT / "preview.png")
    return frames


if __name__ == "__main__":
    render()
    print(OUT / "preview.gif")
