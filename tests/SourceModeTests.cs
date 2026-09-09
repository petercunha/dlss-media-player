using System;using System.IO;using System.Linq;using System.Reflection;using System.Windows.Forms;
class SourceModeTests {
 static ComboBox Box(Launcher form,string name){return (ComboBox)typeof(Launcher).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);}
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 [STAThread] static int Main(string[] args){
  string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"launcher-settings.json");
  string original=File.Exists(path)?File.ReadAllText(path):null;
  try{
   File.WriteAllText(path,"[0,0,1,0,0,0,3,4]");
   using(var f=new Launcher("")){Check(Box(f,"work").SelectedIndex==3,"Old VSR settings must migrate to source mode");}
   File.WriteAllText(path,"[0,0,1,0,1,0,3,4,1]");
   using(var f=new Launcher("")){
    Check(Box(f,"work").SelectedIndex==1,"Retain explicit manual choice after migration");
    Box(f,"rtx").SelectedIndex=0;Box(f,"rtx").SelectedIndex=3;
    Check(Box(f,"work").SelectedIndex==3,"Enabling VSR defaults to source mode");
   }
   var e=new Engine(args[0]);e.LiveSourceResolution=true;e.LiveRtxMode=3;
   Check(e.LiveArguments().Contains("--video-unscaled=downscale-big"),"Source mode must prevent enlargement");
   e.LiveSourceResolution=false;Check(!e.LiveArguments().Any(x=>x.StartsWith("--video-unscaled")),"Manual modes retain their rendering path");
   e.LiveSourceResolution=true;e.LiveRtxMode=2;Check(!e.LiveArguments().Any(x=>x.StartsWith("--video-unscaled")),"HDR without VSR must retain display-sized rendering");
   Console.WriteLine("PASS: migration, saved choices, VSR default, manual and HDR-only routing");return 0;
  }catch(Exception ex){Console.WriteLine(ex);return 1;}
  finally{if(original==null)File.Delete(path);else File.WriteAllText(path,original);}
 }
}
