using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

static class Program {
    [STAThread] static int Main(string[] args) {
        if (args.Length > 0 && args[0] == "--self-test") {
            if (Engine.Quote("a b") != "\"a b\"" || Engine.Quote("a\"b") != "\"a\\\"b\"" || Engine.IsUrl("file:///C:/test") || !Engine.IsUrl("https://example.com/watch?v=a&b=c")) return 1;
            return File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"mpv.exe")) ? 0 : 2;
        }
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new Launcher(args.FirstOrDefault())); return 0;
    }
}

sealed partial class Engine {
    public int LiveRtxMode,LiveTarget,LiveScreen;
    public readonly string Root;
    public Engine(string root = null) { Root = root ?? AppDomain.CurrentDomain.BaseDirectory; }
    public string Downloads { get { return Path.Combine(Root,"Downloads"); } }
    public Action<string> Log = delegate {};
    public static bool IsUrl(string value) { Uri u; return Uri.TryCreate(value, UriKind.Absolute, out u) && (u.Scheme == "http" || u.Scheme == "https"); }
    public static string Quote(string s) {
        var b = new StringBuilder("\""); int slashes = 0;
        foreach (char c in s) { if(c == '\\') { slashes++; continue; } if(c == '"') b.Append('\\',slashes*2+1); else b.Append('\\',slashes); b.Append(c); slashes=0; }
        return b.Append('\\',slashes*2).Append('"').ToString();
    }
    string Tool(string name) { string p=Path.Combine(Root,"tools",name+".exe"); if(!File.Exists(p)) throw new FileNotFoundException("Missing helper: "+name,p); return p; }
    public string Format(int quality) { return quality==0 ? "bestvideo+bestaudio/best" : "bestvideo[height<="+quality+"]+bestaudio/best[height<="+quality+"]/best"; }
    public async Task<int> Run(string exe, IEnumerable<string> args, CancellationToken token, Action<string> output) {
        token.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo(exe,string.Join(" ",args.Select(Quote))) { WorkingDirectory=Root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8 };
        info.EnvironmentVariables["PATH"] = Path.Combine(Root,"tools")+";"+Environment.GetEnvironmentVariable("PATH");
        // Child-scoped setting also reaches MPV launched by Streamlink.
        info.EnvironmentVariables["DLSS_MEDIA_RTX"] = PreparedPlayback?"0":(LiveRtxMode&2).ToString();
        info.EnvironmentVariables["DLSS_MEDIA_GEOMETRY"] = "";
        info.EnvironmentVariables["DLSS_MEDIA_PRERENDERED"] = BufferedPlayback&&string.Equals(Path.GetFileName(exe),"mpv.exe",StringComparison.OrdinalIgnoreCase)?"1":"0";
        using(var p = new Process { StartInfo=info }) {
            p.OutputDataReceived += (s,e)=> { if(e.Data!=null) output(e.Data); };
            p.ErrorDataReceived += (s,e)=> { if(e.Data!=null) output(e.Data); };
            p.Start(); p.BeginOutputReadLine(); p.BeginErrorReadLine();
            using(token.Register(()=> { try { if(!p.HasExited) { using(var killer=Process.Start(new ProcessStartInfo("taskkill.exe","/PID "+p.Id+" /T /F") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true })) { killer.WaitForExit(); } } } catch(InvalidOperationException) {} })) {
                await Task.Run(()=>p.WaitForExit()); token.ThrowIfCancellationRequested(); return p.ExitCode;
            }
        }
    }
    public async Task<int> Play(string input, bool url, int quality, CancellationToken token) {
        if(LiveBufferSeconds>0&&!PreparedPlayback)return await PlayBuffered(input,quality,token);
        string player=PreparedPlayback?Path.Combine(Root,"tools","plain-player","mpv.exe"):Path.Combine(Root,"mpv.exe"); if(!File.Exists(player)) throw new FileNotFoundException("mpv.exe is missing.");
        var args=new List<string> { "--idle=no","--keep-open=no","--force-window=immediate","--input-terminal=no","--msg-level=all=warn,cplayer=info", "--title=DLSS 5 Player" };
        args.Add("--video-sync="+(SmoothPlayback?"display-resample":"audio"));
        args.Add("--interpolation="+(SmoothPlayback?"yes":"no"));
        if(SmoothPlayback) args.Add("--tscale=oversample");
        if(!PreparedPlayback)args.AddRange(LiveArguments(url));
        if(url) { args.Add("--ytdl=yes"); args.Add("--script-opts=ytdl_hook-ytdl_path="+Tool("yt-dlp")); args.Add("--ytdl-format="+Format(quality)); args.Add("--ytdl-raw-options=no-playlist=,ignore-config=,js-runtimes=deno"); args.Add("--cache=yes"); }
        else args.Add("--ytdl=no");
        args.Add("--"); args.Add(input);
        Log(url?"Opening URL with yt-dlp. The player may take a moment to resolve the stream…":"Opening video…");
        return await Run(player,args,token,Log);
    }
    public async Task<string> Download(string url,int quality,CancellationToken token) {
        Directory.CreateDirectory(Downloads); string result=null;
        var args=new [] { "--ignore-config","--no-playlist","--newline","--progress","--no-colors","--windows-filenames","--trim-filenames","120","--socket-timeout","25","--retries","3","--js-runtimes","deno:"+Tool("deno"),"--ffmpeg-location",Path.GetDirectoryName(Tool("ffmpeg")),"-f",Format(quality),"--merge-output-format","mkv","-P",Downloads,"-o",DownloadTemplate(url,quality),"--print","after_move:DLSS_FILE:%(filepath)s","--",url };
        int code=await Run(Tool("yt-dlp"),args,token,line=> { if(line.StartsWith("DLSS_FILE:")) result=line.Substring(10); else Log(line); });
        if(code!=0 || string.IsNullOrEmpty(result) || !File.Exists(result)) throw new Exception("Download failed. See the activity log for the site's error. Some sites require sign-in or do not allow downloading.");
        string full=Path.GetFullPath(result); if(!full.StartsWith(Path.GetFullPath(Downloads)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new Exception("Unexpected download output path.");
        Log("Saved: "+Path.GetFileName(full)); return full;
    }
    public static string DownloadTemplate(string url,int quality) {
        // Generic extractors can report a signed URL or Content-Disposition value as
        // the media ID. Never place that unbounded value in a Windows filename.
        // Quality is part of the key so a later higher-quality request is not mistaken
        // for a previously downloaded lower-quality file.
        string identity=url+"\nquality="+quality;
        using(var hash=SHA256.Create()) {
            string key=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-","").Substring(0,20).ToLowerInvariant();
            return "%(title).60B ["+key+"].%(ext)s";
        }
    }
}
