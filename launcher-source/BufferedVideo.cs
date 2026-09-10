using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// Producer/consumer playback: MPV receives only completed neural frames.
// Enhanced chunks live on disk; the source cache is not the render buffer.
sealed partial class Engine {
    const int RenderChunkSeconds=4;
    sealed class RenderChunk {public string Path;public double Duration;}
    public async Task<int> PlayBuffered(string source,int quality,CancellationToken caller,string streamlinkQuality=null){
        if(streamlinkQuality==null&&IsTwitchUrl(source)){Log("Automatically selected Streamlink for Twitch.");return await PlayStreamlink(source,quality,caller);}
        using(var gate=new Semaphore(1,1,"Local\\DLSSMediaOfflineExport")){
            if(!gate.WaitOne(0))throw new Exception("Another neural render is running. Stop it before starting buffered playback.");
            try{return await BufferedCore(source,quality,caller,streamlinkQuality);}finally{gate.Release();}
        }
    }
    async Task<int> BufferedCore(string source,int quality,CancellationToken caller,string streamlinkQuality){
        if(streamlinkQuality==null&&IsUrl(source)){
            Log("Buffered URL playback downloads the source first. For a livestream, choose Streamlink live.");
            source=await Download(source,quality,caller);
        }
        string parent=Path.Combine(Root,"Cache","playback");Directory.CreateDirectory(parent);
        string job=Path.Combine(parent,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(job);
        int seconds=Math.Max(1,Math.Min(60,LiveBufferSeconds));
        using(var stop=CancellationTokenSource.CreateLinkedTokenSource(caller))
        using(var raw=new BlockingCollection<string>(2))
        using(var ready=new BlockingCollection<RenderChunk>(Math.Max(2,(seconds+3)/4+1))){
            var token=stop.Token;var prefilled=new TaskCompletionSource<bool>((TaskCreationOptions)64);
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start(1);
            int port=((IPEndPoint)listener.LocalEndpoint).Port;
            Exception failure=null;object errorLock=new object();
            Action<Exception> fail=ex=>{if(token.IsCancellationRequested)return;lock(errorLock){if(failure==null)failure=ex;}prefilled.TrySetCanceled();stop.Cancel();};
            Task capture=null,render=null,serve=null,watch=null;Exception caught=null;int code=0;
            try{
                Log("Render buffer: preparing "+seconds+" seconds of completed DLSS frames in Cache/playback.");
                SyncNeuralSettings();
                capture=Task.Run(async()=>{try{
                    if(streamlinkQuality==null)await CaptureFile(source,job,raw,token);
                    else await CaptureStreamlink(source,streamlinkQuality,job,raw,token);
                }catch(Exception ex){fail(ex);}finally{raw.CompleteAdding();}});
                render=Task.Run(async()=>{try{using(var session=new BufferedNeuralSession(this,token)){
                    double offset=0,initial=0;int index=0;
                    foreach(string chunk in raw.GetConsumingEnumerable(token)){
                        var rendered=await RenderBufferedChunk(chunk,job,index++,offset,session,token);
                        offset+=rendered.Duration;ready.Add(rendered,token);initial+=rendered.Duration;
                        if(initial>=seconds)prefilled.TrySetResult(true);
                        File.Delete(chunk);
                    }
                    if(initial==0)throw new Exception("The source ended without producing any video.");
                    prefilled.TrySetResult(true);}
                }catch(Exception ex){fail(ex);}finally{ready.CompleteAdding();}});
                watch=Task.Run(async()=>{try{while(!token.IsCancellationRequested){
                    await Task.Delay(1500,token);
                    long bytes=Directory.EnumerateFiles(job,"*",SearchOption.AllDirectories).Sum(p=>{try{return new FileInfo(p).Length;}catch(FileNotFoundException){return 0L;}});
                    if(bytes>4L*1024*1024*1024||new DriveInfo(Path.GetPathRoot(job)).AvailableFreeSpace<1024L*1024*1024)
                        throw new Exception("Render buffer reached its disk limit (4 GB, or less than 1 GB free). Reduce processing load.");
                }}catch(Exception ex){fail(ex);}});
                serve=Task.Run(()=>{try{ServeRendered(listener,ready,token);}catch(Exception ex){fail(ex);}});
                using(token.Register(()=>prefilled.TrySetCanceled()))await prefilled.Task;
                token.ThrowIfCancellationRequested();BufferedPlayback=true;
                var args=new List<string>{"--idle=no","--keep-open=no","--ytdl=no","--input-terminal=no","--force-window=immediate","--title=DLSS 5 - Render buffer","--msg-level=all=warn,cplayer=info","--cache=yes","--cache-pause=yes","--cache-pause-initial=yes","--cache-pause-wait="+seconds,"--cache-secs="+(seconds+4),"--demuxer-readahead-secs="+(seconds+4),"--demuxer-max-bytes=256MiB","--demuxer-seekable-cache=no","--force-seekable=no","--video-sync="+(SmoothPlayback?"display-resample":"audio"),"--interpolation="+(SmoothPlayback?"yes":"no")};
                args.RemoveAll(x=>x.StartsWith("--video-sync=")||x.StartsWith("--interpolation="));args.AddRange(MotionArguments());
                args.AddRange(LiveArguments());args.Add("--");args.Add("http://127.0.0.1:"+port+"/enhanced.ts");
                Log("Render buffer ready. MPV pauses and refills from enhanced output if rendering falls behind.");
                code=await Run(Path.Combine(Root,"mpv.exe"),args,token,Log);
            }catch(Exception ex){caught=ex;}
                stop.Cancel();listener.Stop();BufferedPlayback=false;
                foreach(var task in new[]{capture,render,serve,watch})if(task!=null)try{await task;}catch{}
                try{RemoveJob(job,parent);}catch(IOException){Log("Temporary playback files remain in Cache/playback.");}
            if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            if(caught!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(caught).Throw();
            return code;
        }
    }
    async Task CaptureFile(string source,string job,BlockingCollection<string> raw,CancellationToken token){
        var info=await Probe(source,token);int index=0;
        for(double start=0;start<info.Duration-.001;){
            double take=info.Duration-start<RenderChunkSeconds*2?info.Duration-start:RenderChunkSeconds;
            token.ThrowIfCancellationRequested();string path=Path.Combine(job,"source-"+(index++).ToString("D6")+".mp4");
            await Ffmpeg(new[]{"-ss",Num(start),"-i",source,"-t",Num(take),"-map","0:v:0","-map","0:a:0?","-vf","scale=trunc(iw/2)*2:trunc(ih/2)*2","-c:v","h264_nvenc","-preset","p1","-cq","16","-b:v","0","-pix_fmt","yuv420p","-c:a","aac",path},token);
            await Task.Run(()=>raw.Add(path,token),token);
            start+=take;
        }
    }
    async Task CaptureStreamlink(string source,string selected,string job,BlockingCollection<string> raw,CancellationToken token){
        using(var captureStop=CancellationTokenSource.CreateLinkedTokenSource(token)){
        string list=Path.Combine(job,"captured.csv");
        // Streamlink substitutes playerinput with its local HTTP endpoint.
        // Arguments are passed directly to processes; no shell is involved.
        var ff=new[]{"-hide_banner","-nostdin","-loglevel","warning","-y","-i","{playerinput}","-map","0:v:0","-map","0:a:0?","-c","copy","-f","segment","-segment_time","4","-reset_timestamps","1","-segment_list",list,"-segment_list_type","csv",Path.Combine(job,"source-%06d.ts")};
        var args=StreamlinkCommon();args.AddRange(new[]{"--loglevel","warning","--player",Tool("ffmpeg"),"--player-args",string.Join(" ",ff.Select(Quote)),"--player-http","--player-no-close","--player-verbose","--retry-open","2","--stream-timeout","60","--url="+source,"--default-stream="+selected});
        Task<int> process=Run(Path.Combine(Root,"tools","streamlink","bin","streamlink.exe"),args,captureStop.Token,Log);
        var sent=new HashSet<string>(StringComparer.OrdinalIgnoreCase);Exception captureFailure=null;
        try{
            while(true){
                token.ThrowIfCancellationRequested();bool ended=process.IsCompleted;
                if(File.Exists(list)){
                    string contents;using(var f=new FileStream(list,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var r=new StreamReader(f))contents=r.ReadToEnd();
                    foreach(string line in contents.Split('\n').Take(contents.Count(c=>c=='\n'))){
                        string name=line.Split(',')[0].Trim('"','\r');
                        if(!System.Text.RegularExpressions.Regex.IsMatch(name,@"^source-\d{6}\.ts$"))throw new Exception("Unexpected capture segment name.");
                        if(sent.Add(name)){string path=Path.Combine(job,name);await Task.Run(()=>raw.Add(path,token),token);}
                    }
                }
                if(ended){if(await process!=0)throw new Exception("Streamlink capture failed; see the activity log.");break;}
                await Task.Delay(250,token);
            }
        }catch(Exception ex){captureFailure=ex;}
        captureStop.Cancel();try{await process;}catch(OperationCanceledException){}
        if(captureFailure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(captureFailure).Throw();
        }
    }
    async Task<RenderChunk> RenderBufferedChunk(string source,string job,int index,double offset,BufferedNeuralSession session,CancellationToken token){
        var timer=Stopwatch.StartNew();var info=await Probe(source,token);
        int boundW=info.Width,boundH=info.Height;
        if(LiveTarget==4){boundW=LiveDisplayWidth;boundH=LiveDisplayHeight;}
        else if(LiveTarget>0){boundH=new[]{0,1080,1440,2160}[LiveTarget];boundW=boundH*16/9;}
        double scale=Math.Min(boundW/(double)info.Width,boundH/(double)info.Height);
        int width=Math.Max(2,(int)(info.Width*scale)/2*2),height=Math.Max(2,(int)(info.Height*scale)/2*2);
        int workW=Math.Max(2,width*LiveWorkPercent/100/2*2),workH=Math.Max(2,height*LiveWorkPercent/100/2*2);
        string normalized=Path.Combine(job,"render-source.mp4"),neural=Path.Combine(job,"neural.mp4"),result=Path.Combine(job,"enhanced-"+index.ToString("D6")+".ts");
        var color=new StringBuilder();await Run(Tool("ffprobe"),new[]{"-v","error","-select_streams","v:0","-show_entries","stream=color_transfer","-of","default=nw=1:nk=1",source},token,l=>color.Append(l));
        if(color.ToString().Contains("smpte2084")||color.ToString().Contains("arib-std-b67"))throw new Exception("Buffered enhancement needs SDR input. Use normal playback for an HDR source.");
        string input=source;
        if(info.Width!=workW||info.Height!=workH||info.Rotated){
            await Ffmpeg(new[]{"-i",source,"-an","-vf","scale="+workW+":"+workH+":flags=lanczos","-c:v","h264_nvenc","-preset","p1","-cq","16","-b:v","0","-pix_fmt","yuv420p",normalized},token);input=normalized;
        }
        if(File.Exists(neural))File.Delete(neural);
        int code=await session.Render(new[]{input,neural,workW.ToString(),workH.ToString(),Num(info.Fps),Num(info.Duration)});
        if(code!=0||!File.Exists(neural))throw new Exception("Buffered DLSS rendering failed to verify neural output.");
        // Use the verified CFR frame clock. Container duration includes AAC
        // priming/tail padding and must not accumulate at every chunk boundary.
        double videoDuration=session.LastFrameCount/info.Fps;
        var mux=new List<string>{"-i",neural,"-i",source,"-map","0:v:0","-map","1:a:0?"};
        if(workW!=width||workH!=height)mux.AddRange(new[]{"-vf","scale="+width+":"+height+":flags=lanczos"});
        mux.AddRange(new[]{"-c:v","h264_nvenc","-preset","p1","-cq","18","-b:v","0","-pix_fmt","yuv420p","-g",Math.Max(1,(int)Math.Round(info.Fps)).ToString(),"-bf","0","-fps_mode","passthrough","-c:a","aac","-b:a","192k","-af","aresample=async=1:first_pts=0,apad","-t",Num(videoDuration),"-avoid_negative_ts","disabled","-output_ts_offset",Num(offset),"-mpegts_copyts","1","-mpegts_flags","+initial_discontinuity","-muxdelay","0","-f","mpegts",result});
        await Ffmpeg(mux,token);var check=await Probe(result,token);
        if(Math.Abs(check.Duration-info.Duration)>Math.Max(.3,3/info.Fps))throw new Exception("Buffered segment timing verification failed.");
        File.Delete(neural);if(File.Exists(normalized))File.Delete(normalized);
        Log("Render buffer · chunk "+(index+1)+" · "+Num(videoDuration)+"s ready · "+(videoDuration/timer.Elapsed.TotalSeconds).ToString("0.00",Invariant)+"× realtime · "+workW+"×"+workH+" DLSS");
        return new RenderChunk{Path=result,Duration=videoDuration};
    }
    // Keep the neural device/model alive between chunks. Recreating it per chunk
    // otherwise dominates short-buffer throughput. Requests are strictly serial.
    sealed class BufferedNeuralSession:IDisposable {
        public long LastFrameCount {get;private set;}
        readonly Process process;readonly CancellationToken token;readonly CancellationTokenRegistration cancellation;
        readonly object sync=new object();TaskCompletionSource<int> pending;
        public BufferedNeuralSession(Engine engine,CancellationToken stop){
            token=stop;token.ThrowIfCancellationRequested();
            var info=new ProcessStartInfo(Path.Combine(engine.Root,"tools","neural-export","NeuralExport.exe"),"--batch"){
                WorkingDirectory=engine.Root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
            info.EnvironmentVariables["PATH"]=Path.Combine(engine.Root,"tools")+";"+Environment.GetEnvironmentVariable("PATH");
            info.EnvironmentVariables["DLSS_MEDIA_RTX"]="0";info.EnvironmentVariables["DLSS_MEDIA_PRERENDERED"]="0";
            process=new Process{StartInfo=info,EnableRaisingEvents=true};
            process.OutputDataReceived+=(s,e)=>{if(e.Data==null)return;
                if(e.Data.StartsWith("DLSS_BATCH_DONE ")){
                    string[] fields=e.Data.Split(' ');int code;long frames,verified;
                    frames=0;bool valid=fields.Length==4&&int.TryParse(fields[1],out code)&&code==0&&long.TryParse(fields[2],out frames)&&long.TryParse(fields[3],out verified)&&frames>0&&frames==verified;
                    LastFrameCount=valid?frames:0;
                    lock(sync){if(pending!=null)pending.TrySetResult(valid?0:1);}
                    engine.Log(e.Data);
                }else if(!e.Data.StartsWith("DLSS frames"))engine.Log(e.Data);
            };
            process.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)engine.Log(e.Data);};
            process.Exited+=(s,e)=>{lock(sync){if(pending!=null)pending.TrySetException(new Exception("Buffered neural worker exited unexpectedly."));}};
            process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();
            cancellation=token.Register(()=>{lock(sync){if(pending!=null)pending.TrySetCanceled();}Kill();});
        }
        public async Task<int> Render(string[] fields){
            token.ThrowIfCancellationRequested();
            if(fields.Any(x=>x.IndexOfAny(new[]{'\r','\n','\t'})>=0))throw new Exception("Invalid buffered render path.");
            TaskCompletionSource<int> request;
            lock(sync){if(process.HasExited)throw new Exception("Buffered neural worker is not running.");pending=request=new TaskCompletionSource<int>((TaskCreationOptions)64);}
            await process.StandardInput.WriteLineAsync(string.Join("\t",fields));await process.StandardInput.FlushAsync();
            return await request.Task;
        }
        void Kill(){try{if(!process.HasExited)using(var killer=Process.Start(new ProcessStartInfo("taskkill.exe","/PID "+process.Id+" /T /F"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})){killer.WaitForExit();}}catch(InvalidOperationException){}}
        public void Dispose(){cancellation.Dispose();Kill();process.WaitForExit();process.Dispose();}
    }
    static void ServeRendered(TcpListener listener,BlockingCollection<RenderChunk> ready,CancellationToken token){
        using(token.Register(()=>listener.Stop())){
            while(true){
                token.ThrowIfCancellationRequested();
                using(var client=listener.AcceptTcpClient())using(token.Register(()=>client.Close()))using(var stream=client.GetStream()){
                    client.SendBufferSize=64*1024;client.ReceiveTimeout=10000;
                    var header=new StringBuilder();int b;
                    while(header.Length<16384&&(b=stream.ReadByte())>=0){header.Append((char)b);if(header.ToString().EndsWith("\r\n\r\n"))break;}
                    string request=header.ToString();bool head=request.StartsWith("HEAD /enhanced.ts ");
                    if(!head&&!request.StartsWith("GET /enhanced.ts "))throw new IOException("Unexpected local player request.");
                    byte[] response=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: video/mp2t\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n");stream.Write(response,0,response.Length);
                    if(head)continue;
                    try{foreach(var chunk in ready.GetConsumingEnumerable(token)){
                        using(var file=File.OpenRead(chunk.Path)){byte[] block=new byte[65536];int count;while((count=file.Read(block,0,block.Length))>0){token.ThrowIfCancellationRequested();stream.Write(block,0,count);}}
                        File.Delete(chunk.Path);
                    }}catch(IOException){return;} // MPV closed by the user.
                    return;
                }
            }
        }
    }
}
