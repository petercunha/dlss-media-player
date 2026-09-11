using System;using System.IO;using System.Linq;using System.Reflection;using System.Windows.Forms;
class SourceModeTests {
 static ComboBox Box(Launcher form,string name){return (ComboBox)typeof(Launcher).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);}
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 [STAThread] static int Main(string[] args){
  string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"launcher-settings.json");
  string original=File.Exists(path)?File.ReadAllText(path):null;
  try{
   File.Delete(path);
   using(var f=new Launcher("")){Check(Box(f,"work").SelectedIndex==0,"Fresh settings default to full quality");Check(!f.Controls.Cast<Control>().Any(c=>c.Text.Contains("RENDER BUFFER")),"Render buffer UI removed");}
   File.WriteAllText(path,"[0,0,1,0,0,0,3,4]");
   using(var f=new Launcher("")){Check(Box(f,"work").SelectedIndex==0,"Old full-quality settings must remain full quality");}
   File.WriteAllText(path,"[0,0,1,0,1,0,3,4,1]");
   using(var f=new Launcher("")){
    Check(Box(f,"work").SelectedIndex==1,"Retain explicit manual choice after migration");
    Check(Box(f,"rtx").SelectedIndex==1,"Old VSR+HDR settings retain HDR");
    Check(Box(f,"rtx").Items.Count==2&&Box(f,"work").Items.Count==5,"DLSS, Off and VSR choices");

   }
   File.WriteAllText(path,"[0,0,1,0,3,0,1,4,1]");
   using(var f=new Launcher("")){Check(Box(f,"work").SelectedIndex==0&&Box(f,"rtx").SelectedIndex==0,"Old VSR-only migrates to full-quality with HDR off");}
   File.WriteAllText(path,"[0,0,1,0,0,0,1,4,3,2]");
   using(var f=new Launcher("")){Check(Box(f,"work").SelectedIndex==0&&Box(f,"rtx").SelectedIndex==1,"Old buffer settings ignored");}
   var e=new Engine(args[0]){LiveRtxMode=2};
   foreach(int mode in new[]{3,4}){
    File.WriteAllText(path,"[0,0,1,0,"+mode+",0,1,4,3]");
    using(var f=new Launcher("")){Check(Box(f,"work").SelectedIndex==mode,"New Off/VSR setting persists");}
   }
   Check(e.LiveArguments().Contains("--d3d11-output-format=rgb10_a2"),"HDR needs RGB10");
   Check(!e.LiveArguments(true).Any(x=>x.StartsWith("--cache-pause")),"Ordinary live arguments must not masquerade source caching as rendered buffering");
   Console.WriteLine("PASS: defaults, migration, live enhancement modes, retired buffer settings and HDR routing");return 0;
  }catch(Exception ex){Console.WriteLine(ex);return 1;}
  finally{if(original==null)File.Delete(path);else File.WriteAllText(path,original);}
 }
}
