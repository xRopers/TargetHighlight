"""Renders images/icon.png (512x512) for the plugin installer, with no third-party dependencies.

Usage: python scripts/make_icon.py
"""
import math
import struct
import zlib
from pathlib import Path

SIZE = 512
OUT = Path(__file__).resolve().parent.parent / "images" / "icon.png"

CYAN = (0.0, 0.86, 1.0)
MAGENTA = (1.0, 0.0, 0.98)
WHITE = (1.0, 1.0, 1.0)
BG_TOP = (0.09, 0.11, 0.20)
BG_BOTTOM = (0.04, 0.05, 0.10)


def clamp(v, lo=0.0, hi=1.0):
    return lo if v < lo else hi if v > hi else v


def mix(a, b, t):
    return tuple(x + (y - x) * t for x, y in zip(a, b))


def coverage(distance):
    """Antialiased coverage for a signed distance (negative = inside), ~1px soft edge."""
    return clamp(0.5 - distance)


def rounded_rect(px, py, half, radius):
    qx, qy = abs(px) - (half - radius), abs(py) - (half - radius)
    outside = math.hypot(max(qx, 0.0), max(qy, 0.0))
    return outside + min(max(qx, qy), 0.0) - radius


def capsule(px, py, ax, ay, bx, by, radius):
    abx, aby = bx - ax, by - ay
    t = clamp(((px - ax) * abx + (py - ay) * aby) / (abx * abx + aby * aby))
    return math.hypot(px - ax - abx * t, py - ay - aby * t) - radius


def shade(x, y):
    px, py = x + 0.5 - SIZE / 2, y + 0.5 - SIZE / 2
    tile = coverage(rounded_rect(px, py, 240, 104))
    if tile <= 0.0:
        return (0, 0, 0, 0)

    r = math.hypot(px, py)
    diagonal = clamp((px - py) / (2 * 200) + 0.5)  # cyan top-left -> magenta bottom-right
    accent = mix(CYAN, MAGENTA, diagonal)

    color = mix(BG_TOP, BG_BOTTOM, clamp(y / SIZE))
    glow = math.exp(-(r * r) / (2 * 135 * 135)) * 0.55
    color = tuple(c + a * glow for c, a in zip(color, accent))

    ring_distance = abs(r - 148) - 15
    ring_glow = math.exp(-(ring_distance * ring_distance) / (2 * 16 * 16)) * 0.65
    color = tuple(c + a * ring_glow for c, a in zip(color, accent))
    color = mix(color, mix(accent, WHITE, 0.25), coverage(ring_distance))

    ticks = min(
        capsule(px, py, 0, -112, 0, -206, 11),
        capsule(px, py, 0, 112, 0, 206, 11),
        capsule(px, py, -112, 0, -206, 0, 11),
        capsule(px, py, 112, 0, 206, 0, 11),
    )
    color = mix(color, WHITE, coverage(ticks))

    dot = r - 24
    color = tuple(c + a * math.exp(-(max(dot, 0) ** 2) / (2 * 14 * 14)) * 0.8 for c, a in zip(color, accent))
    color = mix(color, WHITE, coverage(dot))

    return tuple(round(clamp(c) * 255) for c in color) + (round(tile * 255),)


def write_png(path, pixels):
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    raw = b"".join(b"\x00" + bytes(v for px in row for v in px) for row in pixels)
    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 9))
    png += chunk(b"IEND", b"")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(png)


def main():
    write_png(OUT, [[shade(x, y) for x in range(SIZE)] for y in range(SIZE)])
    print(f"Wrote {OUT}")


if __name__ == "__main__":
    main()
