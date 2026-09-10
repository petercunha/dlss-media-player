using System;using System.IO;using System.Drawing;using System.Drawing.Imaging;using System.Threading;
class ImageTests {
 static int Main(string[] a){try{
  string source=a[1],dest=a[2];
  if(!Engine.IsUrl(source))using(var bitmap=new Bitmap(321,241,PixelFormat.Format32bppArgb)){
   using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.Transparent);g.FillRectangle(Brushes.Red,40,40,100,160);g.FillEllipse(Brushes.Blue,150,40,130,150);}
   bitmap.Save(source,ImageFormat.Png);
  }
  var e=new Engine(a[0]);e.Log=Console.WriteLine;
  e.ProcessImage(source,dest,0,CancellationToken.None).GetAwaiter().GetResult();
  using(var bitmap=new Bitmap(dest))if(bitmap.Width!=321||bitmap.Height!=241||bitmap.GetPixel(0,0).A!=0||bitmap.GetPixel(60,60).A!=255)throw new Exception("Image dimensions or transparency changed");
  try{e.ProcessImage(source,dest,0,CancellationToken.None).GetAwaiter().GetResult();throw new Exception("Overwrote existing image");}catch(Exception ex){if(!ex.Message.Contains("existing images"))throw;}
  Console.WriteLine("PASS: enhanced PNG, original odd dimensions, transparency and overwrite protection");return 0;
 }catch(Exception ex){Console.WriteLine(ex);return 1;}}
}
