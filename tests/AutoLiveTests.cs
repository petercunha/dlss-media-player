using System;using System.Threading;
class AutoLiveTests {
 static int Main(string[] a){try{
  using(var stop=new CancellationTokenSource(TimeSpan.FromMinutes(2))){
   bool routed=false,smoothing=false,hdr=false,download=false;
   var e=new Engine(a[0]){LiveTarget=0,LiveRtxMode=2,SmoothPlayback=true};
   e.Log=line=>{Console.WriteLine(line);if(line.Contains("Automatically selected Streamlink"))routed=true;if(line.Contains("Playback motion:")&&line.Contains("interpolation=yes")&&line.Contains("display-sync-active=yes"))smoothing=true;if(line.Contains("processed neural frame 1; HDR=1"))hdr=true;if(line.Contains("downloads the source first")||line.StartsWith("[download]"))download=true;if(routed&&smoothing&&hdr)stop.Cancel();};
   try{e.Play(a[1],true,720,stop.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){}
   if(!routed||!smoothing||!hdr||download)throw new Exception("Auto live verification failed: route="+routed+" smoothing="+smoothing+" HDR="+hdr+" download="+download);
   Console.WriteLine("PASS: automatic live routing, active display smoothing, HDR, no full download, cancellation");
  }return 0;
 }catch(Exception ex){Console.WriteLine(ex);return 1;}}
}
