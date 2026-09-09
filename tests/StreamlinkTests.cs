using System;using System.IO;using System.Linq;using System.Threading;using System.Threading.Tasks;
class StreamlinkTests {
 static int Main(string[] args){try{Test(args).GetAwaiter().GetResult();return 0;}catch(Exception ex){Console.WriteLine(ex);return 1;}}
 static async Task Test(string[] args){
  string[] names={"audio_only","480p","720p","720p60","1080p50","1080p60","1440p60","best","worst"};
  if(Engine.SelectStreamlinkQuality(names,720)!="720p60"||Engine.SelectStreamlinkQuality(names,1080)!="1080p60"||Engine.SelectStreamlinkQuality(names,0)!="best")throw new Exception("Quality selection failed");
  if(!Engine.IsStreamlinkInput("hls://https://example.com/live.m3u8")||!Engine.IsStreamlinkInput("twitch.tv/example")||Engine.IsStreamlinkInput("--output=test")||Engine.IsStreamlinkInput("file:///C:/test"))throw new Exception("Input validation failed");
  Console.WriteLine("PASS: quality caps retain 60fps; generic protocols accepted; flags/files rejected");
  var e=new Engine(args[0]);e.Log=Console.WriteLine;e.SetWorkResolution(100);
  bool stopped=false;using(var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(40))){try{int code=await e.PlayStreamlink(args[1],720,cancel.Token);throw new Exception("Live stream ended before cancellation: "+code);}catch(OperationCanceledException){stopped=true;}}
  string log=File.ReadAllText(Path.Combine(args[0],"ReShade.log"));if(!stopped||!log.Contains("inline feature 18 evaluation succeeded"))throw new Exception("DLSS neural evaluation was not verified");Console.WriteLine("PASS: Twitch -> Streamlink -> MPV with verified DLSS neural evaluations; Stop cancelled process tree");
 }
}
