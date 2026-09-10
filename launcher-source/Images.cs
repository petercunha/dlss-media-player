using System;using System.IO;using System.Net;using System.Linq;using System.Threading;using System.Threading.Tasks;using System.Drawing;
sealed partial class Engine {
 public static bool IsImageFile(string path){return new[]{".png",".jpg",".jpeg",".webp",".bmp",".tif",".tiff",".avif",".gif"}.Contains(Path.GetExtension(path).ToLowerInvariant());}
 async Task DownloadImage(string url,string path,CancellationToken token){
  var request=(HttpWebRequest)WebRequest.Create(url);request.UserAgent="DLSS-Media-Player";
  using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(token)){
   timeout.CancelAfter(TimeSpan.FromSeconds(60));
   using(timeout.Token.Register(()=>request.Abort())){
    try{using(var response=(HttpWebResponse)await request.GetResponseAsync())using(var input=response.GetResponseStream())using(var output=File.Create(path)){
     byte[] buffer=new byte[65536];long total=0;int n;
     while((n=await input.ReadAsync(buffer,0,buffer.Length,timeout.Token))>0){total+=n;if(total>100L*1024*1024)throw new Exception("Image download exceeds 100 MB.");await output.WriteAsync(buffer,0,n,timeout.Token);}
    }}catch(WebException){token.ThrowIfCancellationRequested();throw new Exception("Could not download the image. Use a direct image URL that does not require sign-in.");}
   }
  }
 }
 public async Task<string> ProcessImage(string source,string destination,int height,CancellationToken token){
  if(File.Exists(destination))throw new Exception("Choose a new filename; existing images are never overwritten.");
  using(var gate=new Semaphore(1,1,"Local\\DLSSMediaOfflineExport")){
   if(!gate.WaitOne(0))throw new Exception("Another offline render is already running.");
   string parent=Path.Combine(Root,"Cache","jobs");Directory.CreateDirectory(parent);
   string job=Path.Combine(parent,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(job);
   string partial=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destination)),"."+Guid.NewGuid().ToString("N")+".partial.png");
   try{
    LiveRtxMode=0;
    if(IsUrl(source)){Log("Downloading image…");string local=Path.Combine(job,"download.bin");await DownloadImage(source,local,token);source=local;}
    string normal=Path.Combine(job,"image.png"),padded=Path.Combine(job,"padded.png"),neural=Path.Combine(job,"neural.mp4");
    Log("1 / 4 · Reading image (first frame for animated images)…");
    await Ffmpeg(new[]{"-i",source,"-frames:v","1","-update","1","-pix_fmt","rgba",normal},token);
    int w,h;using(var img=Image.FromFile(normal)){w=img.Width;h=img.Height;}
    if(w<2||h<2||(long)w*h>40000000)throw new Exception("Image dimensions must be at least 2×2 and no more than 40 megapixels.");
    await Ffmpeg(new[]{"-i",normal,"-frames:v","1","-update","1","-vf","pad=ceil(iw/2)*2:ceil(ih/2)*2:0:0:color=black@0","-pix_fmt","rgba",padded},token);
    Log("2 / 4 · Applying DLSS neural enhancement to the image…");SyncNeuralSettings();
    int code=await Run(Path.Combine(Root,"tools","neural-export","NeuralExport.exe"),new[]{padded,neural,((w+1)/2*2).ToString(),((h+1)/2*2).ToString(),"24","0.041666666666666664"},token,Log);
    if(code!=0||!File.Exists(neural))throw new Exception("Image enhancement could not verify the DLSS neural output. No image was saved.");
    Log("3 / 4 · Restoring image dimensions and transparency…");
    string filter="[0:v]crop="+w+":"+h+":0:0:exact=1,format=rgb24[v];[1:v]format=rgba,alphaextract[a];[v][a]alphamerge";
    if(height>h)filter+=",scale=-1:"+height+":flags=lanczos";
    filter+="[out]";Directory.CreateDirectory(Path.GetDirectoryName(partial));
    await Ffmpeg(new[]{"-i",neural,"-i",normal,"-filter_complex",filter,"-map","[out]","-frames:v","1","-update","1","-pix_fmt","rgba",partial},token);
    using(var check=Image.FromFile(partial)){if(check.Height!=(height>h?height:h))throw new Exception("Image output dimensions did not match.");}
    token.ThrowIfCancellationRequested();File.Move(partial,destination);Log("4 / 4 · Enhanced PNG saved. Larger output uses Lanczos after DLSS enhancement.");return destination;
   }finally{try{if(File.Exists(partial))File.Delete(partial);RemoveJob(job,parent);}finally{gate.Release();}}
  }
 }
}
