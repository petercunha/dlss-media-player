using System;using System.Collections.Generic;
sealed partial class Engine {
    public int LiveEnhancement,LiveDisplayWidth=3840,LiveDisplayHeight=2160;
    public IEnumerable<string> MotionArguments(){
        var args=new List<string>(SmoothPlayback?new[]{"--video-sync=display-resample","--interpolation=yes","--tscale=oversample"}:new[]{"--video-sync=audio","--interpolation=no"});
        string script=System.IO.Path.Combine(Root,"config","playback-state.lua");if(System.IO.File.Exists(script))args.Add("--script="+script);return args;
    }
    public IEnumerable<string> LiveArguments(bool networkStream=false){
        var args=new List<string>();
        if(LiveTarget==4){args.Add("--fullscreen=yes");args.Add("--screen="+LiveScreen);args.Add("--fs-screen="+LiveScreen);Log("Live output: display resolution, fullscreen. Press F to return to a window.");}
        else if(LiveTarget>0){int height=new[]{0,1080,1440,2160}[LiveTarget];args.Add("--autofit="+(height*16/9)+"x"+height);}
        if((LiveRtxMode&2)!=0){
            args.AddRange(new[]{"--d3d11-output-format=rgb10_a2","--d3d11-output-csp=srgb","--target-trc=srgb","--target-prim=bt.709"});
            Log(LiveEnhancement>=3?"Live playback: DLSS disabled; RTX HDR enabled.":"Live chain: MPV scaling -> DLSS -> RTX Video HDR. HDR requires Windows HDR enabled.");
        }
        if(LiveEnhancement==4){
            string script=System.IO.Path.Combine(Root,"config","live-vsr.lua");
            if(!System.IO.File.Exists(script))throw new System.IO.FileNotFoundException("VSR script missing",script);
            int h=LiveTarget>0&&LiveTarget<4?new[]{0,1080,1440,2160}[LiveTarget]:LiveDisplayHeight;
            int w=LiveTarget>0&&LiveTarget<4?h*16/9:LiveDisplayWidth;
            args.Add("--script="+script);args.Add("--script-opts-append=live_vsr-width="+w);args.Add("--script-opts-append=live_vsr-height="+h);
            args.Add("--hwdec=d3d11va");
            Log("RTX VSR selected instead of DLSS; scaling decoded video toward the selected output size.");
        }
        return args;
    }
}
