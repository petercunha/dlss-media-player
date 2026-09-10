using System;using System.IO;using System.Linq;using System.Threading;using System.Threading.Tasks;using System.Collections.Generic;
class BufferedPlaybackTests {
 static int Main(string[] args){try{Run(args).GetAwaiter().GetResult();return 0;}catch(Exception ex){Console.WriteLine(ex);return 1;}}
 static async Task Run(string[] args){
  var e=new Engine(args[0]){LiveBufferSeconds=5,LiveTarget=0,LiveRtxMode=2,SmoothPlayback=true};
  if(args.Length>4)e.LiveTarget=int.Parse(args[4]);
  if(args.Length>5)e.LiveWorkPercent=int.Parse(args[5]);
  string jobs=Path.Combine(e.Root,"Cache","playback");var previous=Directory.Exists(jobs)?Directory.GetDirectories(jobs):new string[0];
  int chunks=0,frames=0;bool bypass=false,hdr=false,ready=false,audio=false;var lines=new List<string>();
  e.Log=line=>{Console.WriteLine(line);lock(lines){lines.Add(line);if(line.StartsWith("DLSS_BATCH_DONE 0 "))frames+=int.Parse(line.Split(' ')[2]);if(line.Contains("Audio  --aid"))audio=true;if(line.StartsWith("Render buffer · chunk"))chunks++;if(line.Contains("prerendered playback: live DLSS bypassed"))bypass=true;if(line.Contains("HDR=1"))hdr=true;if(line.StartsWith("Render buffer ready"))ready=true;}};
  using(var stop=new CancellationTokenSource(TimeSpan.FromMinutes(4))){
   int code=args.Length>2&&args[2]=="streamlink"?await e.PlayStreamlink(args[1],720,stop.Token):await e.Play(args[1],false,720,stop.Token);
   if(code!=0||chunks<2||!ready||!bypass||!hdr)throw new Exception("Missing buffer/HDR evidence: code="+code+" chunks="+chunks+" ready="+ready+" bypass="+bypass+" HDR="+hdr);
   if(args.Length>3&&(frames!=int.Parse(args[3])||!audio))throw new Exception("Source truncated or audio missing: frames="+frames+" audio="+audio);
  }
  if(Directory.Exists(jobs)&&Directory.GetDirectories(jobs).Except(previous).Any())throw new Exception("Playback cache was not cleaned.");
  using(var stop=new CancellationTokenSource(150)){try{if(args.Length>2&&args[2]=="streamlink")await e.PlayStreamlink(args[1],720,stop.Token);else await e.Play(args[1],false,720,stop.Token);throw new Exception("Cancellation was ignored.");}catch(OperationCanceledException){}}
  if(Directory.Exists(jobs)&&Directory.GetDirectories(jobs).Except(previous).Any())throw new Exception("Cancelled playback cache was not cleaned.");
  Console.WriteLine("PASS: completed neural chunks, HDR-only MPV, finite EOF, cancellation and cache cleanup.");
 }
}
