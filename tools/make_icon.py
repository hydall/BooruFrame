#!/usr/bin/env python3
"""Generate BooruFrame/Resources/frame.ico — the app / tray icon.

The icon is a picture frame: a cream frame around a small photo (sky, sun, two
mountains).  It is drawn from vector-ish primitives and supersampled, so every
size in the .ico is rendered at its own resolution instead of being resized.

No third-party packages are needed — the PNG and BMP payloads are written by hand.

    python3 tools/make_icon.py
"""

from __future__ import annotations

import math
import os
import struct
import zlib

# Sizes stored in the icon. <= 48 px go in as classic 32-bit DIBs (maximum
# compatibility, e.g. the notification area), the big ones as PNG (smaller file).
#
# Biggest first: Windows and System.Drawing pick the entry that fits best whatever the
# order, but WPF's Window.Icon simply takes the first one, and a 16x16 image blown up to
# fill an Alt+Tab card looks it.
SIZES = [256, 128, 64, 48, 32, 24, 20, 16]
PNG_FROM = 64

SS = 4  # supersampling factor per axis

FRAME = (0xF2, 0xE7, 0xD2)          # cream frame
FRAME_EDGE = (0xC9, 0xB4, 0x92)     # slightly darker frame rim
SKY_TOP = (0x7C, 0xC6, 0xF2)
SKY_BOTTOM = (0x1E, 0x5F, 0x92)
SUN = (0xFF, 0xD3, 0x6E)
MOUNTAIN_BACK = (0xB6, 0xD9, 0xF0)
MOUNTAIN_FRONT = (0xEF, 0xF8, 0xFF)


def rounded_rect(x, y, x0, y0, x1, y1, r):
    """True when (x, y) is inside the rounded rectangle."""
    if x < x0 or x > x1 or y < y0 or y > y1:
        return False
    cx = min(max(x, x0 + r), x1 - r)
    cy = min(max(y, y0 + r), y1 - r)
    dx = x - cx
    dy = y - cy
    return dx * dx + dy * dy <= r * r


def in_triangle(x, y, a, b, c):
    def side(p, q):
        return (q[0] - p[0]) * (y - p[1]) - (q[1] - p[1]) * (x - p[0])

    s1, s2, s3 = side(a, b), side(b, c), side(c, a)
    return (s1 >= 0 and s2 >= 0 and s3 >= 0) or (s1 <= 0 and s2 <= 0 and s3 <= 0)


def sample(x, y):
    """Colour of the artwork at (x, y) in the unit square; None = transparent."""
    # Frame body.
    if not rounded_rect(x, y, 0.030, 0.030, 0.970, 0.970, 0.170):
        return None

    # Inner rim: a hair darker so the frame keeps an edge on a light background.
    if not rounded_rect(x, y, 0.055, 0.055, 0.945, 0.945, 0.150):
        return FRAME_EDGE

    photo = rounded_rect(x, y, 0.170, 0.170, 0.830, 0.830, 0.060)
    if not photo:
        return FRAME

    # Photo: sky gradient bottom-lit by a low sun.
    t = (y - 0.170) / 0.660
    colour = tuple(
        int(round(SKY_TOP[i] + (SKY_BOTTOM[i] - SKY_TOP[i]) * t)) for i in range(3)
    )

    # Sun.
    if math.hypot(x - 0.655, y - 0.345) <= 0.088:
        colour = SUN

    # Mountains (back one first, so the front one overlaps it).
    if in_triangle(x, y, (0.545, 0.830), (0.720, 0.520), (0.900, 0.830)):
        colour = MOUNTAIN_BACK
    if in_triangle(x, y, (0.115, 0.830), (0.420, 0.415), (0.720, 0.830)):
        colour = MOUNTAIN_FRONT

    return colour


def render(size):
    """Render the artwork at `size` px; returns rows of (r, g, b, a) tuples."""
    rows = []
    step = 1.0 / (size * SS)
    for py in range(size):
        row = []
        for px in range(size):
            r = g = b = a = 0
            for sy in range(SS):
                y = (py * SS + sy + 0.5) * step
                for sx in range(SS):
                    x = (px * SS + sx + 0.5) * step
                    c = sample(x, y)
                    if c is not None:
                        r += c[0]
                        g += c[1]
                        b += c[2]
                        a += 255
            n = SS * SS
            if a == 0:
                row.append((0, 0, 0, 0))
            else:
                hits = a // 255
                row.append((r // hits, g // hits, b // hits, a // n))
        rows.append(row)
    return rows


def to_png(rows):
    size = len(rows)
    raw = bytearray()
    for row in rows:
        raw.append(0)  # filter: none
        for r, g, b, a in row:
            raw += bytes((r, g, b, a))

    def chunk(tag, data):
        out = struct.pack(">I", len(data)) + tag + data
        return out + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    header = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", header)
        + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
        + chunk(b"IEND", b"")
    )


def to_dib(rows):
    """32-bit bottom-up DIB + AND mask, as stored inside a classic .ico entry."""
    size = len(rows)
    header = struct.pack(
        "<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0, 0, 0, 0, 0, 0
    )

    xor = bytearray()
    for row in reversed(rows):
        for r, g, b, a in row:
            xor += bytes((b, g, r, a))

    mask_stride = ((size + 31) // 32) * 4
    mask = bytearray()
    for row in reversed(rows):
        bits = bytearray(mask_stride)
        for x, (_, _, _, a) in enumerate(row):
            if a == 0:  # fully transparent -> "leave the background alone"
                bits[x // 8] |= 0x80 >> (x % 8)
        mask += bits

    return header + bytes(xor) + bytes(mask)


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    out_path = os.path.join(root, "BooruFrame", "Resources", "frame.ico")
    os.makedirs(os.path.dirname(out_path), exist_ok=True)

    images = []
    for size in SIZES:
        rows = render(size)
        images.append((size, to_png(rows) if size >= PNG_FROM else to_dib(rows)))
        print(f"  rendered {size}x{size}")

    header = struct.pack("<HHH", 0, 1, len(images))
    offset = len(header) + 16 * len(images)
    entries = bytearray()
    for size, blob in images:
        entries += struct.pack(
            "<BBBBHHII",
            size if size < 256 else 0,
            size if size < 256 else 0,
            0,
            0,
            1,
            32,
            len(blob),
            offset,
        )
        offset += len(blob)

    with open(out_path, "wb") as f:
        f.write(header)
        f.write(entries)
        for _, blob in images:
            f.write(blob)

    print(f"wrote {out_path} ({os.path.getsize(out_path)} bytes)")


if __name__ == "__main__":
    main()
