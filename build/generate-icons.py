#!/usr/bin/env python3
"""Regenerates the app and tray icons from their SVG sources.

The SVGs in src/ModernWorkElevate.App.Avalonia/Assets are the source of truth;
the .ico and .png files next to them are build output committed to the repo,
so a Windows build needs no image tooling. Run this after editing any SVG.

Every size is rendered from the vector source on its own instead of letting
Windows downscale one large bitmap: at 16 px a downscaled arrow and status
dot turn to mush.

Two tray variants per state, named for the taskbar they are drawn for:
-onlight carries a dark keyline, -ondark a light one. TrayPopupController
picks between them from the Windows setting that colours the taskbar.

Requires: pip install cairosvg pillow
"""
import io
import pathlib
import sys

try:
    import cairosvg
    from PIL import Image
except ImportError:
    sys.exit("error: missing packages - install with: pip install cairosvg pillow")

ASSETS = pathlib.Path(__file__).resolve().parent.parent / "src" / "ModernWorkElevate.App.Avalonia" / "Assets"
TRAY_SIZES = (16, 20, 24, 32, 48)
APP_SIZES = (16, 24, 32, 48, 64, 128, 256)
LOGO_SIZE = 215  # Entra App Registration logo: 215 x 215 px recommended, < 100 KB


def render(svg: pathlib.Path, size: int) -> Image.Image:
    png = cairosvg.svg2png(url=str(svg), output_width=size, output_height=size)
    return Image.open(io.BytesIO(png)).convert("RGBA")


def write_ico(svg: pathlib.Path, sizes: tuple[int, ...]) -> None:
    frames = [render(svg, s) for s in sizes]
    target = svg.with_suffix(".ico")
    frames[-1].save(target, format="ICO", sizes=[(s, s) for s in sizes], append_images=frames[:-1])
    print(f"wrote {target.name} ({', '.join(map(str, sizes))})")


def main() -> None:
    for svg in sorted(ASSETS.glob("tray-icon-*.svg")):
        write_ico(svg, TRAY_SIZES)
    app = ASSETS / "app-icon.svg"
    write_ico(app, APP_SIZES)
    logo = ASSETS / "app-registration-logo.png"
    render(app, LOGO_SIZE).save(logo, optimize=True)
    print(f"wrote {logo.name} ({LOGO_SIZE} px)")


if __name__ == "__main__":
    main()
