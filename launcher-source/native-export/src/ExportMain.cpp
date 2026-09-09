#include "OfflineNeuralRenderer.h"
#include "RuntimePolicy.h"
#include <windows.h>
#include <mfapi.h>
#include <iostream>
#include <thread>

int wmain(int argc,wchar_t** argv) {
 if(argc!=7) {std::cerr<<"Usage: NeuralExport input output width height fps duration\n";return 2;}
 (void)DetectHighPerformanceGpu();
 CoInitializeEx(nullptr,COINIT_MULTITHREADED);
 if(FAILED(MFStartup(MF_VERSION)))return 3;
 WNDCLASSW wc{}; wc.lpfnWndProc=DefWindowProcW;wc.hInstance=GetModuleHandleW(nullptr);wc.lpszClassName=L"DLSSOfflineExport";RegisterClassW(&wc);
 HWND window=CreateWindowExW(WS_EX_TOOLWINDOW,wc.lpszClassName,L"",WS_POPUP,0,0,16,16,nullptr,nullptr,wc.hInstance,nullptr);
 NeuralRenderRequest req{}; req.renderWindow=window;req.sourcePath=argv[1];req.stagingVideoPath=argv[2];req.width=std::stoul(argv[3]);req.height=std::stoul(argv[4]);req.fps=std::stod(argv[5]);req.durationSeconds=std::stod(argv[6]);
 HANDLE done=CreateEventW(nullptr,TRUE,FALSE,nullptr); NeuralRenderResult result;
 std::jthread worker([&]{try {OfflineNeuralRenderer renderer; result=renderer.Run(req,[](const NeuralRenderProgress& p){std::cout<<"DLSS frames "<<p.completedFrames<<" / "<<p.totalFrames<<std::endl;});}catch(const std::exception& e){std::cerr<<e.what()<<std::endl;}SetEvent(done);});
 while(MsgWaitForMultipleObjects(1,&done,FALSE,50,QS_ALLINPUT)!=WAIT_OBJECT_0){MSG msg{};while(PeekMessageW(&msg,nullptr,0,0,PM_REMOVE)){TranslateMessage(&msg);DispatchMessageW(&msg);}}
 worker.join();CloseHandle(done);DestroyWindow(window);MFShutdown();CoUninitialize();
 std::wcout<<L"Neural result: "<<result.detail<<L"; frames="<<result.frameCount<<L"; verified="<<result.verifiedNeuralFrames<<std::endl;
 return result.ok?0:1;
}
