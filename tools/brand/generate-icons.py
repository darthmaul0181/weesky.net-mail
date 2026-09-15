"""Regenerates every shipped icon from the master artwork.

Run it after replacing `assets/brand/scotty.png` — nothing else reads that file, and pointing a
<link> at whatever an image editor exported is what this script exists to prevent.

    python -m pip install pillow      # the one dependency
    python tools/brand/generate-icons.py

It writes, all from the one source:
  src/frontend/src/assets/scotty-720.webp / -1440.webp  the whole illustration, About tab
  src/frontend/src/assets/logo-192.png                  top bar, desktop notification, iOS icon
  src/frontend/src/assets/favicon-32.png                browser tab
  src/frontend/public/icon-192.png / icon-512.png       installed application

The small icons crop the cat's head: the whole drawing is 1.4 times wider than tall and unreadable
at 16px. The crop is expressed in the source's own pixels, so re-exporting the artwork at another
size keeps working; changing what the drawing contains means re-reading these four numbers.
"""
import pathlib
from PIL import Image, ImageDraw

ROOT = pathlib.Path(__file__).resolve().parents[2]
SOURCE = ROOT / "assets/brand/scotty.png"
ASSETS = ROOT / "src/frontend/src/assets"
PUBLIC = ROOT / "src/frontend/public"

HEAD_BOX = (300, 30, 840, 570)
# The disc's fill is --pane-item-active-bg of the default palette: the cat sits on the same cream
# the active settings row wears, and it reads on a light tab strip as well as on the dark top bar.
DISC_FILL = (253, 243, 240, 255)
# Of the disc's diameter. Small on purpose: the head has to fill the disc to still read as a
# cat at 26px in the top bar — at 0.115 it was a pale smudge with a lot of cream around it.
HEAD_INSET = 0.02


def resized(image: Image.Image, size: tuple[int, int]) -> Image.Image:
    """Alpha premultiplied, or the transparent black around the drawing bleeds into its edges."""
    return image.convert("RGBa").resize(size, Image.LANCZOS).convert("RGBA")


def disc(source: Image.Image, size: int) -> Image.Image:
    work = size * 4
    out = Image.new("RGBA", (work, work), (0, 0, 0, 0))
    ImageDraw.Draw(out).ellipse((0, 0, work - 1, work - 1), fill=DISC_FILL)

    inset = round(work * HEAD_INSET)
    head = Image.new("RGBA", (work, work), (0, 0, 0, 0))
    head.alpha_composite(resized(source.crop(HEAD_BOX), (work - 2 * inset,) * 2), (inset, inset))

    mask = Image.new("L", (work, work), 0)
    ImageDraw.Draw(mask).ellipse((0, 0, work - 1, work - 1), fill=255)
    head.putalpha(Image.composite(head.getchannel("A"), Image.new("L", (work, work), 0), mask))

    out.alpha_composite(head)
    return resized(out, (size, size))


def main() -> None:
    source = Image.open(SOURCE).convert("RGBA")
    whole = source.crop(source.getchannel("A").getbbox())

    for width in (720, 1440):
        height = round(whole.height * width / whole.width)
        resized(whole, (width, height)).save(
            ASSETS / f"scotty-{width}.webp", "WEBP", quality=80, method=6)

    disc(source, 192).save(ASSETS / "logo-192.png", optimize=True)
    disc(source, 32).save(ASSETS / "favicon-32.png", optimize=True)
    disc(source, 192).save(PUBLIC / "icon-192.png", optimize=True)
    disc(source, 512).save(PUBLIC / "icon-512.png", optimize=True)
    print("icons written")


if __name__ == "__main__":
    main()
