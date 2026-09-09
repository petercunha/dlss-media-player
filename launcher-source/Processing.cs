using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

sealed class VideoInfo { public int Width,Height; public double Fps,Duration; public bool Rotated; }

sealed partial class Engine {
    public bool SmoothPlayback,PreparedPlayback;
    static readonly CultureInfo Invariant=CultureInfo.InvariantCulture;
    static string Num(double n) { return n.ToString("0.########",Invariant); }
    public void SetWorkResolution(int percent) {
        string p=Path.Combine(Root,"dlss5-feed.cfg");
        var lines=File.Exists(p)?File.ReadAllLines(p).ToList():new List<string>();
        lines.RemoveAll(l=>l.TrimStart().StartsWith("work_resolution=")); lines.Add("work_resolution="+percent); File.WriteAllLines(p,lines);
    }
    public async Task<VideoInfo> Probe(string file,CancellationToken token) {
        var output=new StringBuilder();
        int code=await Run(Tool("ffprobe"),new[]{"-v","error","-select_streams","v:0","-show_entries","stream=width,height,avg_frame_rate:stream_side_data=rotation:format=duration","-of","json",file},token,l=>output.AppendLine(l));
        if(code!=0) throw new Exception("Unable to read video information.");
        var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(output.ToString());
        var streams=(System.Collections.ArrayList)data["streams"]; if(streams.Count==0) throw new Exception("No video track found.");
        var v=(Dictionary<string,object>)streams[0]; string[] rate=((string)v["avg_frame_rate"]).Split('/');
        double fps=double.Parse(rate[0],Invariant)/(rate.Length>1?double.Parse(rate[1],Invariant):1);
        var format=(Dictionary<string,object>)data["format"];
        double duration=double.Parse(Convert.ToString(format["duration"],Invariant),Invariant);
        if(!double.IsNaN(fps)&&!double.IsInfinity(fps)&&fps>0&&duration>0) {
            int width=Convert.ToInt32(v["width"]),height=Convert.ToInt32(v["height"]);bool rotated=false;
            if(v.ContainsKey("side_data_list")) foreach(Dictionary<string,object> item in (System.Collections.ArrayList)v["side_data_list"]) if(item.ContainsKey("rotation")){int rotation=Math.Abs(Convert.ToInt32(item["rotation"]));rotated=rotation%360!=0;if(rotation%180==90){int temp=width;width=height;height=temp;}}
            return new VideoInfo { Width=width,Height=height,Fps=fps,Duration=duration,Rotated=rotated };
        }
        throw new Exception("Offline processing needs a finite-duration video with a valid frame rate. Live streams can use live playback.");
    }
    async Task Ffmpeg(IEnumerable<string> args,CancellationToken token) {
        var list=new List<string>{"-hide_banner","-nostdin","-loglevel","warning","-stats","-y"};list.AddRange(args);
        if(await Run(Tool("ffmpeg"),list,token,Log)!=0)throw new Exception("Video conversion failed; see the activity log.");
    }
    void RemoveJob(string path,string parent) {
        string allowed=Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        string target=Path.GetFullPath(path); if(!target.StartsWith(allowed,StringComparison.OrdinalIgnoreCase) || target==allowed.TrimEnd(Path.DirectorySeparatorChar))throw new Exception("Unexpected work directory.");
        if(Directory.Exists(target))Directory.Delete(target,true);
    }
    public async Task<string> ProcessVideo(string source,string destination,bool rife,int targetHeight,CancellationToken token) {
        using(var gate=new Semaphore(1,1,"Local\\DLSSMediaOfflineExport")) {
            if(!gate.WaitOne(0))throw new Exception("Another offline render is already running. Wait for it to finish or stop it first.");
            try{return await ProcessCore(source,destination,rife,targetHeight,token);}finally{gate.Release();}
        }
    }
    async Task<string> ProcessCore(string source,string destination,bool rife,int targetHeight,CancellationToken token) {
        var info=await Probe(source,token);
        if(File.Exists(destination))throw new Exception("Choose a new output filename; existing exports are never overwritten.");
        string jobs=Path.Combine(Root,"Cache","jobs");Directory.CreateDirectory(jobs);
        string job=Path.Combine(jobs,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(job);
        string partial=Path.Combine(Path.GetDirectoryName(destination),"."+Path.GetFileNameWithoutExtension(destination)+"."+Guid.NewGuid().ToString("N")+".partial.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        try {
            long estimated=(long)(info.Width*(double)info.Height*4*(rife?740:8)+info.Width*(double)info.Height*info.Fps*info.Duration/12);
            if(new DriveInfo(Path.GetPathRoot(job)).AvailableFreeSpace<estimated+512L*1024*1024)throw new Exception("Not enough free disk space for this render. Free some space or use a smaller source.");
            string normalized=Path.Combine(job,"source.mp4"), neural=Path.Combine(job,"neural.mp4");
            Log("1 / 4 · Checking source format…");
            // The native decoder already produces CFR frames while decoding.
            // Only geometry that its raw-video interface cannot represent needs an intermediate.
            // HDR is rejected rather than silently interpreting PQ/HLG as SDR.
            var color=new StringBuilder();await Run(Tool("ffprobe"),new[]{"-v","error","-select_streams","v:0","-show_entries","stream=color_transfer","-of","default=nw=1:nk=1",source},token,l=>color.Append(l));
            if(color.ToString().Contains("smpte2084")||color.ToString().Contains("arib-std-b67"))throw new Exception("HDR export is not supported yet. Use an SDR source; live playback remains available.");
            if(info.Rotated||info.Width%2!=0||info.Height%2!=0){
                Log("Correcting rotation/odd dimensions using NVENC…");
                await Ffmpeg(new[]{"-i",source,"-map","0:v:0","-an","-vf","fps="+Num(info.Fps)+",scale=trunc(iw/2)*2:trunc(ih/2)*2","-c:v","h264_nvenc","-preset","p4","-cq","16","-b:v","0","-pix_fmt","yuv420p",normalized},token);
                info=await Probe(normalized,token);
            }else{normalized=source;Log("Skipping source re-encode; frame timing is handled during DLSS decoding.");}
            Log("2 / 4 · Rendering DLSS neural frames offline…");
            string native=Path.Combine(Root,"tools","neural-export","NeuralExport.exe");
            SyncNeuralSettings();
            int code=await Run(native,new[]{normalized,neural,info.Width.ToString(),info.Height.ToString(),Num(info.Fps),Num(info.Duration)},token,Log);
            if(code!=0||!File.Exists(neural))throw new Exception("DLSS export could not verify its neural frames. See tools/neural-export/ReShade.log. No export was published.");
            string rendered=neural;
            if(rife){Log("3 / 4 · RIFE 2× frame generation…");rendered=await Rife(neural,info,job,token);}
            else Log("3 / 4 · Keeping original frame rate.");
            Log("4 / 4 · Saving MP4 with source audio…");
            var encode=new List<string>{"-i",rendered,"-i",source,"-map","0:v:0","-map","1:a:0?"};
            if(targetHeight>0&&targetHeight>info.Height)encode.AddRange(new[]{"-vf","scale=-2:"+targetHeight+":flags=lanczos"});
            if(rife&&targetHeight<=info.Height)encode.AddRange(new[]{"-c:v","copy"});
            else encode.AddRange(new[]{"-c:v","h264_nvenc","-preset","p4","-cq","18","-b:v","0","-pix_fmt","yuv420p"});
            encode.AddRange(new[]{"-c:a","aac","-b:a","192k","-t",Num(info.Duration),"-movflags","+faststart",partial});
            await Ffmpeg(encode,token);
            var check=await Probe(partial,token);
            double expected=info.Fps*(rife?2:1);
            if(Math.Abs(check.Duration-info.Duration)>Math.Max(.2,2/info.Fps)||Math.Abs(check.Fps-expected)>.1)throw new Exception("Export timing verification failed. No export was published.");
            token.ThrowIfCancellationRequested();File.Move(partial,destination);Log("Saved: "+destination);return destination;
        }finally {try{if(File.Exists(partial))File.Delete(partial);RemoveJob(job,jobs);}catch(IOException){Log("Some temporary files remain in Cache/jobs.");}catch(UnauthorizedAccessException){Log("Some temporary files remain in Cache/jobs.");}}
    }
    void SyncNeuralSettings() {
        string live=Path.Combine(Root,"ReShade.ini");var keys=new List<string>();bool section=false;
        if(File.Exists(live))foreach(string line in File.ReadAllLines(live)){string s=line.Trim();if(s.StartsWith("[")){section=s=="[RenoDX.DLSS5]";continue;}if(section&&s.Contains("=")&&!s.StartsWith("EnableHooks=")&&!s.StartsWith("NeuralUplift=")&&!s.StartsWith("NREnableUpscaling="))keys.Add(s);}
        string ini="[GENERAL]\nNoReloadOnInit=1\n[OVERLAY]\nTutorialProgress=4\n[RenoDX.DLSS5]\nEnableHooks=2\nNeuralUplift=1\nNREnableUpscaling=0\n"+string.Join("\n",keys)+"\n";
        File.WriteAllText(Path.Combine(Root,"tools","neural-export","ReShade.ini"),ini,new UTF8Encoding(false));
    }
    async Task<string> Rife(string source,VideoInfo info,string job,CancellationToken token) {
        int count=0;var counts=new StringBuilder();
        // Our native intermediate has one encoded video packet per frame.
        // Count packets without decoding the entire video again.
        int probe=await Run(Tool("ffprobe"),new[]{"-v","error","-select_streams","v:0","-count_packets","-show_entries","stream=nb_read_packets","-of","default=nw=1:nk=1",source},token,l=>counts.Append(l));
        if(probe!=0||!int.TryParse(counts.ToString().Trim(),out count)||count<2)throw new Exception("RIFE needs at least two decoded frames.");
        string segments=Path.Combine(job,"segments");Directory.CreateDirectory(segments);var paths=new List<string>();
        var callerToken=token;
        using(var pipeline=CancellationTokenSource.CreateLinkedTokenSource(token)) {
        token=pipeline.Token;Task extraction=null,encoding=null;Exception failure=null;
        try {
        extraction=ExtractRifeChunk(source,info,job,0,Math.Min(121,count),token);
        for(int start=0;start<count;start+=120) {
            token.ThrowIfCancellationRequested();int take=Math.Min(120,count-start),read=Math.Min(take+1,count-start);
            string chunk=Path.Combine(job,"chunk"+start),input=Path.Combine(chunk,"in"),output=Path.Combine(chunk,"out");Directory.CreateDirectory(output);
            Log("RIFE frames "+start+" / "+count+" · 2× output");
            await extraction;
            // A bounded three-stage pipeline: next extraction, current Vulkan
            // inference and previous NVENC encoding may run concurrently.
            extraction=start+120<count?ExtractRifeChunk(source,info,job,start+120,Math.Min(121,count-start-120),token):Task.FromResult(0);
            string exe=Path.Combine(Root,"tools","rife","rife-ncnn-vulkan.exe");
            if(read>1) {
                // Device 0 was verified as this PC's RTX 5070 Ti. Limit 4K
                // concurrency to avoid exhausting the 16 GB VRAM workset.
                string workers=info.Width*(long)info.Height>2560L*1440?"4:2:4":"4:4:4";
                int code=await Run(exe,new[]{"-i",input,"-o",output,"-m",Path.Combine(Root,"tools","rife","rife-v4.6"),"-n",(read*2).ToString(),"-g","0","-j",workers,"-f","%08d.png"},token,l=>{if(!l.EndsWith("%"))Log(l);});
                if(code!=0)throw new Exception("RIFE frame generation failed.");
            }else{string frame=Directory.GetFiles(input,"*.png").Single();File.Copy(frame,Path.Combine(output,"00000001.png"));File.Copy(frame,Path.Combine(output,"00000002.png"));}
            var frames=Directory.GetFiles(output,"*.png").OrderBy(p=>p,StringComparer.Ordinal).ToArray();
            if(frames.Length<take*2)throw new Exception("RIFE returned fewer frames than expected.");
            string segment=Path.Combine(segments,paths.Count.ToString("D6")+".mp4");
            string first=Path.GetFileNameWithoutExtension(frames[0]);int firstIndex=int.Parse(first,Invariant);
            if(encoding!=null)await encoding;
            encoding=EncodeRifeChunk(output,segment,firstIndex,take,info.Fps,chunk,job,token);
            paths.Add(segment);
        }
        if(encoding!=null)await encoding;
        }catch(Exception ex){failure=ex;pipeline.Cancel();}
        if(failure!=null){
            // Helpers must have exited before the parent removes job files.
            if(extraction!=null)try{await extraction;}catch{}
            if(encoding!=null)try{await encoding;}catch{}
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
        }
        token=callerToken;
        string list=Path.Combine(segments,"concat.txt");File.WriteAllLines(list,paths.Select(p=>"file '"+Path.GetFileName(p)+"'"),new UTF8Encoding(false));
        string result=Path.Combine(job,"rife.mp4");await Ffmpeg(new[]{"-f","concat","-safe","1","-i",list,"-c","copy",result},token);return result;
    }
    async Task ExtractRifeChunk(string source,VideoInfo info,string job,int start,int read,CancellationToken token){
        string input=Path.Combine(job,"chunk"+start,"in");Directory.CreateDirectory(input);
        await Ffmpeg(new[]{"-ss",Num(start/info.Fps),"-i",source,"-frames:v",read.ToString(),"-fps_mode","passthrough","-compression_level","0","-threads","4",Path.Combine(input,"%08d.png")},token);
    }
    async Task EncodeRifeChunk(string output,string segment,int firstIndex,int take,double fps,string chunk,string job,CancellationToken token){
        await Ffmpeg(new[]{"-framerate",Num(fps*2),"-start_number",firstIndex.ToString(),"-i",Path.Combine(output,"%08d.png"),"-frames:v",(take*2).ToString(),"-c:v","h264_nvenc","-preset","p4","-cq","16","-b:v","0","-pix_fmt","yuv420p",segment},token);
        RemoveJob(chunk,job);
    }
}
