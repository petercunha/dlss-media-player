// Local MPV integration: consume the completed neural texture on the GPU.
// NVIDIA video-processor extension ABI also used by Chromium and MPV.
#include <wrl/client.h>
#include <dxgi1_6.h>
namespace MediaRtx {
using Microsoft::WRL::ComPtr;
static const GUID VsrGuid={0xd43ce1b3,0x1f4b,0x48ac,{0xba,0xee,0xc3,0xc2,0x53,0x75,0xe6,0xf7}};
static const GUID HdrGuid={0xfdd62bb4,0x620b,0x4fd7,{0x9a,0xb3,0x1e,0x59,0xd0,0xd5,0x44,0xb3}};
static int Mode(){static int mode=[] {char v[16]={};GetEnvironmentVariableA("DLSS_MEDIA_RTX",v,sizeof(v));return atoi(v)&3;}();return mode;}
static void Report(const char* format,...){
 char text[1024];va_list args;va_start(args,format);vsnprintf(text,sizeof(text),format,args);va_end(args);
 Log("%s",text);size_t n=strlen(text);if(n<sizeof(text)-2){text[n++]='\n';text[n]=0;}DWORD written=0;
 HANDLE output=GetStdHandle(STD_ERROR_HANDLE);if(output&&output!=INVALID_HANDLE_VALUE)WriteFile(output,text,(DWORD)n,&written,nullptr);
}
struct State {
 ComPtr<IDXGISwapChain3> swap;
 ComPtr<ID3D11VideoDevice> device;
 ComPtr<ID3D11VideoContext1> context;
 ComPtr<ID3D11VideoProcessorEnumerator> convertEnum,enhanceEnum;
 ComPtr<ID3D11VideoProcessor> convert,enhance;
 ComPtr<ID3D11Texture2D> inputCopy,nv12,result;
 ComPtr<ID3D11VideoProcessorInputView> rgbView,yuvView;
 ComPtr<ID3D11VideoProcessorOutputView> yuvOut,resultOut;
 UINT iw{},ih{},ow{},oh{},sequence{};
 DXGI_FORMAT format{};bool hdr{},failed{};unsigned long long frames{};
};
static State s;
static void Reset(){auto swap=s.swap;s=State{};s.swap=swap;}
static bool Check(HRESULT hr,const char* stage){if(SUCCEEDED(hr))return true;Report("[media-rtx] ERROR %s: 0x%08X; using SDR DLSS output",stage,hr);return false;}
static bool DisplayHdr(){
 if(!s.swap)return false;
 ComPtr<IDXGIOutput> output;ComPtr<IDXGIOutput6> output6;DXGI_OUTPUT_DESC1 desc{};
 return SUCCEEDED(s.swap->GetContainingOutput(&output))&&SUCCEEDED(output.As(&output6))&&
        SUCCEEDED(output6->GetDesc1(&desc))&&desc.ColorSpace==DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020;
}
static void Present(reshade::api::command_queue* queue,reshade::api::swapchain* swap,const reshade::api::rect*,const reshade::api::rect*,uint32_t,const reshade::api::rect*){
 if(!Mode()||queue->get_device()->get_api()!=reshade::api::device_api::d3d11)return;
 auto native=reinterpret_cast<IDXGISwapChain*>(swap->get_native());
 if(native)native->QueryInterface(IID_PPV_ARGS(&s.swap));
}
static void Destroy(reshade::api::effect_runtime* runtime){if(runtime->get_device()->get_api()==reshade::api::device_api::d3d11)s=State{};}
static bool Texture(ID3D11Device* dev,UINT w,UINT h,DXGI_FORMAT format,UINT flags,ComPtr<ID3D11Texture2D>& tex){
 D3D11_TEXTURE2D_DESC d{};d.Width=w;d.Height=h;d.MipLevels=1;d.ArraySize=1;d.Format=format;d.SampleDesc.Count=1;d.BindFlags=flags;
 return Check(dev->CreateTexture2D(&d,nullptr,&tex),"texture");
}
static bool Processor(UINT iw,UINT ih,UINT ow,UINT oh,ComPtr<ID3D11VideoProcessorEnumerator>& enumerator,ComPtr<ID3D11VideoProcessor>& processor){
 D3D11_VIDEO_PROCESSOR_CONTENT_DESC d{};d.InputFrameFormat=D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE;d.InputWidth=iw;d.InputHeight=ih;d.OutputWidth=ow;d.OutputHeight=oh;d.InputFrameRate={60,1};d.OutputFrameRate={60,1};d.Usage=D3D11_VIDEO_USAGE_PLAYBACK_NORMAL;
 return Check(s.device->CreateVideoProcessorEnumerator(&d,&enumerator),"enumerator")&&Check(s.device->CreateVideoProcessor(enumerator.Get(),0,&processor),"processor");
}
static bool Input(ID3D11Texture2D* tex,ID3D11VideoProcessorEnumerator* e,ComPtr<ID3D11VideoProcessorInputView>& view){D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC d{};d.ViewDimension=D3D11_VPIV_DIMENSION_TEXTURE2D;return Check(s.device->CreateVideoProcessorInputView(tex,e,&d,&view),"input view");}
static bool Output(ID3D11Texture2D* tex,ID3D11VideoProcessorEnumerator* e,ComPtr<ID3D11VideoProcessorOutputView>& view){D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC d{};d.ViewDimension=D3D11_VPOV_DIMENSION_TEXTURE2D;return Check(s.device->CreateVideoProcessorOutputView(tex,e,&d,&view),"output view");}
static void Configure(ID3D11VideoProcessor* p,UINT iw,UINT ih,UINT ow,UINT oh,DXGI_COLOR_SPACE_TYPE in,DXGI_COLOR_SPACE_TYPE out){
 RECT src={0,0,(LONG)iw,(LONG)ih},dst={0,0,(LONG)ow,(LONG)oh};
 s.context->VideoProcessorSetStreamFrameFormat(p,0,D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE);
 s.context->VideoProcessorSetStreamSourceRect(p,0,TRUE,&src);s.context->VideoProcessorSetStreamDestRect(p,0,TRUE,&dst);
 s.context->VideoProcessorSetOutputTargetRect(p,TRUE,&dst);s.context->VideoProcessorSetStreamAutoProcessingMode(p,0,FALSE);
 s.context->VideoProcessorSetStreamColorSpace1(p,0,in);s.context->VideoProcessorSetOutputColorSpace1(p,out);
}
static bool Build(ID3D11DeviceContext* ctx,D3D11_TEXTURE2D_DESC input,D3D11_TEXTURE2D_DESC target,bool hdr){
 Reset();s.iw=input.Width;s.ih=input.Height;s.ow=target.Width;s.oh=target.Height;s.format=target.Format;s.hdr=hdr;
 ComPtr<ID3D11Device> dev;ctx->GetDevice(&dev);
 if(!Check(dev.As(&s.device),"video device")||!Check(ctx->QueryInterface(IID_PPV_ARGS(&s.context)),"video context"))return false;
 if(!Processor(s.iw,s.ih,s.iw,s.ih,s.convertEnum,s.convert)||!Processor(s.iw,s.ih,s.ow,s.oh,s.enhanceEnum,s.enhance))return false;
 if(!Texture(dev.Get(),s.iw,s.ih,input.Format,D3D11_BIND_RENDER_TARGET,s.inputCopy)||!Texture(dev.Get(),s.iw,s.ih,DXGI_FORMAT_NV12,D3D11_BIND_RENDER_TARGET,s.nv12)||!Texture(dev.Get(),s.ow,s.oh,target.Format,D3D11_BIND_RENDER_TARGET,s.result))return false;
 if(!Input(s.inputCopy.Get(),s.convertEnum.Get(),s.rgbView)||!Output(s.nv12.Get(),s.convertEnum.Get(),s.yuvOut)||!Input(s.nv12.Get(),s.enhanceEnum.Get(),s.yuvView)||!Output(s.result.Get(),s.enhanceEnum.Get(),s.resultOut))return false;
 Configure(s.convert.Get(),s.iw,s.ih,s.iw,s.ih,DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709,DXGI_COLOR_SPACE_YCBCR_STUDIO_G22_LEFT_P709);
 Configure(s.enhance.Get(),s.iw,s.ih,s.ow,s.oh,DXGI_COLOR_SPACE_YCBCR_STUDIO_G22_LEFT_P709,hdr?DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020:DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709);
 struct Extension{UINT version,method,enable;};
 if(Mode()&1){Extension ext={1,2,1};if(!Check(s.context->VideoProcessorSetStreamExtension(s.enhance.Get(),0,&VsrGuid,sizeof(ext),&ext),"enable RTX VSR"))return false;}
 if(hdr){UINT supported=0;if(!Check(s.context->VideoProcessorGetStreamExtension(s.enhance.Get(),0,&HdrGuid,sizeof(supported),&supported),"query RTX HDR")||!supported){Report("[media-rtx] RTX HDR unavailable");return false;}Extension ext={4,3,1};if(!Check(s.context->VideoProcessorSetStreamExtension(s.enhance.Get(),0,&HdrGuid,sizeof(ext),&ext),"enable RTX HDR"))return false;}
 Report("[media-rtx] DLSS -> RTX VSR=%d HDR=%d: %ux%u -> %ux%u; format=%d",Mode()&1,hdr,s.iw,s.ih,s.ow,s.oh,(int)target.Format);
 return true;
}
static bool Process(ID3D11DeviceContext* ctx,ID3D11Texture2D* input,ID3D11RenderTargetView* rtv){
 if(!Mode())return false;
 ComPtr<ID3D11Resource> resource;rtv->GetResource(&resource);ComPtr<ID3D11Texture2D> target;if(FAILED(resource.As(&target)))return false;
 D3D11_TEXTURE2D_DESC in{},out{};input->GetDesc(&in);target->GetDesc(&out);
 bool hdr=(Mode()&2)&&DisplayHdr()&&out.Format==DXGI_FORMAT_R10G10B10A2_UNORM;
 if(!s.convert||in.Width!=s.iw||in.Height!=s.ih||out.Width!=s.ow||out.Height!=s.oh||out.Format!=s.format||hdr!=s.hdr){
  if(s.failed)return false;
  if(!Build(ctx,in,out,hdr)){s.failed=true;return false;}
  if((Mode()&2)&&!hdr)Report("[media-rtx] HDR inactive: enable Windows HDR on the player display and use RGB10 output");
 }
 ComPtr<ID3D11RenderTargetView> old;ComPtr<ID3D11DepthStencilView> depth;ctx->OMGetRenderTargets(1,&old,&depth);ctx->OMSetRenderTargets(0,nullptr,nullptr);
 ctx->CopyResource(s.inputCopy.Get(),input);
 D3D11_VIDEO_PROCESSOR_STREAM stream{};stream.Enable=TRUE;stream.pInputSurface=s.rgbView.Get();
 bool ok=Check(s.context->VideoProcessorBlt(s.convert.Get(),s.yuvOut.Get(),s.sequence,1,&stream),"RGB to NV12");
 stream.pInputSurface=s.yuvView.Get();
 if(ok)ok=Check(s.context->VideoProcessorBlt(s.enhance.Get(),s.resultOut.Get(),s.sequence++,1,&stream),"RTX processing");
 if(ok&&s.swap)ok=Check(s.swap->SetColorSpace1(hdr?DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020:DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709),"presentation color space");
 if(ok){ctx->CopyResource(target.Get(),s.result.Get());if(++s.frames==1||s.frames%600==0)Report("[media-rtx] processed neural frame %llu; HDR=%d",s.frames,hdr);}
 ID3D11RenderTargetView* restore=old.Get();ctx->OMSetRenderTargets(1,&restore,depth.Get());
 if(!ok){s.failed=true;if(s.swap)s.swap->SetColorSpace1(DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709);}
 return ok;
}
}
