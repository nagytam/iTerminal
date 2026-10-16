"""Draws the iTerminal icon and writes iTerminal/Assets/iTerminal.ico (16-256 px, PNG frames).

    python Tools/make-icon.py

Pure standard library: the icon is two terminal panels with a prompt in the left one, rendered
with 4x supersampling.
"""
import struct, zlib, os

S = 256
SS = 4


def inside_round_rect(x, y, x0, y0, x1, y1, r):
    if x < x0 or x > x1 or y < y0 or y > y1:
        return False
    cx = min(max(x, x0 + r), x1 - r)
    cy = min(max(y, y0 + r), y1 - r)
    return (x - cx) ** 2 + (y - cy) ** 2 <= r * r


def seg_dist(px, py, ax, ay, bx, by):
    dx, dy = bx - ax, by - ay
    t = max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)))
    return ((px - ax - t * dx) ** 2 + (py - ay - t * dy) ** 2) ** 0.5


def sample(x, y):
    """RGBA of the icon at a point in a 256x256 design space."""
    if not inside_round_rect(x, y, 8, 8, 248, 248, 44):
        return (0, 0, 0, 0)
    # Background: deep blue gradient.
    t = y / S
    col = (int(18 + 14 * t), int(32 + 28 * t), int(74 + 40 * t))
    # Title bar strip.
    if y < 56:
        col = (10, 18, 46)
        for i, c in enumerate(((255, 95, 86), (255, 189, 46), (39, 201, 63))):
            if (x - (36 + i * 22)) ** 2 + (y - 32) ** 2 <= 49:
                col = c
        return col + (255,)
    # Two panels: dark insets with a gap.
    if inside_round_rect(x, y, 24, 72, 120, 232, 10) or inside_round_rect(x, y, 136, 72, 232, 232, 10):
        col = (8, 14, 34)
        # Right panel: file rows.
        if x > 136:
            for k in range(5):
                ry = 92 + k * 28
                w = (70, 52, 62, 44, 56)[k]
                if 152 <= x <= 152 + w and ry <= y <= ry + 10:
                    col = (96, 165, 250) if k == 0 else (148, 163, 184)
        else:
            # Left panel: ">" prompt and a cursor block.
            if seg_dist(x, y, 42, 112, 70, 138) <= 7 or seg_dist(x, y, 70, 138, 42, 164) <= 7:
                col = (74, 222, 128)
            if 82 <= x <= 108 and 156 <= y <= 166:
                col = (74, 222, 128)
    return col + (255,)


def render():
    big = []
    for py in range(S):
        row = []
        for px in range(S):
            acc = [0, 0, 0, 0]
            for sy in range(SS):
                for sx in range(SS):
                    r, g, b, a = sample(px + (sx + .5) / SS, py + (sy + .5) / SS)
                    acc[0] += r * a; acc[1] += g * a; acc[2] += b * a; acc[3] += a
            a = acc[3]
            row.append((acc[0] // a, acc[1] // a, acc[2] // a, a // (SS * SS)) if a else (0, 0, 0, 0))
        big.append(row)
    return big


def scale(img, n):
    f = S // n if S % n == 0 else S / n
    out = []
    for y in range(n):
        row = []
        for x in range(n):
            x0, x1 = int(x * S / n), max(int((x + 1) * S / n), int(x * S / n) + 1)
            y0, y1 = int(y * S / n), max(int((y + 1) * S / n), int(y * S / n) + 1)
            acc = [0, 0, 0, 0]; cnt = 0
            for yy in range(y0, y1):
                for xx in range(x0, x1):
                    r, g, b, a = img[yy][xx]
                    acc[0] += r * a; acc[1] += g * a; acc[2] += b * a; acc[3] += a; cnt += 1
            a = acc[3]
            row.append((acc[0] // a, acc[1] // a, acc[2] // a, a // cnt) if a else (0, 0, 0, 0))
        out.append(row)
    return out


def png(img):
    n = len(img)
    raw = b"".join(b"\x00" + bytes(v for p in row for v in p) for row in img)
    def chunk(t, d):
        c = struct.pack(">I", len(d)) + t + d
        return c + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", n, n, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")


def main():
    big = render()
    sizes = [16, 24, 32, 48, 64, 128, 256]
    frames = [(n, png(big if n == S else scale(big, n))) for n in sizes]
    out = struct.pack("<HHH", 0, 1, len(frames))
    offset = 6 + 16 * len(frames)
    body = b""
    for n, data in frames:
        out += struct.pack("<BBBBHHII", n % 256, n % 256, 0, 0, 1, 32, len(data), offset + len(body))
        body += data
    path = os.path.join(os.path.dirname(__file__), "..", "iTerminal", "Assets", "iTerminal.ico")
    with open(path, "wb") as f:
        f.write(out + body)
    with open(path.replace(".ico", ".png"), "wb") as f:
        f.write(frames[-1][1])


main()
