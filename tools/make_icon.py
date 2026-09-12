#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
RockCore 图标生成器（零第三方依赖，仅用 Python 标准库）
设计：地质工程风格 —— 放大镜 + 岩芯柱（岩芯分层岩性），深蓝工程色背景。
输出：src/RockCore.Wpf/Resources/RockCore.ico（多尺寸 PNG 压缩 ICO）
     + 一张 256px PNG 预览：docs/icon_preview.png
原理：超采样抗锯齿（SSAA，4x4）→ 纯标准库 PNG 编码 → 组装 ICO 容器。
"""
import struct, zlib, os, math

OUT_DIR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                       "src", "RockCore.Wpf", "Resources")
PREVIEW = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "docs", "icon_preview.png")

# ---------- 颜色 ----------
BG_TOP   = (44, 95, 138)    # 工程蓝（上）
BG_BOT   = (20, 45, 70)     # 工程蓝（下）
COL_L    = (210, 180, 140)  # 岩芯亮边
COL_R    = (120, 95, 70)    # 岩芯暗边
STRATA = [(214,190,152),(178,178,178),(176,106,78),(122,94,74),(200,178,142),(150,140,128),(120,96,74)]
RULER    = (240,240,240)
RING     = (245,246,248)
HANDLE   = (236,238,240)
LENS     = (212,232,250)
HILITE   = (255,255,255)
SPECKLE  = (70,54,40)

# ---------- 几何（单位空间 0..1，列在左、放大镜在右上）----------
COLX, COLW = 0.12, 0.46
COLTOP, COLBOT = 0.13, 0.88
GLASS_CX, GLASS_CY, GLASS_R = 0.665, 0.435, 0.205
HANDLE_ANG, HANDLE_LEN, HANDLE_W = math.radians(45), 0.30, 0.13

# ---------- 抗锯齿几何原语（超采样） ----------
def _col_rock(x, y):
    """命中岩芯柱则返回 (True, 颜色)，否则 (False, None)。"""
    if COLX < x < COLX + COLW and COLTOP < y < COLBOT:
        t = (y - COLTOP) / (COLBOT - COLTOP)
        band = STRATA[min(len(STRATA) - 1, int(t * len(STRATA)))]
        frac = (t * len(STRATA)) % 1.0
        if frac < 0.07 or frac > 0.93:
            band = tuple(int(c * 0.70) for c in band)  # 岩层分界线
        shade = 1.0 - 0.20 * ((x - COLX) / COLW)       # 柱体左亮右暗
        col = tuple(int(c * shade) for c in band)
        if x < COLX + 0.016:                            # 左侧高光边
            col = tuple(int(0.65 * col[k] + 0.35 * COL_L[k]) for k in range(3))
        if COLX + 0.012 < x < COLX + 0.020 and (y * 30.0) % 1.0 < 0.16:  # 左侧刻度线
            col = tuple(int(0.55 * col[k] + 0.45 * RULER[k]) for k in range(3))
        h = (int(x * 1024) * 73856093) ^ (int(y * 1024) * 19349663)
        if (h & 0x1FF) == 0:                            # 确定性岩石颗粒
            col = SPECKLE
        return True, col
    return False, None

def inside(px, py):
    x = px / 1024.0
    y = py / 1024.0
    gx, gy = x - GLASS_CX, y - GLASS_CY
    dist = math.sqrt(gx * gx + gy * gy)
    in_lens = dist < GLASS_R
    in_ring = abs(dist - GLASS_R) < 0.028
    hx, hy = math.cos(HANDLE_ANG), math.sin(HANDLE_ANG)
    px0, py0 = GLASS_CX + GLASS_R * hx, GLASS_CY + GLASS_R * hy
    dx, dy = x - px0, y - py0
    along = dx * hx + dy * hy
    in_handle = (0 < along < HANDLE_LEN) and (abs(-hx * dy + hy * dx) < HANDLE_W / 2.0)
    col_hit, col_rock = _col_rock(x, y)
    # 图层顺序：手柄 > 边框 > 镜片 > 岩芯柱 > 背景
    if in_handle:
        return HANDLE
    if in_ring:
        return RING
    if in_lens:
        if col_hit:  # 透过镜片看岩芯：提亮，呈现“放大检视”
            return tuple(min(255, int(c * 1.18 + 24)) for c in col_rock)
        hx2 = (x - (GLASS_CX - 0.05)) / (GLASS_R * 0.85)
        hy2 = (y - (GLASS_CY - 0.05)) / (GLASS_R * 0.85)
        return HILITE if (hx2 * hx2 + hy2 * hy2 < 0.5) else LENS
    if col_hit:
        return col_rock
    return None  # 背景

def render(size):
    SS = 4
    S = size * SS
    buf = bytearray(S * S * 4)
    for j in range(S):
        y = j / (S - 1) if S > 1 else 0.0
        for i in range(S):
            x = i / (S - 1) if S > 1 else 0.0
            col = inside(int(x * 1024), int(y * 1024))
            if col is None:
                t = y
                col = tuple(int(BG_TOP[c] * (1 - t) + BG_BOT[c] * t) for c in range(3))
            a = 255
            for k in range(3):
                buf[(j * S + i) * 4 + k] = col[k]
            buf[(j * S + i) * 4 + 3] = a
    # 下采样（SSAA）
    out = bytearray(size * size * 4)
    for j in range(size):
        for i in range(size):
            r = g = b = 0
            for dj in range(SS):
                for di in range(SS):
                    o = ((j * SS + dj) * S + (i * SS + di)) * 4
                    r += buf[o]; g += buf[o + 1]; b += buf[o + 2]
            n = SS * SS
            o2 = (j * size + i) * 4
            out[o2] = r // n; out[o2 + 1] = g // n; out[o2 + 2] = b // n; out[o2 + 3] = 255
    return bytes(out)

def encode_png(size, rgba):
    def chunk(typ, data):
        c = struct.pack(">I", len(data)) + typ + data
        return c + struct.pack(">I", zlib.crc32(typ + data) & 0xffffffff)
    raw = b"".join(b"\x00" + rgba[j * size * 4:(j + 1) * size * 4] for j in range(size))
    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 9))
    png += chunk(b"IEND", b"")
    return png

def build_ico(pngs):
    """pngs: list of (size, pngbytes)"""
    n = len(pngs)
    hdr = struct.pack("<HHH", 0, 1, n)
    entries, offset = b"", 6 + 16 * n
    for size, data in pngs:
        w = size if size < 256 else 0
        entries += struct.pack("<BBBBHHII", w, w, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    body = b"".join(d for _, d in pngs)
    return hdr + entries + body

def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    os.makedirs(os.path.dirname(PREVIEW), exist_ok=True)
    print("rendering 256 (SSAA 4x) ...")
    sizes = [16, 24, 32, 48, 64, 128, 256]
    pngs = []
    for s in sizes:
        img = render(s)
        png = encode_png(s, img)
        pngs.append((s, png))
        print(f"  {s}px -> {len(png)} B")
    ico = build_ico(pngs)
    ico_path = os.path.join(OUT_DIR, "RockCore.ico")
    with open(ico_path, "wb") as f:
        f.write(ico)
    print(f"ICO written: {ico_path} ({len(ico)} B)")
    with open(PREVIEW, "wb") as f:
        f.write(pngs[-1][1])
    print(f"preview PNG: {PREVIEW}")

if __name__ == "__main__":
    main()
