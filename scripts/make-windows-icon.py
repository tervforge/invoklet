#!/usr/bin/env python3
"""Builds src/main/Invoklet/Assets/icon.ico from icon.svg (needs rsvg-convert).

icon.svg draws its plate over the whole canvas with no margin, which is what Windows wants: it draws every frame
of an icon edge to edge in the taskbar, the title bar and the Start menu. So every frame is the whole canvas;
cropping it would cut the hammer and the grid, which reach close to the plate's edges.
"""
import pathlib
import re
import struct
import subprocess
import sys
import tempfile

SIZES = [16, 24, 32, 48, 64, 128, 256]

assets = pathlib.Path(__file__).resolve().parent.parent / "src/main/Invoklet/Assets"
original = (assets / "icon.svg").read_text(encoding="utf-8")
if 'viewBox="0 0 1024 1024"' not in original:
    sys.exit("icon.svg no longer has the 1024 x 1024 canvas this script expects")

with tempfile.TemporaryDirectory() as tmp:
    frames = []
    for size in SIZES:
        png = pathlib.Path(tmp) / f"{size}.png"
        subprocess.run(["rsvg-convert", "-w", str(size), "-h", str(size), str(assets / "icon.svg"), "-o", str(png)], check=True)
        frames.append(png.read_bytes())

offset = 6 + 16 * len(SIZES)
entries, body = b"", b""
for size, frame in zip(SIZES, frames):
    entries += struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(frame), offset + len(body))
    body += frame
(assets / "icon.ico").write_bytes(struct.pack("<HHH", 0, 1, len(SIZES)) + entries + body)
