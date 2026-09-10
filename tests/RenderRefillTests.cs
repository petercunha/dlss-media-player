using System;using System.IO;using System.Linq;using System.Net;using System.Net.Sockets;using System.Reflection;using System.Threading;using System.Threading.Tasks;using System.Collections.Concurrent;
// Force a producer stall independently of the GPU. The production HTTP consumer
// and MPV cache settings must pause playback and resume on completed output.
class RenderRefillTests {
 static int Main(string[] args){try{Test(args).GetAwaiter().GetResult();return 0;}catch(Exception ex){Console.WriteLine(ex);return 1;}}
 static async Task Test(string[] a){
  var e=new Engine(a[0]);string folder=Path.GetFullPath(a[1]);Directory.CreateDirectory(folder);
  if(await e.Run(Path.Combine(e.Root,"tools","ffmpeg.exe"),new[]{"-hide_banner","-loglevel","error","-i",a[2],"-c","copy","-f","segment","-segment_time","4",Path.Combine(folder,"part-%03d.ts")},CancellationToken.None,Console.WriteLine)!=0)throw new Exception("Fixture segmentation failed");
  var files=Directory.GetFiles(folder,"part-*.ts").OrderBy(x=>x).ToArray();if(files.Length<3)throw new Exception("Need at least 12 seconds of fixture");
  string lua=Path.Combine(folder,"observe.lua");File.WriteAllText(lua,"mp.observe_property('paused-for-cache','bool',function(_,v) mp.msg.warn('REFILL_STATE '..tostring(v)..' AT '..tostring(mp.get_property_number('time-pos',0))) end)");
  Type chunk=typeof(Engine).GetNestedType("RenderChunk",BindingFlags.NonPublic),collection=typeof(BlockingCollection<>).MakeGenericType(chunk);
  object queue=Activator.CreateInstance(collection,new object[]{2});var add=collection.GetMethod("Add",new[]{chunk});
  var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;
  bool paused=false,resumed=false;
  using(var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(50))){
   var serve=Task.Run(()=>typeof(Engine).GetMethod("ServeRendered",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{listener,queue,cancel.Token}));
   var produce=Task.Run(async()=>{for(int i=0;i<files.Length;i++){if(i==2)await Task.Delay(16000,cancel.Token);object item=Activator.CreateInstance(chunk,true);chunk.GetField("Path").SetValue(item,files[i]);add.Invoke(queue,new[]{item});}collection.GetMethod("CompleteAdding").Invoke(queue,null);});
   int code=await e.Run(Path.Combine(e.Root,"tools","plain-player","mpv.exe"),new[]{"--idle=no","--keep-open=no","--cache=yes","--cache-pause=yes","--cache-pause-initial=yes","--cache-pause-wait=5","--cache-secs=9","--demuxer-readahead-secs=9","--demuxer-seekable-cache=no","--force-seekable=no","--ytdl=no","--script="+lua,"--","http://127.0.0.1:"+port+"/enhanced.ts"},cancel.Token,line=>{
    Console.WriteLine(line);int pos=line.IndexOf(" AT ");double time;
    if(pos>=0&&double.TryParse(line.Substring(pos+4),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out time)&&time>1){if(line.Contains("REFILL_STATE true"))paused=true;if(paused&&line.Contains("REFILL_STATE false"))resumed=true;}
   });
   await produce;await serve;listener.Stop();((IDisposable)queue).Dispose();
   if(code!=0||!paused||!resumed)throw new Exception("Refill behavior missing: pause="+paused+" resume="+resumed);
  }
  Console.WriteLine("PASS: completed-output stall automatically paused and resumed playback.");
 }
}
