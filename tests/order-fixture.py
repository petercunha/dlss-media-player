from pathlib import Path
import sys, subprocess, numpy as np
from PIL import Image, ImageDraw
r=Path(sys.argv[1]).resolve(); w=Path(sys.argv[2]).resolve(); w.mkdir(parents=True, exist_ok=True)
ff=r/'tools/ffmpeg.exe'
p=subprocess.Popen([str(ff),'-hide_banner','-loglevel','error','-y','-f','rawvideo','-pixel_format','rgb24','-video_size','640x360','-framerate','30','-i','pipe:0','-f','lavfi','-i','sine=frequency=440:sample_rate=48000','-t','10','-c:v','h264_nvenc','-preset','p1','-cq','16','-g','60','-bf','0','-pix_fmt','yuv420p','-c:a','aac',str(w/'order-source.mp4')],stdin=subprocess.PIPE)
for f in range(300):
    im=Image.new('RGB',(640,360),(75,80,85));d=ImageDraw.Draw(im)
    for x in range(0,640,32):d.line((x,70,x,359),fill=(120,125,130))
    for y in range(70,360,32):d.line((0,y,639,y),fill=(120,125,130))
    x=40+round(200*(1+np.sin(f/25)))
    d.ellipse((x,130,x+80,210),fill=(200,60,30));d.rectangle((500,250,600,320),fill=(40,180,70))
    for b in range(9):
        v=235 if f&(1<<b) else 15;d.rectangle((24+b*64,10,56+b*64,42),fill=(v,v,v))
    p.stdin.write(im.tobytes())
p.stdin.close();assert p.wait()==0
print('Created 300-frame numbered motion fixture')
