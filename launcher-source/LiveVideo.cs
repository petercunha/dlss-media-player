using System;using System.Collections.Generic;
sealed partial class Engine {
    public int LiveBufferSeconds,LiveWorkPercent=100,LiveDisplayWidth=3840,LiveDisplayHeight=2160;
    public bool BufferedPlayback;
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
            Log(BufferedPlayback?"Playback: completed DLSS frames -> RTX Video HDR; live DLSS bypassed.":"Live chain: MPV scaling -> DLSS -> RTX Video HDR. HDR requires Windows HDR enabled.");
        }
        return args;
    }
}
