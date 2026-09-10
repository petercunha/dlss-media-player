using System;using System.IO;using System.Linq;using System.Reflection;using System.Threading;using System.Threading.Tasks;using System.Windows.Forms;
class AutoRoutingTests {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static T Field<T>(Launcher form,string name){return (T)typeof(Launcher).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);}
 [STAThread] static int Main(string[] a){try{
  Check(Engine.IsTwitchUrl("https://www.twitch.tv/sweetheart?x=1"),"Twitch channel detection");
  Check(!Engine.IsTwitchUrl("https://twitch.tv.evil.example/sweetheart")&&!Engine.IsTwitchUrl("https://example.com/twitch.tv")&&!Engine.IsTwitchUrl("file:///twitch.tv"),"Host validation");
  using(var f=new Launcher("")){
   Field<ComboBox>(f,"action").SelectedIndex=0;Field<ComboBox>(f,"motion").SelectedIndex=1;Field<TextBox>(f,"input").Text="https://www.twitch.tv/sweetheart";
   Check(Field<ComboBox>(f,"buffer").Enabled&&Field<ComboBox>(f,"motion").Enabled&&!Field<ComboBox>(f,"network").Enabled,"Automatic Twitch UI routing and smoothing controls");
   Field<ComboBox>(f,"motion").SelectedIndex=2;Check(Field<ComboBox>(f,"motion").SelectedIndex==0,"A live channel must not accidentally enter offline RIFE/download mode");
  }
  var e=new Engine(a[0]){SmoothPlayback=true};Check(e.MotionArguments().Contains("--video-sync=display-resample")&&e.MotionArguments().Contains("--interpolation=yes"),"Shared smoothing options");
  Check(e.CanStreamlinkHandle("https://www.twitch.tv/sweetheart",CancellationToken.None).GetAwaiter().GetResult(),"Automatic stream route");
  Check(!Task.Run(()=>e.CanStreamlinkHandle("https://example.com/video.mp4",CancellationToken.None)).GetAwaiter().GetResult(),"Generic URL retains yt-dlp path");
  Console.WriteLine("PASS: Twitch auto-routing, host validation, live controls, and shared smoothing arguments");return 0;
 }catch(Exception ex){Console.WriteLine(ex);return 1;}}
}
