using System;using System.Collections.Generic;
sealed partial class Engine {
    public bool LiveSourceResolution;
    public IEnumerable<string> LiveArguments(){
        var args=new List<string>();
        if(LiveSourceResolution && (LiveRtxMode&1)!=0){
            if(!System.IO.File.Exists(System.IO.Path.Combine(Root,"config","source-resolution.lua")))throw new System.IO.FileNotFoundException("Source-resolution script is missing. Restore config/source-resolution.lua.");
            args.AddRange(new[]{"--video-unscaled=downscale-big","--keepaspect=yes","--video-zoom=0","--video-pan-x=0","--video-pan-y=0","--script="+System.IO.Path.Combine(Root,"config","source-resolution.lua")});
            Log("Live chain: source -> VSR cleanup up to 1920x1080 -> DLSS Super Resolution + neural enhancement -> optional HDR. Sources above 1080p skip VSR. Aspect ratio is preserved; small windows may downscale to fit.");
        }
        if(LiveTarget==4){args.Add("--fullscreen=yes");args.Add("--screen="+LiveScreen);args.Add("--fs-screen="+LiveScreen);Log("Live output: display resolution, fullscreen. Press F to return to a window.");}
        else if(LiveTarget>0){int height=new[]{0,1080,1440,2160}[LiveTarget];args.Add("--autofit="+(height*16/9)+"x"+height);Log("Live output: fit within "+height+"p; aspect ratio preserved.");}
        if(LiveRtxMode>0){
            args.AddRange(new[]{"--d3d11-output-format="+((LiveRtxMode&2)!=0?"rgb10_a2":"rgba8"),"--d3d11-output-csp=srgb","--target-trc=srgb","--target-prim=bt.709"});
            if(!LiveSourceResolution)Log("Live chain: DLSS → "+((LiveRtxMode&1)!=0?"RTX VSR ":"")+((LiveRtxMode&2)!=0?"RTX Video HDR ":"")+"→ display. HDR activates only on a Windows HDR display.");
        }
        return args;
    }
}
