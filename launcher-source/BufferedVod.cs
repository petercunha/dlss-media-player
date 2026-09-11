using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

sealed partial class Engine {
    internal string BufferedIpcPath {get;set;}
    sealed class BufferedSource {
        public string Video,Audio,VideoHeaders,AudioHeaders;public VideoInfo Info;
        public IEnumerable<string> Options(bool audio=false){
            string path=audio?Audio:Video,headers=audio?AudioHeaders:VideoHeaders;
            if(IsUrl(path)){yield return "-rw_timeout";yield return "25000000";}
            if(!string.IsNullOrEmpty(headers)){yield return "-headers";yield return headers;}
        }
    }
    static string JsonText(Dictionary<string,object> value,string name){object result;return value.TryGetValue(name,out result)?Convert.ToString(result,Invariant):null;}
    static string SourceHeaders(Dictionary<string,object> value){
        object raw;if(!value.TryGetValue("http_headers",out raw))return null;
        var headers=raw as Dictionary<string,object>;if(headers==null)return null;
        var result=new StringBuilder();
        foreach(var h in headers){string v=Convert.ToString(h.Value,Invariant);
            if(h.Key.Any(c=>!char.IsLetterOrDigit(c)&&c!='-')||v.Any(c=>c=='\r'||c=='\n'||c=='\0'))continue;
            result.Append(h.Key).Append(": ").Append(v).Append("\r\n");
        }return result.ToString();
    }
    async Task<BufferedSource> ResolveBufferedSource(string source,int quality,CancellationToken token){
        var resolved=new BufferedSource{Video=source};
        if(!IsUrl(source)){resolved.Info=await Probe(source,token);return resolved;}
        Log("Buffered URL · opening source directly; no full download.");
        try{resolved.Info=await Probe(source,token,resolved.Options());return resolved;}
        catch(OperationCanceledException){throw;}
        catch(Exception){Log("Resolving video and audio stream URLs with yt-dlp…");}
        var json=new StringBuilder();
        int code=await Run(Tool("yt-dlp"),new[]{"--ignore-config","--no-playlist","--skip-download","--dump-single-json","--no-warnings","--socket-timeout","25","--js-runtimes","deno:"+Tool("deno"),"-f",Format(quality),"--",source},token,l=>{lock(json){if(json.Length<16*1024*1024)json.AppendLine(l);}});
        if(code!=0)throw new Exception("Could not resolve a streaming video URL. See the site error, or open a downloaded file.");
        var data=new JavaScriptSerializer{MaxJsonLength=16*1024*1024}.Deserialize<Dictionary<string,object>>(json.ToString());
        object formats;var selected=data.TryGetValue("requested_formats",out formats)?formats as ArrayList:null;
        var video=selected==null?data:selected.Cast<Dictionary<string,object>>().FirstOrDefault(x=>JsonText(x,"vcodec")!="none");
        var audio=selected==null?null:selected.Cast<Dictionary<string,object>>().FirstOrDefault(x=>JsonText(x,"vcodec")=="none"&&JsonText(x,"acodec")!="none");
        if(video==null||!IsUrl(JsonText(video,"url"))||(audio!=null&&!IsUrl(JsonText(audio,"url"))))throw new Exception("This site did not provide an HTTP video stream for buffered playback.");
        resolved.Video=JsonText(video,"url");resolved.VideoHeaders=SourceHeaders(video)??SourceHeaders(data);
        if(audio!=null){resolved.Audio=JsonText(audio,"url");resolved.AudioHeaders=SourceHeaders(audio)??SourceHeaders(data);}
        resolved.Info=await Probe(resolved.Video,token,resolved.Options());return resolved;
    }
    sealed class VodPart {public double Start,Duration;public string Path;public DateTime Used;}
    // The complete VOD manifest supplies duration and seek points before every
    // segment exists. A segment request produces just that interval, using the
    // same retained neural session. HTTP handlers never feed MPV incomplete files.
    sealed class VodRenderer:IDisposable {
        readonly Engine engine;readonly BufferedSource source;readonly string job;
        readonly CancellationToken token;readonly SemaphoreSlim renderGate=new SemaphoreSlim(1);
        readonly BufferedNeuralSession session;public readonly List<VodPart> Parts=new List<VodPart>();
        public VodRenderer(Engine owner,BufferedSource input,string folder,CancellationToken stop){
            engine=owner;source=input;job=folder;token=stop;
            // Round boundaries to the decoder's CFR clock, including NTSC rates.
            double step=Math.Max(1,Math.Round(RenderChunkSeconds*input.Info.Fps))/input.Info.Fps;
            for(double start=0;start<input.Info.Duration-.001;){double take=input.Info.Duration-start<step*2?input.Info.Duration-start:step;Parts.Add(new VodPart{Start=start,Duration=take});start+=take;}
            session=new BufferedNeuralSession(engine,token);
        }
        public string Manifest(){
            var text=new StringBuilder("#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-PLAYLIST-TYPE:VOD\n#EXT-X-MEDIA-SEQUENCE:0\n#EXT-X-TARGETDURATION:");
            text.Append((int)Math.Ceiling(Parts.Max(p=>p.Duration))).Append('\n');
            foreach(var p in Parts.Select((part,index)=>new{part,index}))text.Append("#EXTINF:").Append(Num(p.part.Duration)).Append(",\npart-").Append(p.index).Append(".ts\n");
            return text.Append("#EXT-X-ENDLIST\n").ToString();
        }
        public async Task<byte[]> Read(int index){
            await renderGate.WaitAsync(token);
            try{
                var part=Parts[index];
                if(part.Path==null||!File.Exists(part.Path)){
                    engine.Log("Render seek · preparing "+Num(part.Start)+"–"+Num(part.Start+part.Duration)+"s of "+Num(source.Info.Duration)+"s");
                    string clip=Path.Combine(job,"source-"+index.ToString("D6")+".mkv");
                    var args=new List<string>();args.AddRange(source.Options());args.AddRange(new[]{"-ss",Num(part.Start),"-i",source.Video});
                    if(source.Audio!=null){args.AddRange(source.Options(true));args.AddRange(new[]{"-ss",Num(part.Start),"-i",source.Audio});}
                    args.AddRange(new[]{"-t",Num(part.Duration),"-map","0:v:0","-map",source.Audio==null?"0:a:0?":"1:a:0?","-vf","fps="+Num(source.Info.Fps)+",scale=trunc(iw/2)*2:trunc(ih/2)*2","-frames:v",Math.Max(1,(long)Math.Round(part.Duration*source.Info.Fps)).ToString(),"-c:v","h264_nvenc","-preset","p1","-tune","lossless","-pix_fmt","yuv420p","-c:a","pcm_s16le",clip});
                    await engine.Ffmpeg(args,token);
                    var chunk=await engine.PrepareBufferedChunk(clip,job,index,token);
                    chunk.Info.Fps=source.Info.Fps;chunk.Info.Duration=part.Duration;
                    var timer=System.Diagnostics.Stopwatch.StartNew();
                    if(await session.Render(new[]{chunk.Input,chunk.Neural,chunk.WorkW.ToString(),chunk.WorkH.ToString(),Num(chunk.Info.Fps),Num(chunk.Info.Duration)})!=0)throw new Exception("On-demand neural rendering failed.");
                    // Keep DTS and AAC priming above zero. Negative TS timestamps
                    // wrap at 2^33 and break HLS demuxer state after a backward seek.
                    chunk.NeuralSeconds=timer.Elapsed.TotalSeconds;chunk.Duration=session.LastFrameCount/chunk.Info.Fps;chunk.Offset=part.Start+1;
                    part.Path=(await engine.PackageBufferedChunk(chunk,token)).Path;
                }
                part.Used=DateTime.UtcNow;
                // Read before eviction and release the renderer before slow HTTP
                // writes. A seek can disconnect while the current chunk finishes.
                byte[] result=File.ReadAllBytes(part.Path);
                long bytes=Parts.Where(p=>p.Path!=null&&File.Exists(p.Path)).Sum(p=>new FileInfo(p.Path).Length);
                foreach(var old in Parts.Where(p=>p!=part&&p.Path!=null).OrderBy(p=>p.Used)){
                    if(bytes<=512L*1024*1024)break;if(File.Exists(old.Path)){bytes-=new FileInfo(old.Path).Length;File.Delete(old.Path);}old.Path=null;
                }
                if(new DriveInfo(Path.GetPathRoot(job)).AvailableFreeSpace<1024L*1024*1024)throw new IOException("Render buffer needs at least 1 GB free disk space.");
                return result;
            }catch(IOException ex){throw new InvalidOperationException("Could not read or save the rendered segment.",ex);}
            finally{renderGate.Release();}
        }
        public void Dispose(){session.Dispose();renderGate.Dispose();}
    }
    async Task<int> BufferedVod(string input,int quality,CancellationToken caller){
        var source=await ResolveBufferedSource(input,quality,caller);
        string parent=Path.Combine(Root,"Cache","playback"),job=Path.Combine(parent,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(job);
        using(var stop=CancellationTokenSource.CreateLinkedTokenSource(caller)){
            var listener=new TcpListener(IPAddress.Loopback,0);var clients=new List<Task>();Task accept=null;Exception failure=null,caught=null;VodRenderer renderer=null;int result=0;
            try{
                SyncNeuralSettings();
                renderer=new VodRenderer(this,source,job,stop.Token);
                    listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;
                    accept=Task.Run(async()=>{
                        try{while(!stop.IsCancellationRequested){
                            var client=await listener.AcceptTcpClientAsync();
                            var task=Task.Run(async()=>{using(client)using(stop.Token.Register(()=>client.Close())){
                                try{await ServeVod(client,renderer,stop.Token);}
                                catch(IOException){}catch(SocketException){}catch(ObjectDisposedException){}
                                catch(OperationCanceledException){}
                                catch(Exception ex){failure=ex;stop.Cancel();}
                            }});
                            clients.RemoveAll(t=>t.IsCompleted);clients.Add(task);
                        }}catch(ObjectDisposedException){}catch(SocketException){}
                    });
                        double filled=0;int seconds=Math.Max(1,Math.Min(60,LiveBufferSeconds));
                        for(int i=0;i<renderer.Parts.Count&&filled<seconds;i++){await renderer.Read(i);filled+=renderer.Parts[i].Duration;}
                        BufferedPlayback=true;
                        var args=new List<string>{"--idle=no","--keep-open=no","--ytdl=no","--input-terminal=no","--force-window=immediate","--title=DLSS 5 - Buffered video","--msg-level=all=warn,cplayer=info","--cache=yes","--cache-pause=yes","--cache-pause-initial=yes","--cache-pause-wait="+seconds,"--cache-secs="+(seconds+4),"--demuxer-readahead-secs="+(seconds+4),"--demuxer-max-bytes=256MiB","--demuxer-lavf-o=seg_max_retry=0","--network-timeout=180"};
                        args.AddRange(MotionArguments());args.AddRange(LiveArguments());
                        if(!string.IsNullOrEmpty(BufferedIpcPath))args.Add("--input-ipc-server="+BufferedIpcPath);
                        args.Add("--");args.Add("http://127.0.0.1:"+port+"/video.m3u8");
                        Log("Render buffer ready · full timeline "+Num(source.Info.Duration)+"s · seek anywhere; uncached sections render on demand.");
                        result=await Run(Path.Combine(Root,"mpv.exe"),args,stop.Token,Log);
            }catch(Exception ex){caught=ex;}
            stop.Cancel();listener.Stop();if(accept!=null)try{await accept;}catch(Exception ex){if(caught==null)caught=ex;}
            try{await Task.WhenAll(clients);}catch(Exception ex){if(caught==null)caught=ex;}
            if(renderer!=null)renderer.Dispose();BufferedPlayback=false;
            for(int attempt=0;;attempt++){try{RemoveJob(job,parent);break;}catch(IOException){if(attempt==9){Log("Temporary playback files remain in Cache/playback.");break;}}await Task.Delay(200);}
            if(failure!=null)throw failure;if(caught!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(caught).Throw();return result;
        }
    }
    static async Task ServeVod(TcpClient client,VodRenderer renderer,CancellationToken token){
        client.ReceiveTimeout=10000;client.SendTimeout=180000;
        using(var stream=client.GetStream()){
            var header=new StringBuilder();int b;while(header.Length<16384&&(b=stream.ReadByte())>=0){header.Append((char)b);if(header.ToString().EndsWith("\r\n\r\n"))break;}
            var request=header.ToString().Split(' ');if(request.Length<2)return;
            bool head=request[0]=="HEAD";if(request[0]!="GET"&&!head)return;
            string path=request[1].Split('?')[0],type="text/plain";byte[] body;int index;
            if(path=="/video.m3u8"){body=Encoding.UTF8.GetBytes(renderer.Manifest());type="application/vnd.apple.mpegurl";}
            else if(path.StartsWith("/part-")&&path.EndsWith(".ts")&&int.TryParse(path.Substring(6,path.Length-9),out index)&&index>=0&&index<renderer.Parts.Count){body=await renderer.Read(index);type="video/mp2t";}
            else{byte[] missing=Encoding.ASCII.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");await stream.WriteAsync(missing,0,missing.Length,token);return;}
            byte[] response=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: "+type+"\r\nContent-Length: "+body.Length+"\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n");
            await stream.WriteAsync(response,0,response.Length,token);if(!head)await stream.WriteAsync(body,0,body.Length,token);
        }
    }
}
