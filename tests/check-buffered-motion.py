"""Check temporal trails in the order-fixture output; also run check-frame-order.py."""
from pathlib import Path
import subprocess, sys
import numpy as np

root, video = Path(sys.argv[1]), Path(sys.argv[2])
data = subprocess.check_output([str(root / 'tools/ffmpeg.exe'), '-v', 'error', '-i', str(video),
    '-map', '0:v:0', '-fps_mode', 'passthrough', '-f', 'rawvideo', '-pix_fmt', 'rgb24', 'pipe:1'])
frames = np.frombuffer(data, np.uint8).reshape(-1, 360, 640, 3)
assert len(frames) == 300, len(frames)
y, x = np.mgrid[:360, :640]
dark, trails = [], []
for i in range(10, 300):
    for bit in range(9):
        if not i & (1 << bit):
            dark.append(float(frames[i, 18:35, 32+bit*64:48+bit*64].mean()))
    left = 40 + round(200 * (1 + np.sin(i / 25)))
    outside = (y > 80) & (y < 240) & ((x < left-3) | (x > left+83) | (y < 127) | (y > 213))
    f = frames[i].astype(float)
    red = (f[:,:,0] > f[:,:,1]*1.6) & (f[:,:,0] > f[:,:,2]*1.5) & (f[:,:,0] > 100)
    # A BGRA/RGBA channel swap must not pass merely by removing all red trails.
    assert np.count_nonzero(red[140:200, left+20:left+60]) > 1500, ('red object lost or channels swapped', i)
    trails.append(np.count_nonzero(outside & red))
# Source dark cells are around 15. The old temporal path reached ~170;
# independent-frame rendering measured <16. Allow normal encoder variation.
assert max(dark) < 30, ('ghosted dark marker', max(dark))
assert max(trails) <= 5, ('moving object trail', max(trails))
print(f'PASS: maximum dark marker {max(dark):.2f}/255; maximum red trail {max(trails)} pixels')
