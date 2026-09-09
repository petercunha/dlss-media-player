using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

sealed partial class Engine {
    public static bool IsStreamlinkInput(string source) {
        return !string.IsNullOrWhiteSpace(source) && source.Length<=8192 && !source.Any(char.IsControl)
            && !source.StartsWith("-") && !source.StartsWith("file:",StringComparison.OrdinalIgnoreCase)
            && !Path.IsPathRooted(source);
    }
    public static string SelectStreamlinkQuality(IEnumerable<string> names,int quality) {
        var available=names.ToArray();
        if(quality>0) {
            var candidates=available.Select(name=>new {Name=name,Match=Regex.Match(name,@"^(\d+)p(\d*)",RegexOptions.IgnoreCase)})
                .Where(item=>item.Match.Success)
                .Select(item=>new {item.Name,Height=int.Parse(item.Match.Groups[1].Value),Fps=item.Match.Groups[2].Length==0?0:int.Parse(item.Match.Groups[2].Value)})
                .Where(item=>item.Height<=quality)
                .OrderByDescending(item=>item.Height).ThenByDescending(item=>item.Fps).ThenBy(item=>item.Name.Contains("_alt"));
            if(candidates.Any())return candidates.First().Name;
        }
        if(available.Contains("best"))return "best";
        return available.FirstOrDefault(name=>name!="worst"&&name!="audio_only")??available.FirstOrDefault();
    }
    List<string> StreamlinkCommon() {
        var args=new List<string>();
        string config=Path.Combine(Root,"streamlink.conf");
        if(File.Exists(config))args.AddRange(new[]{"--config",config});else args.Add("--no-config");
        args.AddRange(new[]{"--http-timeout","25","--ffmpeg-ffmpeg",Tool("ffmpeg")});return args;
    }
    public async Task<int> PlayStreamlink(string source,int quality,CancellationToken token) {
        if(!IsStreamlinkInput(source))throw new Exception("Enter a Streamlink-supported URL or protocol URL.");
        string exe=Path.Combine(Root,"tools","streamlink","bin","streamlink.exe");
        if(!File.Exists(exe))throw new FileNotFoundException("The bundled Streamlink runtime is missing.");
        Log("Streamlink · checking available streams…");
        var discovery=StreamlinkCommon();discovery.AddRange(new[]{"--loglevel","error","--json","--url="+source});
        var json=new StringBuilder();int code=await Run(exe,discovery,token,line=>{lock(json){if(json.Length<8*1024*1024)json.AppendLine(line);}});
        var serializer=new JavaScriptSerializer {MaxJsonLength=8*1024*1024};Dictionary<string,object> info=null;
        try{info=serializer.Deserialize<Dictionary<string,object>>(json.ToString());}catch(ArgumentException){}
        if(code!=0||info==null||!info.ContainsKey("streams")) {
            string detail=info!=null&&info.ContainsKey("error")?Convert.ToString(info["error"]):"The stream may be offline, unavailable, or require site-specific configuration.";
            throw new Exception("Streamlink could not open this URL: "+detail);
        }
        var streams=info["streams"] as Dictionary<string,object>;
        if(streams==null||streams.Count==0)throw new Exception("Streamlink found no playable streams. The channel may be offline.");
        string selected=SelectStreamlinkQuality(streams.Keys,quality);
        if(string.IsNullOrEmpty(selected))throw new Exception("Streamlink found no playable stream quality.");
        Log("Streamlink · selected "+selected+" · opening DLSS player");
        if(quality>0&&selected=="best")Log("No named resolution matched the quality cap; using the site's best available stream.");
        // Streamlink owns both the local HTTP transport and the player child process.
        // No stream URL/header is handed to a shell, saved to a file, or re-extracted by yt-dlp.
        string playerArgs="--idle=no --keep-open=no --ytdl=no --force-window=immediate --input-terminal=no --video-sync=audio --interpolation=no --cache=yes --demuxer-max-bytes=128MiB --title=\"DLSS 5 - Streamlink\" --msg-level=all=warn,cplayer=info "+string.Join(" ",LiveArguments().Select(Quote));
        var args=StreamlinkCommon();args.AddRange(new[]{"--loglevel","info","--player",Path.Combine(Root,"mpv.exe"),"--player-args",playerArgs,"--player-http","--player-verbose","--retry-open","2","--stream-timeout","60","--url="+source,"--default-stream="+selected});
        return await Run(exe,args,token,Log);
    }
}
