using System;using System.IO;using System.Linq;using System.Threading;
class BufferedOrderTests {
 static int Main(string[] a){try{
  var e=new Engine(a[0]){LiveBufferSeconds=5,LiveTarget=0,LiveRtxMode=0,SmoothPlayback=true};
  string dest=Path.GetFullPath(a[2]);Directory.CreateDirectory(dest);int chunks=0;
  e.Log=line=>{Console.WriteLine(line);if(line.StartsWith("Render buffer · chunk")){
   var path=Directory.GetFiles(Path.Combine(e.Root,"Cache","playback"),"enhanced-*.ts",SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).First();
   File.Copy(path,Path.Combine(dest,"part-"+(chunks++).ToString("D3")+".ts"));
  }};
  using(var stop=new CancellationTokenSource(TimeSpan.FromMinutes(3)))if(e.Play(a[1],false,720,stop.Token).GetAwaiter().GetResult()!=0)throw new Exception("Playback failed");
  using(var output=File.Create(Path.Combine(dest,"joined.ts")))foreach(string part in Directory.GetFiles(dest,"part-*.ts").OrderBy(x=>x))using(var input=File.OpenRead(part))input.CopyTo(output);
  if(chunks<2)throw new Exception("No chunk boundary exercised");Console.WriteLine("CAPTURED: "+chunks+" chunks for pixel IDs / PTS verification");return 0;
 }catch(Exception ex){Console.WriteLine(ex);return 1;}}
}
