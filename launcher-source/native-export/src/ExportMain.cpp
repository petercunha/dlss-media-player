#include "OfflineNeuralRenderer.h"
#include "RuntimePolicy.h"
#include <windows.h>
#include <mfapi.h>
#include <iostream>
#include <thread>
#include <sstream>
#include <fcntl.h>
#include <io.h>

int wmain(int argc,wchar_t** argv) {
 const bool batch=argc==2&&std::wstring(argv[1])==L"--batch";
 if(argc!=7&&!batch) {std::cerr<<"Usage: NeuralExport input output width height fps duration, or --batch\n";return 2;}
 if(batch)_setmode(_fileno(stdin),_O_U8TEXT);
 (void)DetectHighPerformanceGpu();
 CoInitializeEx(nullptr,COINIT_MULTITHREADED);
 if(FAILED(MFStartup(MF_VERSION)))return 3;
 WNDCLASSW wc{}; wc.lpfnWndProc=DefWindowProcW;wc.hInstance=GetModuleHandleW(nullptr);wc.lpszClassName=L"DLSSOfflineExport";RegisterClassW(&wc);
 HWND window=CreateWindowExW(WS_EX_TOOLWINDOW,wc.lpszClassName,L"",WS_POPUP,0,0,16,16,nullptr,nullptr,wc.hInstance,nullptr);
 NeuralRenderRequest req{};req.renderWindow=window;
 if(!batch){req.sourcePath=argv[1];req.stagingVideoPath=argv[2];req.width=std::stoul(argv[3]);req.height=std::stoul(argv[4]);req.fps=std::stod(argv[5]);req.durationSeconds=std::stod(argv[6]);}
 HANDLE done=CreateEventW(nullptr,TRUE,FALSE,nullptr); NeuralRenderResult result;
 std::jthread worker([&]{try {
  OfflineNeuralRenderer renderer;
  do {
   if(batch){
    std::wstring line;if(!std::getline(std::wcin,line))break;
    std::wistringstream fields(line);std::vector<std::wstring> parts;std::wstring part;
    while(std::getline(fields,part,L'\t'))parts.push_back(part);
    if(parts.size()!=6)throw std::runtime_error("Invalid buffered render request");
    req.sourcePath=parts[0];req.stagingVideoPath=parts[1];req.width=std::stoul(parts[2]);req.height=std::stoul(parts[3]);req.fps=std::stod(parts[4]);req.durationSeconds=std::stod(parts[5]);req.reuseSession=true;
   }
   result=renderer.Run(req,[](const NeuralRenderProgress& p){std::cout<<"DLSS frames "<<p.completedFrames<<" / "<<p.totalFrames<<std::endl;});
   if(batch){std::cout<<"DLSS_BATCH_DONE "<<(result.ok?0:1)<<" "<<result.frameCount<<" "<<result.verifiedNeuralFrames<<std::endl;if(!result.ok){std::wcerr<<result.detail<<std::endl;break;}}
  }while(batch);
 }catch(const std::exception& e){std::cerr<<e.what()<<std::endl;}SetEvent(done);});
 // Keep pumping while thread-local GPU/session destructors run too. The body
 // completion event is earlier than actual thread termination.
 HANDLE workerHandle=worker.native_handle();
 while(MsgWaitForMultipleObjects(1,&workerHandle,FALSE,50,QS_ALLINPUT)!=WAIT_OBJECT_0){MSG msg{};while(PeekMessageW(&msg,nullptr,0,0,PM_REMOVE)){TranslateMessage(&msg);DispatchMessageW(&msg);}}
 worker.join();CloseHandle(done);DestroyWindow(window);MFShutdown();CoUninitialize();
 std::wcout<<L"Neural result: "<<result.detail<<L"; frames="<<result.frameCount<<L"; verified="<<result.verifiedNeuralFrames<<std::endl;
 return result.ok?0:1;
}
