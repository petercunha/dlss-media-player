using System;using System.IO;using System.IO.Pipes;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;using System.Web.Script.Serialization;
class BufferedSeekTests {
 static int Main(string[] a){try{Test(a).GetAwaiter().GetResult();return 0;}catch(Exception ex){Console.WriteLine(ex);return 1;}}
 static int id;static StreamReader reader;static StreamWriter writer;static JavaScriptSerializer json=new JavaScriptSerializer();
 static async Task<object> Command(params object[] command){int request=++id;await writer.WriteLineAsync(json.Serialize(new{command=command,request_id=request}));
  while(true){var line=await reader.ReadLineAsync();if(line==null)throw new Exception("IPC closed");var response=json.Deserialize<Dictionary<string,object>>(line);object rid;
   if(response.TryGetValue("request_id",out rid)&&Convert.ToInt32(rid)==request){if(Convert.ToString(response["error"])!="success")throw new Exception(line);object data;return response.TryGetValue("data",out data)?data:null;}
  }
 }
 static async Task Test(string[] a){
  string pipe="dlss-seek-test-"+Guid.NewGuid().ToString("N");
  var e=new Engine(a[0]){LiveBufferSeconds=1,LiveTarget=0,SmoothPlayback=true,BufferedIpcPath="\\\\.\\pipe\\"+pipe};e.Log=Console.WriteLine;
  int originalJobs=Directory.GetDirectories(Path.Combine(e.Root,"Cache","playback")).Length;
  using(var stop=new CancellationTokenSource(TimeSpan.FromMinutes(3))){
   var playback=e.Play(a[1],Engine.IsUrl(a[1]),720,stop.Token);Exception failure=null;
   try{using(var connection=new NamedPipeClientStream(".",pipe,PipeDirection.InOut,PipeOptions.Asynchronous)){
    await Task.Run(()=>connection.Connect(90000));reader=new StreamReader(connection);writer=new StreamWriter(connection){AutoFlush=true};
    double duration=0;for(int i=0;i<100;i++){try{duration=Convert.ToDouble(await Command("get_property","duration"));if(duration>0)break;}catch{}await Task.Delay(100);}
    if(Math.Abs(duration-60)>.1)throw new Exception("Missing full duration: "+duration);
    if(!Convert.ToBoolean(await Command("get_property","seekable")))throw new Exception("Not seekable");
    await Command("set_property","pause",true);
    foreach(double target in new[]{45.0,2.0,54.0}){
     await Command("seek",target,"absolute+exact");double position=-1;
     bool settled=false;for(int i=0;i<400;i++){await Task.Delay(100);try{position=Convert.ToDouble(await Command("get_property","time-pos"));}catch{}if(Math.Abs(position-target)<.1&&!Convert.ToBoolean(await Command("get_property","seeking"))&&!Convert.ToBoolean(await Command("get_property","paused-for-cache"))){settled=true;break;}}
     if(!settled)throw new Exception("Seek did not finish rendering: "+target);
     if(Math.Abs(position-target)>.1)throw new Exception("Seek failed: "+target+" -> "+position);
     Console.WriteLine("SEEK VERIFIED: "+target+" -> "+position);
     if(a.Length>2)await Command("screenshot-to-file",Path.Combine(Path.GetFullPath(a[2]),"seek-"+target+".png"),"video");
    }
    await Command("quit");if(await playback!=0)throw new Exception("Playback failed");
   }}catch(Exception ex){failure=ex;}
   if(failure!=null){stop.Cancel();try{await playback;}catch{}throw failure;}
  }
  if(Directory.GetDirectories(Path.Combine(e.Root,"Cache","playback")).Length!=originalJobs)throw new Exception("Cache not cleaned");
  Console.WriteLine("PASS: full 60-second timeline; forward/backward uncached seeks and cleanup.");
 }
}
