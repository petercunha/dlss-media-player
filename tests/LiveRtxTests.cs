using System;using System.IO;using System.Threading;using System.Threading.Tasks;
class LiveRtxTests{
 static int Main(string[] a){try{Test(a).GetAwaiter().GetResult();return 0;}catch(Exception ex){Console.WriteLine(ex);return 1;}}
 static async Task Test(string[] a){
  var e=new Engine(a[0]);e.LiveRtxMode=3;e.LiveTarget=int.Parse(a[2]);e.Log=Console.WriteLine;e.SetWorkResolution(50);
  using(var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(40))){try{
   if(a[1].StartsWith("https")){await e.PlayStreamlink(a[1],720,cancel.Token);throw new Exception("Stream ended before test cancellation");}
   else{if(await e.Play(a[1],false,720,cancel.Token)!=0)throw new Exception("Player failed");}
  }catch(OperationCanceledException){Console.WriteLine("Cancelled test playback");}}
  string log=File.ReadAllText(Path.Combine(a[0],"dlss5-feed.log")),neural=File.ReadAllText(Path.Combine(a[0],"ReShade.log"));
  if(!log.Contains("processed neural frame")||!log.Contains("HDR=1")||!neural.Contains("inline feature 18 evaluation succeeded"))throw new Exception("Missing verification");
  if(log.Contains("[media-rtx] ERROR"))throw new Exception("RTX error");
  Console.WriteLine("PASS: DLSS -> VSR/HDR live GPU pipeline and shutdown");
 }
}
