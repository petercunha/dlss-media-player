#include <windows.h>
#include <d3d11_4.h>
#include <algorithm>
#include <cstdio>
#include <cstdarg>
#include <vector>
#include <reshade.hpp>
static void Log(const char* fmt,...){va_list a;va_start(a,fmt);vprintf(fmt,a);puts("");va_end(a);}
#include "media_source.h"
#include "media_rtx.h"
int main(){
 SetEnvironmentVariableA("DLSS_MEDIA_RTX","1");SetEnvironmentVariableA("DLSS_MEDIA_GEOMETRY","");
 using Microsoft::WRL::ComPtr;
 ComPtr<ID3D11Device> dev;ComPtr<ID3D11DeviceContext> ctx;D3D_FEATURE_LEVEL fl;
 if(FAILED(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&dev,&fl,&ctx)))return 1;
 const UINT colors[]={0xc0000000,0xe0080200,0xffffffff,0xc00003ff};
 std::vector<UINT> data(512*512);for(int y=0;y<512;y++)for(int x=0;x<512;x++)data[y*512+x]=colors[x/128];
 D3D11_TEXTURE2D_DESC d={};d.Width=d.Height=512;d.MipLevels=d.ArraySize=1;d.Format=DXGI_FORMAT_R10G10B10A2_UNORM;d.SampleDesc.Count=1;d.BindFlags=D3D11_BIND_RENDER_TARGET;
 D3D11_SUBRESOURCE_DATA init={data.data(),512*4,0};ComPtr<ID3D11Texture2D> input,out,stage;ComPtr<ID3D11RenderTargetView> rtv;
 if(FAILED(dev->CreateTexture2D(&d,&init,&input)))return 2;d.Format=DXGI_FORMAT_R8G8B8A8_UNORM;
 if(FAILED(dev->CreateTexture2D(&d,nullptr,&out))||FAILED(dev->CreateRenderTargetView(out.Get(),nullptr,&rtv)))return 2;
 for(int i=0;i<4;i++)if(!MediaRtx::Process(ctx.Get(),input.Get(),rtv.Get()))return 3;
 d.Usage=D3D11_USAGE_STAGING;d.BindFlags=0;d.CPUAccessFlags=D3D11_CPU_ACCESS_READ;dev->CreateTexture2D(&d,nullptr,&stage);ctx->CopyResource(stage.Get(),out.Get());D3D11_MAPPED_SUBRESOURCE map={};ctx->Map(stage.Get(),0,D3D11_MAP_READ,0,&map);
 bool valid=true;const int expected[4][3]={{0,0,0},{128,128,128},{255,255,255},{255,0,0}};
 for(int i=0;i<4;i++){UINT v=((UINT*)((BYTE*)map.pData+256*map.RowPitch))[64+128*i];int rgb[3]={(int)(v&255),(int)((v>>8)&255),(int)((v>>16)&255)};for(int c=0;c<3;c++)if(abs(rgb[c]-expected[i][c])>5)valid=false;printf("patch %d: %u %u %u\n",i,v&255,(v>>8)&255,(v>>16)&255);}ctx->Unmap(stage.Get(),0);return valid?0:4;
}
