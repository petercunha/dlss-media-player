using System;using System.Collections.Generic;
sealed partial class Engine {
    public IEnumerable<string> LiveArguments(){
        var args=new List<string>();
        if(LiveTarget==4){args.Add("--fullscreen=yes");args.Add("--screen="+LiveScreen);args.Add("--fs-screen="+LiveScreen);Log("Live output: display resolution, fullscreen. Press F to return to a window.");}
        else if(LiveTarget>0){int height=new[]{0,1080,1440,2160}[LiveTarget];args.Add("--autofit="+(height*16/9)+"x"+height);Log("Live output: fit within "+height+"p; aspect ratio preserved.");}
        if(LiveRtxMode>0){
            args.AddRange(new[]{"--d3d11-output-format=rgb10_a2","--d3d11-output-csp=srgb","--target-trc=srgb","--target-prim=bt.709"});
            Log("Live chain: DLSS → "+((LiveRtxMode&1)!=0?"RTX VSR ":"")+((LiveRtxMode&2)!=0?"RTX Video HDR ":"")+"→ display. HDR activates only on a Windows HDR display.");
        }
        return args;
    }
}
