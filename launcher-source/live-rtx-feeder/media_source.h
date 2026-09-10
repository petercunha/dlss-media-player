// MPV renders without enlargement. Crop its video rectangle before neural evaluation.
namespace MediaSource {
static UINT x{},y{},width{},height{},nativeWidth{},nativeHeight{},workWidth{},workHeight{},outputWidth{},outputHeight{};
static bool useVsr=false;
static bool active=false;
static const wchar_t* Path(){static wchar_t path[32768]={};static bool read=false;if(!read){GetEnvironmentVariableW(L"DLSS_MEDIA_GEOMETRY",path,32768);read=true;}return path;}
static bool Enabled(){return Path()[0]!=0;}
static bool Update(UINT bw,UINT bh){
 active=false;if(!Enabled())return false;
 static ULONGLONG next=0;static int w=0,h=0,l=0,t=0,cw=0,ch=0,nw=0,nh=0;
 if(GetTickCount64()>=next){
  next=GetTickCount64()+100;
  FILE* f=_wfopen(Path(),L"r");int a,b,c,d,e,g,i,j;
  if(f){if(fscanf(f,"%d %d %d %d %d %d %d %d",&a,&b,&c,&d,&e,&g,&i,&j)==8){w=a;h=b;l=c;t=d;cw=e;ch=g;nw=i;nh=j;}else w=0;fclose(f);}else w=0;
 }
 if(w!=(int)bw||h!=(int)bh||l<0||t<0||cw<2||ch<2||cw>w||ch>h||l>w-cw||t>h-ch)return false;
 if(nw<2||nh<2)return false;
 x=l;y=t;width=cw&~1u;height=ch&~1u;nativeWidth=nw;nativeHeight=nh;
 double fit=(std::min)((double)bw/width,(double)bh/height);
 outputWidth=((UINT)(width*fit))&~1u;outputHeight=((UINT)(height*fit))&~1u;
 useVsr=nw<=1920&&nh<=1080;
 double scale=(std::min)(fit,(std::min)(1920.0/width,1080.0/height));
 workWidth=useVsr?((UINT)(width*scale)&~1u):width;
 workHeight=useVsr?((UINT)(height*scale)&~1u):height;
 active=true;return true;
}
static RECT Fit(UINT iw,UINT ih,UINT ow,UINT oh){
 double scale=(std::min)((double)ow/iw,(double)oh/ih);
 LONG w=(LONG)(iw*scale),h=(LONG)(ih*scale),x=((LONG)ow-w)/2,y=((LONG)oh-h)/2;
 return RECT{x,y,x+w,y+h};
}
}
