using System;using System.IO;using System.Threading;using System.Threading.Tasks;
class ExportRegressionTests {
 static int Main(string[] a){try{Test(a).GetAwaiter().GetResult();return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
 static async Task Test(string[] a){
  var engine=new Engine(a[0]);engine.Log=Console.WriteLine;
  string source=a[1],dest=a[2];bool rife=a[3]=="rife";
  using(var cancel=new CancellationTokenSource(TimeSpan.FromMinutes(3))){
   if(a[3]=="cancel"){
    engine.Log=l=>{Console.WriteLine(l);if(l.StartsWith("RIFE frames 0"))cancel.CancelAfter(700);};
    try{await engine.ProcessVideo(source,dest,true,0,cancel.Token);throw new Exception("Cancellation was ignored");}catch(OperationCanceledException){}
    if(File.Exists(dest)||Directory.GetDirectories(Path.Combine(a[0],"Cache","jobs")).Length!=0)throw new Exception("Cancellation left files");
    Console.WriteLine("PASS: cancellation during RIFE removes queued frames and publishes nothing");return;
   }
   var before=await engine.Probe(source,cancel.Token);
   await engine.ProcessVideo(source,dest,rife,0,cancel.Token);
   var after=await engine.Probe(dest,cancel.Token);
   if(after.Width!=before.Width-before.Width%2||after.Height!=before.Height-before.Height%2||Math.Abs(after.Fps-before.Fps*(rife?2:1))>.01||Math.Abs(after.Duration-before.Duration)>.15)throw new Exception("Geometry or timing mismatch");
   Console.WriteLine("PASS: geometry, frame rate and duration: "+after.Width+"x"+after.Height+" "+after.Fps+"fps "+after.Duration+"s");
  }
 }
}
