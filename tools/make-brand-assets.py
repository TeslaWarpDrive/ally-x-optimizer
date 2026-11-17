#!/usr/bin/env python3
"""Build Ally Tuner brand assets from the Handheld Boost source art.

Reads the two source PNGs in assets/brand/ and writes the masked tile, the
window icon, the installer wizard images, and the MSIX logo sizes. Run from
anywhere:

    python3 tools/make-brand-assets.py
    python3 tools/make-brand-assets.py --previews /path/to/previews

The logo source is a dark plum rounded tile on a light corner fill. Everything
outside that tile becomes transparent. The anti-aliased rim uses the plum just
inside the edge, so the light corner fill does not leave a white fringe. The
banner source is already full-bleed and is only resized.
"""

from __future__ import annotations

import argparse
import math
import struct
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SOURCE_LOGO = ROOT / "assets" / "brand" / "03-handheld-boost-logo.png"
SOURCE_ART = ROOT / "assets" / "brand" / "03-handheld-boost-art.png"
APP_ASSETS = ROOT / "src" / "AllyTuner.App" / "Assets"
INSTALLER = ROOT / "installer"

ICON_SIZES = (16, 20, 24, 32, 40, 48, 64, 256)
# Square44 target sizes. These match the .ico sizes; the project has no others.
TARGET_SIZES = ICON_SIZES

WIZARD_SMALL = 256
# Classic wizard sidebar keeps a 164:314 frame. 3x is sharp at high DPI.
WIZARD_LARGE = (164 * 3, 314 * 3)


def repo_file(path: Path) -> Path:
    if not path.is_file():
        raise SystemExit(f"Missing {path}")
    return path


