using System;using System.IO;using System.Linq;using System.Threading;
class LivePerformanceTests {
 static int Main(string[] a){try{
  using(var stop=new CancellationTokenSource(TimeSpan.FromMinutes(3))){
   int chunks=0;bool routed=false,smoothing=false,hdr=false;
   var e=new Engine(a[0]){LiveBufferSeconds=5,LiveTarget=int.Parse(a[2]),LiveRtxMode=2,SmoothPlayback=true};
   string cache=Path.Combine(e.Root,"Cache","playback");var previous=Directory.Exists(cache)?Directory.GetDirectories(cache):new string[0];
   if(a.Length>3)e.LiveWorkPercent=int.Parse(a[3]);
   e.Log=line=>{Console.WriteLine(line);if(line.Contains("Automatically selected Streamlink"))routed=true;if(line.Contains("Playback motion:")&&line.Contains("interpolation=yes")&&line.Contains("display-sync-active=yes"))smoothing=true;if(line.Contains("processed neural frame 1; HDR=1"))hdr=true;if(line.StartsWith("Render buffer · chunk"))chunks++;if(routed&&smoothing&&hdr&&chunks>=4)stop.Cancel();};
   try{e.Play(a[1],true,1080,stop.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){}
   if(!routed||!smoothing||!hdr||chunks<4)throw new Exception("Incomplete live performance sample: chunks="+chunks);
   if(Directory.Exists(cache)&&Directory.GetDirectories(cache).Except(previous).Any())throw new Exception("Live cancellation left a new cache directory.");
   Console.WriteLine("PASS: four live chunks, automatic routing, active smoothing, HDR and cancellation");
  }return 0;
 }catch(Exception ex){Console.WriteLine(ex);return 1;}}
}
