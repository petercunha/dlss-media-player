using System;using System.Diagnostics;using System.Threading;using System.Threading.Tasks;
// Integration: player root, a regular YouTube video longer than 30 seconds.
class YouTubeVodTests {
 static int Main(string[] a){try{Test(a).GetAwaiter().GetResult();return 0;}catch(Exception ex){Console.WriteLine(ex);return 1;}}
 static async Task Test(string[] a){
  bool enhanced=false,routed=false;
  var e=new Engine(a[0]){LiveRtxMode=2,LiveTarget=4};
  e.Log=line=>{Console.WriteLine(line);if(line.Contains("processed neural frame 1; HDR=1"))enhanced=true;if(line.Contains("Automatically selected Streamlink"))routed=true;};
  using(var stop=new CancellationTokenSource(TimeSpan.FromSeconds(25))){
   var watch=Stopwatch.StartNew();bool cancelled=false;
   try{int code=await e.Play(a[1],true,1080,stop.Token);throw new Exception("Video exited before test cancellation: "+code+" after "+watch.Elapsed.TotalSeconds+"s");}
   catch(OperationCanceledException){cancelled=true;}
   if(!cancelled||watch.Elapsed.TotalSeconds<24||!enhanced||routed)throw new Exception("Missing sustained MPV/yt-dlp DLSS+HDR playback");
  }
  Console.WriteLine("PASS: regular YouTube video streams through MPV/yt-dlp and stays open with DLSS+HDR.");
 }
}