def median(values: list[int]) -> int:
    ordered = sorted(values)
    return ordered[len(ordered) // 2]


def corner_background(image: Image.Image, span: int = 16) -> tuple[int, int, int]:
    """Median colour of the four light corners, outside the rounded tile."""
    pixels = image.convert("RGB").load()
    width, height = image.size
    channels: list[list[int]] = [[], [], []]
    origins = ((0, 0), (width - span, 0), (0, height - span), (width - span, height - span))
    for origin_x, origin_y in origins:
        for y in range(origin_y, origin_y + span):
            for x in range(origin_x, origin_x + span):
                pixel = pixels[x, y]
                for channel in range(3):
                    channels[channel].append(pixel[channel])
    return (median(channels[0]), median(channels[1]), median(channels[2]))


def estimated_coverage(pixel: tuple[int, int, int], background: tuple[int, int, int]) -> float:
    """How much of a light corner pixel has been replaced by the tile."""
    drops = []
    for channel in range(3):
        if background[channel] <= 1:
            drops.append(0.0)
        else:
            drops.append((background[channel] - pixel[channel]) / background[channel])
    return max(0.0, min(1.0, max(drops)))


def rounded_rect_sdf(x: float, y: float, width: float, height: float, radius: float) -> float:
    """Signed distance to a rounded rect filling [0, width] x [0, height]. Negative is inside."""
    half_w = width / 2
    half_h = height / 2
    qx = abs(x - half_w) - (half_w - radius)
    qy = abs(y - half_h) - (half_h - radius)
    outside = math.hypot(max(qx, 0.0), max(qy, 0.0))
    inside = min(max(qx, qy), 0.0)
    # iq's rounded-box distance: the corner arc sits `radius` px in from the corner.
    return outside + inside - radius


def pixel_coverage(x: int, y: int, width: int, height: int, radius: float, samples: int) -> float:
    inside = 0
    for sy in range(samples):
        for sx in range(samples):
            px = x + (sx + 0.5) / samples
            py = y + (sy + 0.5) / samples
            if rounded_rect_sdf(px, py, width, height, radius) <= 0:
                inside += 1
    return inside / (samples * samples)


def fit_radius(image: Image.Image, background: tuple[int, int, int]) -> float:
    """Pick the corner radius whose coverage matches the anti-aliased rim."""
    rgb = image.convert("RGB")
    pixels = rgb.load()
    width, height = rgb.size
    span = min(width, height) // 4
    samples: list[tuple[int, int, float]] = []
    corners = (
        (0, 0, 1, 1),
        (width - span, 0, 1, 1),
        (0, height - span, 1, 1),
        (width - span, height - span, 1, 1),
    )
    for origin_x, origin_y, _, _ in corners:
        for y in range(origin_y, origin_y + span):
            for x in range(origin_x, origin_x + span):
                coverage = estimated_coverage(pixels[x, y], background)
                if 0.05 < coverage < 0.95:
                    samples.append((x, y, coverage))
    if len(samples) < 20:
        raise SystemExit("Could not find the rounded-tile edge in the logo source.")

    best_radius = 0.0
    best_error = math.inf
    for step in range(300, 441):
        radius = step / 2
        error = 0.0
        for x, y, expected in samples:
            error += abs(pixel_coverage(x, y, width, height, radius, 4) - expected)
        error /= len(samples)
        if error < best_error:
            best_error = error
            best_radius = radius
    print(f"Fitted corner radius {best_radius:.1f}px (mean edge error {best_error:.3f})")
    return best_radius


def mask_tile(image: Image.Image, radius: float) -> Image.Image:
    """Cut the light corners away. The rim keeps the tile colour, with coverage as alpha.

    Un-blending each edge pixel against the corner fill leaves a few light specks where the
    fitted curve and the source raster do not match. The only thing that meets that edge is
    the plum tile, so partial pixels use that colour and the geometric coverage.
    """
    rgb = image.convert("RGB")
    pixels = rgb.load()
    width, height = rgb.size
    coverage = [0.0] * (width * height)
    rim_samples: list[list[int]] = [[], [], []]
    for y in range(height):
        for x in range(width):
            distance = rounded_rect_sdf(x + 0.5, y + 0.5, width, height, radius)
            index = y * width + x
            if distance >= 1.5:
                continue
            if distance <= -1.5:
                coverage[index] = 1.0
                if distance >= -3.5:
                    pixel = pixels[x, y]
                    for channel in range(3):
                        rim_samples[channel].append(pixel[channel])
                continue
            coverage[index] = pixel_coverage(x, y, width, height, radius, 8)
    if not rim_samples[0]:
        raise SystemExit("Could not sample the tile colour inside the rounded edge.")
    rim = tuple(median(channel) for channel in rim_samples)
    print(f"Rim colour {rim}")

    masked = Image.new("RGBA", (width, height))
    out = masked.load()
    for y in range(height):
        for x in range(width):
            amount = coverage[y * width + x]
            if amount <= 0.02:
                continue
            if amount >= 0.98:
                out[x, y] = (*pixels[x, y], 255)
                continue
            out[x, y] = (*rim, int(round(amount * 255)))
    return masked


def content_box(image: Image.Image) -> tuple[int, int, int, int]:
    """Bounding box of pixels that are not fully transparent. Inclusive."""
    pixels = image.convert("RGBA").load()
    width, height = image.size
    min_x, min_y, max_x, max_y = width, height, -1, -1
    for y in range(height):
        for x in range(width):
            if pixels[x, y][3] == 0:
                continue
            min_x = min(min_x, x)
            min_y = min(min_y, y)
            max_x = max(max_x, x)
            max_y = max(max_y, y)
    if max_x < 0:
        raise SystemExit("Masked logo has no visible pixels.")
    return min_x, min_y, max_x, max_y


def save_png(image: Image.Image, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, "PNG", optimize=True)


def resize_tile(tile: Image.Image, size: int) -> Image.Image:
    return tile.resize((size, size), Image.Resampling.LANCZOS)


def flatten(tile: Image.Image, size: int, background: tuple[int, int, int]) -> Image.Image:
    resized = resize_tile(tile, size)
    canvas = Image.new("RGB", (size, size), background)
    canvas.paste(resized, (0, 0), resized)
    return canvas


def contain(image: Image.Image, size: tuple[int, int], background: tuple[int, int, int]) -> Image.Image:
    target_w, target_h = size
    scale = min(target_w / image.width, target_h / image.height)
    resized = image.resize(
        (max(1, int(round(image.width * scale))), max(1, int(round(image.height * scale)))),
        Image.Resampling.LANCZOS,
    )
    canvas = Image.new("RGB", size, background)
    canvas.paste(resized, ((target_w - resized.width) // 2, (target_h - resized.height) // 2))
    return canvas


def artwork_foreground(image: Image.Image, background: tuple[int, int, int]) -> Image.Image:
    """Crop to the painted part of the banner so a tall frame is not empty."""
    pixels = image.convert("RGB").load()
    width, height = image.size
    min_x, min_y, max_x, max_y = width, height, -1, -1
    for y in range(height):
        for x in range(width):
            pixel = pixels[x, y]
            if sum(abs(pixel[channel] - background[channel]) for channel in range(3)) <= 28:
                continue
            min_x = min(min_x, x)
            min_y = min(min_y, y)
            max_x = max(max_x, x)
            max_y = max(max_y, y)
    if max_x < 0:
        return image
    pad = 8
    return image.crop(
        (
            max(0, min_x - pad),
            max(0, min_y - pad),
            min(width, max_x + 1 + pad),
            min(height, max_y + 1 + pad),
        )
    )


def write_ico(frames: dict[int, Image.Image], path: Path) -> None:
    """Write a 32-bit BMP icon. Width 0 in the directory means 256."""
    blobs: list[tuple[int, bytes]] = []
    for size in ICON_SIZES:
        frame = frames[size].convert("RGBA")
        blobs.append((size, dib_32(frame)))

    count = len(blobs)
    header = struct.pack("<HHH", 0, 1, count)
    directory = bytearray()
    offset = 6 + 16 * count
    payload = bytearray()
    for size, blob in blobs:
        dimension = 0 if size >= 256 else size
        directory += struct.pack("<BBBBHHII", dimension, dimension, 0, 0, 1, 32, len(blob), offset)
        offset += len(blob)
        payload += blob
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(header + directory + payload)


def dib_32(image: Image.Image) -> bytes:
    width, height = image.size
    pixels = image.load()
    xor_rows = bytearray()
    for y in range(height - 1, -1, -1):
        for x in range(width):
            red, green, blue, alpha = pixels[x, y]
            xor_rows += bytes((blue, green, red, alpha))
    mask_stride = ((width + 31) // 32) * 4
    # Alpha carries transparency. An empty AND mask keeps Windows from punching a second hole.
    and_mask = bytes(mask_stride * height)
    header = struct.pack(
        "<IIIHHIIIIII",
        40,
        width,
        height * 2,
        1,
        32,
        0,
        len(xor_rows) + len(and_mask),
        0,
        0,
        0,
        0,
    )
    return header + xor_rows + and_mask


def checker(size: int, first: tuple[int, int, int], second: tuple[int, int, int], cell: int = 8) -> Image.Image:
    canvas = Image.new("RGB", (size, size))
    pixels = canvas.load()
    for y in range(size):
        for x in range(size):
            pixels[x, y] = first if ((x // cell) + (y // cell)) % 2 == 0 else second
    return canvas


def preview_icon(frame: Image.Image, zoom: int = 8) -> Image.Image:
    """Native icon, then the same pixels enlarged on ink and on white."""
    native = frame.convert("RGBA")
    enlarged = native.resize((native.width * zoom, native.height * zoom), Image.Resampling.NEAREST)
    pad = 16
    width = enlarged.width * 2 + pad * 3
    height = enlarged.height + pad * 2
    sheet = Image.new("RGB", (width, height), (32, 32, 36))
    ink = Image.new("RGB", enlarged.size, (14, 13, 24))
    white = Image.new("RGB", enlarged.size, (255, 255, 255))
    ink.paste(enlarged, (0, 0), enlarged)
    white.paste(enlarged, (0, 0), enlarged)
    sheet.paste(ink, (pad, pad))
    sheet.paste(white, (pad * 2 + enlarged.width, pad))
    return sheet


def fringe_report(tile: Image.Image) -> None:
    """Semi-transparent pixels should be the plum rim, not the old light fill."""
    pixels = tile.load()
    worst = 0
    count = 0
    for y in range(tile.height):
        for x in range(tile.width):
            red, green, blue, alpha = pixels[x, y]
            if alpha == 0 or alpha == 255:
                continue
            count += 1
            worst = max(worst, min(red, green, blue))
    print(f"Rim pixels {count}; lightest semi-transparent channel {worst}")
    if worst > 180:
        raise SystemExit("Masked rim still looks like the light corner fill.")


def build(previews: Path | None) -> None:
    logo = Image.open(repo_file(SOURCE_LOGO))
    art = Image.open(repo_file(SOURCE_ART)).convert("RGB")
    if logo.size != (1024, 1024):
        raise SystemExit(f"Logo source is {logo.size}, expected 1024x1024.")
    if art.size != (1344, 768):
        raise SystemExit(f"Banner source is {art.size}, expected 1344x768.")

    background = corner_background(logo)
    print(f"Corner fill {background}")
    radius = fit_radius(logo, background)
    tile = mask_tile(logo, radius)
    fringe_report(tile)
    box = content_box(tile)
    print(f"Opaque bounds x={box[0]}..{box[2]} y={box[1]}..{box[3]}")
    cropped = tile.crop((box[0], box[1], box[2] + 1, box[3] + 1))

    save_png(tile, APP_ASSETS / "LogoTile.png")
    # The tile already meets the canvas on the straight edges, so this is the tight crop.
    save_png(resize_tile(cropped, 128), APP_ASSETS / "LogoTitleBar.png")

    frames = {size: resize_tile(cropped, size) for size in ICON_SIZES}
    write_ico(frames, APP_ASSETS / "AppIcon.ico")

    plum = (21, 4, 37)
    save_png(resize_tile(cropped, 44), APP_ASSETS / "Square44x44Logo.png")
    save_png(resize_tile(cropped, 88), APP_ASSETS / "Square44x44Logo.scale-200.png")
    for size in TARGET_SIZES:
        frame = resize_tile(cropped, size)
        save_png(frame, APP_ASSETS / f"Square44x44Logo.targetsize-{size}.png")
        save_png(frame, APP_ASSETS / f"Square44x44Logo.targetsize-{size}_altform-unplated.png")

    save_png(resize_tile(cropped, 150), APP_ASSETS / "Square150x150Logo.png")
    save_png(resize_tile(cropped, 300), APP_ASSETS / "Square150x150Logo.scale-200.png")

    art_background = art.getpixel((art.width - 1, art.height // 2))
    wide_100 = contain(art, (310, 150), art_background)
    wide_200 = contain(art, (620, 300), art_background)
    splash_100 = contain(art, (620, 300), art_background)
    splash_200 = contain(art, (1240, 600), art_background)
    save_png(wide_100, APP_ASSETS / "Wide310x150Logo.png")
    save_png(wide_200, APP_ASSETS / "Wide310x150Logo.scale-200.png")
    save_png(splash_100, APP_ASSETS / "SplashScreen.png")
    save_png(splash_200, APP_ASSETS / "SplashScreen.scale-200.png")

    save_png(flatten(cropped, 50, plum), APP_ASSETS / "StoreLogo.png")
    save_png(flatten(cropped, 50, plum), APP_ASSETS / "StoreLogo.scale-100.png")
    save_png(flatten(cropped, 100, plum), APP_ASSETS / "StoreLogo.scale-200.png")

    save_png(resize_tile(cropped, WIZARD_SMALL), INSTALLER / "wizard-small.png")
    painted = artwork_foreground(art, art_background)
    save_png(contain(painted, WIZARD_LARGE, art_background), INSTALLER / "wizard-large.png")

    if previews is None:
        return
    previews.mkdir(parents=True, exist_ok=True)
    for size, frame in frames.items():
        save_png(preview_icon(frame), previews / f"ico-{size}.png")
    sheet_rows = [preview_icon(frames[size]) for size in ICON_SIZES]
    sheet_w = max(row.width for row in sheet_rows)
    sheet_h = sum(row.height for row in sheet_rows)
    sheet = Image.new("RGB", (sheet_w, sheet_h), (32, 32, 36))
    y = 0
    for row in sheet_rows:
        sheet.paste(row, (0, y))
        y += row.height
    save_png(sheet, previews / "ico-sheet.png")

    shown = resize_tile(cropped, 512)
    for name, backdrop in (
        ("logo-tile-on-ink.png", (14, 13, 24)),
        ("logo-tile-on-white.png", (255, 255, 255)),
        ("logo-tile-on-magenta.png", (255, 0, 255)),
    ):
        canvas = Image.new("RGB", shown.size, backdrop)
        canvas.paste(shown, (0, 0), shown)
        save_png(canvas, previews / name)
    save_png(wide_200, previews / "wide-310x150-scale-200.png")
    save_png(splash_200, previews / "splash-scale-200.png")
    save_png(Image.open(INSTALLER / "wizard-large.png"), previews / "wizard-large.png")
    save_png(Image.open(INSTALLER / "wizard-small.png"), previews / "wizard-small.png")
    print(f"Previews in {previews}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--previews", type=Path, help="Also write icon and fringe previews here.")
    args = parser.parse_args()
    build(args.previews)


if __name__ == "__main__":
    try:
        main()
    except BrokenPipeError:
        sys.exit(0)
