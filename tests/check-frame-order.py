from pathlib import Path
import sys,subprocess,json,numpy as np
r=Path(sys.argv[1]).resolve();p=Path(sys.argv[2]);ff=r/'tools/ffmpeg.exe';probe=r/'tools/ffprobe.exe'
data=subprocess.check_output([str(ff),'-v','error','-i',str(p),'-map','0:v:0','-fps_mode','passthrough','-f','rawvideo','-pix_fmt','rgb24','pipe:1'])
frames=np.frombuffer(data,dtype=np.uint8).reshape(-1,360,640,3)
ids=[]
for f in frames:
    # Decode the bright marker cores, tolerating neural ghosting in dark cells.
    ids.append(sum((int(f[18:35,32+b*64:48+b*64].mean()>200)<<b) for b in range(9)))
assert ids==list(range(300)),[(i,n) for i,n in enumerate(ids) if i!=n][:20]
meta=json.loads(subprocess.check_output([str(probe),'-v','error','-select_streams','v:0','-show_frames','-show_entries','frame=best_effort_timestamp_time','-of','json',str(p)]))
pts=np.array([float(x['best_effort_timestamp_time']) for x in meta['frames']]);delta=np.diff(pts)
assert len(pts)==300 and np.max(abs(delta-1/30))<.00002,(len(pts),delta.min(),delta.max())
print(f'PASS: all {len(ids)} pixel frame IDs in exact source order; no duplicates/skips; PTS steps {delta.min():.6f}..{delta.max():.6f}s across boundaries')
