using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

sealed class Launcher : Form {
 readonly Engine engine=new Engine();
 readonly TextBox input=new TextBox(),log=new TextBox();
 readonly ComboBox action=new ComboBox(),network=new ComboBox(),quality=new ComboBox(),motion=new ComboBox(),work=new ComboBox(),size=new ComboBox(),rtx=new ComboBox(),liveSize=new ComboBox(); Label sizeLabel;
 readonly Button browse=new Button(),start=new Button(),stop=new Button();
 readonly Label status=new Label(),hint=new Label(); readonly ProgressBar progress=new ProgressBar(); CancellationTokenSource active;
 readonly Color bg=Color.FromArgb(19,23,30),panel=Color.FromArgb(31,38,48),ink=Color.FromArgb(230,237,244),muted=Color.FromArgb(150,165,181),accent=Color.FromArgb(133,221,170);
 string SettingsPath {get{return Path.Combine(engine.Root,"launcher-settings.json");}}
 public Launcher(string initial) {
  Text="DLSS 5 · Media Launcher";ClientSize=new Size(760,670);MinimumSize=new Size(776,709);StartPosition=FormStartPosition.CenterScreen;BackColor=bg;ForeColor=ink;Font=new Font("Segoe UI",10);AutoScaleMode=AutoScaleMode.Dpi;
  LabelAt("DLSS 5  /  MEDIA PLAYER",28,20,705,40,ink).Font=new Font("Segoe UI Semibold",22);
  LabelAt("Watch live, smooth the motion, or save an enhanced video.",28,65,704,25,muted);
  LabelAt("VIDEO FILE OR URL",28,105,600,24,muted);
  input.SetBounds(28,134,548,33);input.BackColor=panel;input.ForeColor=ink;input.BorderStyle=BorderStyle.FixedSingle;input.AccessibleName="Video file or URL";Controls.Add(input);
  ButtonAt(browse,"Browse…",594,130,138,38,false);browse.Click+=(s,e)=>{using(var d=new OpenFileDialog{Title="Select a video",Filter="Video files|*.mp4;*.mkv;*.webm;*.mov;*.avi;*.m4v;*.wmv;*.ts;*.m2ts|All files|*.*"})if(d.ShowDialog(this)==DialogResult.OK)input.Text=d.FileName;};
  LabelAt("ACTION",28,190,220,22,muted);LabelAt("URL HANDLING",274,190,220,22,muted);LabelAt("URL QUALITY",520,190,200,22,muted);
  ComboAt(action,28,219,225,"Action",new[]{"Live playback","Prepare enhanced & play","Export enhanced MP4","Streamlink live"});
  ComboAt(network,274,219,225,"URL handling",new[]{"Auto · stream / download","Stream only","Download first"});
  ComboAt(quality,520,219,212,"URL source quality",new[]{"Up to 720p","Up to 1080p","Best available"});quality.SelectedIndex=1;
  LabelAt("MOTION / FRAME GENERATION",28,264,250,22,muted);LabelAt("LIVE DLSS WORK SIZE",274,264,225,22,muted);sizeLabel=LabelAt("EXPORT SIZE",520,264,210,22,muted);
  ComboAt(motion,28,293,225,"Motion and frame generation",new[]{"Off · original frames","Display smoothing (live)","RIFE 2× (prepare first)"});
  ComboAt(work,274,293,225,"Live DLSS work size",new[]{"100% · full quality","75% · balanced","50% · faster","Source · VSR upscale"});work.SelectedIndex=3;
  ComboAt(size,520,293,212,"Export size",new[]{"Source resolution","At least 1080p","At least 1440p","At least 2160p"});
  ComboAt(liveSize,520,293,212,"Live output size",new[]{"Fit player window","Fit within 1080p","Fit within 1440p","Fit within 2160p","Display · fullscreen"});liveSize.SelectedIndex=4;
  hint.SetBounds(28,341,704,47);hint.ForeColor=muted;Controls.Add(hint);
  ButtonAt(start,"Start",28,397,206,44,true);ButtonAt(stop,"Stop",253,397,110,44,false);stop.Enabled=false;
  var outputs=new LinkLabel{Text="Open output folder",LinkColor=accent,Location=new Point(520,410),Size=new Size(212,27)};outputs.Click+=(s,e)=>{string folder=Path.Combine(engine.Root,"Exports");Directory.CreateDirectory(folder);Process.Start(new ProcessStartInfo("explorer.exe",Engine.Quote(folder)){UseShellExecute=true});};Controls.Add(outputs);
  status.SetBounds(28,460,704,35);status.ForeColor=accent;status.Text="Ready";status.AutoEllipsis=true;Controls.Add(status);
  progress.SetBounds(28,500,704,4);progress.Style=ProgressBarStyle.Marquee;progress.Visible=false;Controls.Add(progress);
  log.SetBounds(28,520,704,102);log.Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;log.Multiline=true;log.ReadOnly=true;log.ScrollBars=ScrollBars.Vertical;log.BackColor=panel;log.ForeColor=muted;log.BorderStyle=BorderStyle.None;log.Font=new Font("Consolas",9);Controls.Add(log);
  LabelAt("DLSS neural enhancement · optional RIFE · audio included in exports",28,640,704,24,muted).Anchor=AnchorStyles.Bottom|AnchorStyles.Left;
  foreach(Control c in Controls)if(c.Top>=341)c.Top+=74;ClientSize=new Size(760,744);MinimumSize=new Size(776,783);log.SetBounds(28,594,704,102);Controls.OfType<Label>().First(l=>l.Text.StartsWith("DLSS neural enhancement")).Top=714;
  LabelAt("LIVE RTX PROCESSING · AFTER DLSS",28,338,704,24,muted);
  ComboAt(rtx,28,367,704,"Live RTX processing",new[]{"Off","RTX Video Super Resolution","RTX Video HDR","RTX VSR + RTX Video HDR"});rtx.SelectedIndex=3;
  engine.Log=Append;start.Click+=async(s,e)=>await Start();stop.Click+=(s,e)=>{if(active!=null){status.Text="Stopping…";active.Cancel();}};AcceptButton=start;
  rtx.SelectedIndexChanged+=(s,e)=>{if(!loadingSettings && (rtx.SelectedIndex&1)!=0)work.SelectedIndex=3;RefreshHint();};work.SelectedIndexChanged+=(s,e)=>RefreshHint();action.SelectedIndexChanged+=(s,e)=>RefreshHint();motion.SelectedIndexChanged+=(s,e)=>RefreshHint();
  AllowDrop=true;DragEnter+=(s,e)=>{if(active==null&&(e.Data.GetDataPresent(DataFormats.FileDrop)||e.Data.GetDataPresent(DataFormats.UnicodeText)))e.Effect=DragDropEffects.Copy;};
  DragDrop+=(s,e)=>{input.Text=e.Data.GetDataPresent(DataFormats.FileDrop)?((string[])e.Data.GetData(DataFormats.FileDrop))[0]:((string)e.Data.GetData(DataFormats.UnicodeText)).Trim();};
  FormClosing+=(s,e)=>{SaveSettings();if(active!=null){e.Cancel=true;status.Text="Stopping before closing…";closeWhenDone=true;active.Cancel();}};
  LoadSettings();loadingSettings=false;if(legacySettings && (rtx.SelectedIndex&1)!=0)work.SelectedIndex=3;RefreshHint();if(!string.IsNullOrWhiteSpace(initial))input.Text=initial;
 }
 bool closeWhenDone; bool loadingSettings=true,legacySettings=true;
 Label LabelAt(string text,int x,int y,int w,int h,Color color){var l=new Label{Text=text,Location=new Point(x,y),Size=new Size(w,h),ForeColor=color};Controls.Add(l);return l;}
 void ButtonAt(Button b,string text,int x,int y,int w,int h,bool primary){b.Text=text;b.SetBounds(x,y,w,h);b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderSize=0;b.BackColor=primary?accent:panel;b.ForeColor=primary?bg:ink;b.Cursor=Cursors.Hand;Controls.Add(b);}
 void ComboAt(ComboBox b,int x,int y,int w,string accessible,string[] values){b.SetBounds(x,y,w,32);b.DropDownStyle=ComboBoxStyle.DropDownList;b.FlatStyle=FlatStyle.Flat;b.BackColor=panel;b.ForeColor=ink;b.AccessibleName=accessible;b.Items.AddRange(values);b.SelectedIndex=0;Controls.Add(b);}
 ComboBox[] Options(){return new[]{action,network,quality,motion,work,size,rtx,liveSize};}
 void SaveSettings(){try{File.WriteAllText(SettingsPath,new JavaScriptSerializer().Serialize(Options().Select(c=>c.SelectedIndex).Concat(new[]{1}).ToArray()));}catch(IOException){}catch(UnauthorizedAccessException){}}
 void LoadSettings(){try{if(!File.Exists(SettingsPath))return;int[] v=new JavaScriptSerializer().Deserialize<int[]>(File.ReadAllText(SettingsPath));legacySettings=v.Length<9;var controls=Options();for(int i=0;i<Math.Min(v.Length,controls.Length);i++)if(v[i]>=0&&v[i]<controls[i].Items.Count)controls[i].SelectedIndex=v[i];}catch(Exception){}}
 void RefreshHint(){bool streamlink=action.SelectedIndex==3;bool offline=!streamlink&&(action.SelectedIndex!=0||motion.SelectedIndex==2);hint.Text=streamlink?"Streamlink → DLSS → optional RTX VSR/HDR. Live output size controls the window/display; DLSS work size controls neural processing cost.":offline?"Prepare first: DLSS enhancement, optional RIFE 2×, then play or save. Larger exports use Lanczos resizing; this is not DLSS Super Resolution.":"Source mode: DLSS at source size, then VSR enlarges to the display. Percentage modes use display-sized input. HDR requires Windows HDR.";if(!offline && work.SelectedIndex==3 && (rtx.SelectedIndex&1)==0)hint.Text="Source mode requires RTX VSR. With VSR off, DLSS uses 100% display size.";if(active==null){size.Visible=offline;liveSize.Visible=!offline;sizeLabel.Text=offline?"EXPORT SIZE":"LIVE OUTPUT SIZE";size.Enabled=offline;liveSize.Enabled=!offline;rtx.Enabled=!offline;work.Enabled=!offline;network.Enabled=!offline&&!streamlink;motion.Enabled=!streamlink;browse.Enabled=!streamlink;}start.Text=streamlink?"Watch live stream":action.SelectedIndex==2?"Export video…":offline?"Prepare & play":"Play video";}
 void Append(string line){if(IsDisposed||!IsHandleCreated)return;try{BeginInvoke((Action)(()=>{if(IsDisposed)return;if(log.TextLength>24000)log.Text=log.Text.Substring(log.TextLength-16000);log.AppendText(line+Environment.NewLine);if(line.StartsWith("[download]")||line.StartsWith("DLSS frames")||line.StartsWith("RIFE frames")||line.Contains(" / 4 ·"))status.Text=line;}));}catch(InvalidOperationException){}}
 async Task Start(){
  if(active!=null)return;string source=input.Text.Trim();bool url=Engine.IsUrl(source),streamlink=action.SelectedIndex==3;if(streamlink?!Engine.IsStreamlinkInput(source):!url&&!File.Exists(source)){status.Text=streamlink?"Enter a Streamlink-supported URL.":"Choose a video file or paste an http(s) URL.";return;}
  bool offline=!streamlink&&(action.SelectedIndex!=0||motion.SelectedIndex==2),export=action.SelectedIndex==2,rife=!streamlink&&motion.SelectedIndex==2;int q=quality.SelectedIndex==0?720:quality.SelectedIndex==1?1080:0;int selectedNetwork=network.SelectedIndex;int height=new[]{0,1080,1440,2160}[size.SelectedIndex];string destination=null;
  if(export){string folder=Path.Combine(engine.Root,"Exports");Directory.CreateDirectory(folder);using(var save=new SaveFileDialog{Title="Save enhanced video",Filter="MP4 video|*.mp4",DefaultExt="mp4",InitialDirectory=folder,FileName="Enhanced-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".mp4",OverwritePrompt=false}){if(save.ShowDialog(this)!=DialogResult.OK)return;destination=save.FileName;if(File.Exists(destination)){status.Text="Choose a new filename; that file already exists.";return;}}}
  SaveSettings();active=new CancellationTokenSource();var token=active.Token;foreach(var c in Options())c.Enabled=false;input.Enabled=browse.Enabled=start.Enabled=false;stop.Enabled=true;progress.Visible=true;log.Clear();status.Text="Preparing…";
  try{
   engine.LiveRtxMode=offline?0:rtx.SelectedIndex;engine.LiveSourceResolution=!offline && work.SelectedIndex==3 && (engine.LiveRtxMode&1)!=0;engine.LiveTarget=liveSize.SelectedIndex;engine.LiveScreen=Math.Max(0,Array.IndexOf(Screen.AllScreens,Screen.FromControl(this)));
   engine.PreparedPlayback=false;engine.SmoothPlayback=motion.SelectedIndex==1;int code=0;
   if(streamlink){engine.SetWorkResolution(new[]{100,75,50,100}[work.SelectedIndex]);status.Text="Opening live stream with Streamlink…";code=await engine.PlayStreamlink(source,q,token);}
   else if(offline){if(url)source=await engine.Download(source,q,token);else source=Path.GetFullPath(source);if(destination==null){string folder=Path.Combine(engine.Root,"Exports");Directory.CreateDirectory(folder);destination=Path.Combine(folder,"Prepared-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+".mp4");}string result=await engine.ProcessVideo(source,destination,rife,height,token);if(!export){engine.PreparedPlayback=true;status.Text="Playing prepared video…";code=await engine.Play(result,false,q,token);}else status.Text="Export complete · "+Path.GetFileName(result);}
   else{engine.SetWorkResolution(new[]{100,75,50,100}[work.SelectedIndex]);if(!url)code=await engine.Play(Path.GetFullPath(source),false,q,token);else{bool download=selectedNetwork==2;if(!download){status.Text="Streaming…";code=await engine.Play(source,true,q,token);download=code!=0&&selectedNetwork==0;}if(download){source=await engine.Download(source,q,token);code=await engine.Play(source,false,q,token);}}}
   if(!export)status.Text=code==0?"Ready · playback finished":"Playback failed · see activity log.";
  }catch(OperationCanceledException){status.Text="Stopped";}catch(Exception ex){status.Text="Could not complete · see activity log";Append(ex.Message);}
  finally{engine.PreparedPlayback=false;active.Dispose();active=null;foreach(var c in Options())c.Enabled=true;input.Enabled=browse.Enabled=start.Enabled=true;stop.Enabled=false;progress.Visible=false;RefreshHint();if(closeWhenDone)Close();}
 }
}
