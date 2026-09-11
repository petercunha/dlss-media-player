using System;using System.IO;using System.Threading;using System.Threading.Tasks;
class LiveEnhancementTests{
 static int Main(string[] a){try{Test(a).GetAwaiter().GetResult();return 0;}catch(Exception ex){Console.WriteLine(ex);return 1;}}
 static async Task Test(string[] a){
  var e=new Engine(a[0]){LiveEnhancement=int.Parse(a[2]),LiveRtxMode=int.Parse(a[3]),LiveTarget=1};bool vsr=false,error=false;
  e.Log=l=>{Console.WriteLine(l);if(l.Contains("RTX VSR requested: 640x360 -> 1920x1080"))vsr=true;if(l.Contains("VSR filter failed")||l.Contains("error converting")||l.Contains("[media-rtx] ERROR"))error=true;};
  using(var stop=new CancellationTokenSource(TimeSpan.FromSeconds(45)))if(await e.Play(a[1],false,1080,stop.Token)!=0)throw new Exception("Player exited with an error");
  string feed=File.ReadAllText(Path.Combine(e.Root,"dlss5-feed.log")),reshade=File.ReadAllText(Path.Combine(e.Root,"ReShade.log"));
  if(error||!feed.Contains("live enhancement: DLSS bypassed")||reshade.Contains("inline feature 18 evaluation succeeded"))throw new Exception("DLSS bypass / RTX verification failed");
  if(e.LiveEnhancement==4&&!vsr)throw new Exception("VSR was not requested");
  if(e.LiveRtxMode==2&&!feed.Contains("processed non-DLSS frame 1; HDR=1"))throw new Exception("HDR missing");
  Console.WriteLine("PASS: mode "+e.LiveEnhancement+", HDR "+e.LiveRtxMode+", no neural evaluations");
 }
}
