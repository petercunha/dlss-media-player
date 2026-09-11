#include "OfflineNeuralRenderer.h"

#include <algorithm>
#include <array>
#include <charconv>
#include <cmath>
#include <fstream>
#include <iostream>
#include <deque>
#include <limits>
#include <thread>

#ifndef OFFLINE_NEURAL_RENDERER_TESTING
#include "D3D12Renderer.h"
#include "TemporalGuides.h"
#include "VideoDecoder.h"
#include "DLSSBackend.h"
#endif

namespace {

using SteadyClock = std::chrono::steady_clock;

enum class JobRead { FrameReady, EndOfStream, Error, Cancelled };

struct JobFrame {
    std::vector<uint8_t> bgra;
    int64_t timestamp100ns{};
    bool discontinuity{};
};

enum class AttemptFailure { None, Encoder, Neural, Source, Cancelled };

struct AttemptResult {
    AttemptFailure failure{AttemptFailure::None};
    EncodeError encoderError{EncodeError::None};
    uint64_t frames{};
    uint64_t bytes{};
    uint64_t evaluations{};
    bool hasTimestamp{};
    int64_t firstTimestamp{};
    int64_t lastTimestamp{};
};

std::string LowerAscii(std::string_view value)
{
    std::string result(value);
    for (char& character : result) {
        if (character >= 'A' && character <= 'Z') character = char(character - 'A' + 'a');
    }
    return result;
}

uint64_t HighestEvaluationCount(std::string_view lower)
{
    uint64_t highest = 0;
    constexpr std::array<std::string_view,2> markers{
        "evaluation count=", "evaluation succeeded (count="};
    for(const auto marker:markers){
        size_t position = 0;
        while ((position = lower.find(marker, position)) != std::string_view::npos) {
            position += marker.size();
            uint64_t value = 0;
            const char* first = lower.data() + position;
            const char* last = lower.data() + lower.size();
            const auto parsed = std::from_chars(first, last, value);
            if (parsed.ec == std::errc{}) highest = std::max(highest, value);
        }
    }
    return highest;
}

template<class Source, class Evaluator, class Encoder, class Evidence, class Clock>
NeuralRenderResult RunJob(const NeuralRenderRequest& request,
                          OfflineNeuralRenderer::ProgressCallback progress,
                          std::stop_token stop, Source& source, Evaluator& evaluator,
                          Encoder& encoder, Evidence evidenceProvider, Clock clock,
                          bool verifiedContinuation=false)
{
    NeuralRenderResult result;
    if (request.sourcePath.empty() || request.stagingVideoPath.empty() ||
        !request.width || !request.height || !std::isfinite(request.fps) || request.fps <= 0.0 ||
        !std::isfinite(request.durationSeconds) || request.durationSeconds <= 0.0) {
        result.detail = L"Invalid neural render request.";
        return result;
    }
    const uint64_t totalFrames = std::max<uint64_t>(
        1, static_cast<uint64_t>(std::llround(request.durationSeconds * request.fps)));
    const uint64_t expectedBytes64 = uint64_t{request.width} * request.height * 4u;
    if (expectedBytes64 > std::numeric_limits<size_t>::max()) {
        result.detail = L"Neural render dimensions are too large.";
        return result;
    }
    const size_t expectedBytes = static_cast<size_t>(expectedBytes64);
    const auto started = clock();
    auto lastProgressTime = started;
    uint64_t reportedCompleted = 0;
    uint64_t reportedBytes = 0;
    double smoothedFramesPerMs = 0.0;
    NeuralRenderProgress previous{};
    auto emit = [&](NeuralRenderPhase phase, uint64_t completed, uint64_t bytes, bool frameTick) {
        const auto now = clock();
        reportedCompleted = std::max(reportedCompleted, completed);
        reportedBytes = std::max(reportedBytes, bytes);
        if (frameTick && completed > previous.completedFrames) {
            const double milliseconds = std::max(1.0,
                std::chrono::duration<double, std::milli>(now - lastProgressTime).count());
            const double instant = double(completed - previous.completedFrames) / milliseconds;
            smoothedFramesPerMs = smoothedFramesPerMs == 0.0
                ? instant : smoothedFramesPerMs * 0.75 + instant * 0.25;
            lastProgressTime = now;
        }
        NeuralRenderProgress snapshot;
        snapshot.phase = phase;
        snapshot.completedFrames = reportedCompleted;
        snapshot.totalFrames = std::max(totalFrames, reportedCompleted);
        snapshot.bytes = reportedBytes;
        snapshot.elapsed = std::max(previous.elapsed,
            std::chrono::duration_cast<std::chrono::milliseconds>(now - started));
        if (snapshot.completedFrames < snapshot.totalFrames && smoothedFramesPerMs > 0.0) {
            snapshot.estimatedRemaining = std::chrono::milliseconds(static_cast<int64_t>(
                std::ceil(double(snapshot.totalFrames - snapshot.completedFrames) /
                          smoothedFramesPerMs)));
        }
        if (progress) progress(snapshot);
        previous = snapshot;
    };
    auto cancelled = [&](std::wstring detail) {
        encoder.Cancel();source.Close();result.cancelled = true;result.detail = std::move(detail);
        return result;
    };

    emit(NeuralRenderPhase::Acquiring, 0, 0, false);
    if (stop.stop_requested()) return cancelled(L"Neural render was cancelled.");
    if (!source.Open(request.sourcePath, stop)) {
        if (stop.stop_requested()) return cancelled(L"Neural render was cancelled.");
        result.detail = L"The source video could not be opened.";return result;
    }
    emit(NeuralRenderPhase::Decoding, 0, 0, false);
    if (!evaluator.Initialize(request.renderWindow, request.width, request.height, request.fps)) {
        source.Close();result.detail = L"The neural renderer could not be initialized.";return result;
    }

    const bool singleFrameSource = totalFrames == 1;
    const uint64_t primeLimit = singleFrameSource
        ? 120 : std::max<uint64_t>(2, std::min<uint64_t>(totalFrames, 120));
    uint64_t primed = 0;
    for (JobFrame primingFrame; !evaluator.FeatureCreated() && primed < primeLimit; ++primed) {
        if (stop.stop_requested()) return cancelled(L"Neural render was cancelled.");
        if (!singleFrameSource || primed == 0) {
            const JobRead read = source.Read(primingFrame, stop);
            if (read == JobRead::Cancelled) return cancelled(L"Neural render was cancelled.");
            if (read != JobRead::FrameReady) {
                source.Close();result.detail = L"Feature 18 could not be primed from the source.";return result;
            }
        } else {
            // A photo may need several presents to create feature 18. Reuse it
            // only for warm-up; capture below still reopens and reads it once.
            primingFrame.discontinuity = false;
        }
        std::vector<uint8_t> ignored;
        if (!evaluator.Submit(primingFrame, primed == 0 || primingFrame.discontinuity, false, ignored)) {
            source.Close();result.detail = L"Feature 18 priming failed.";return result;
        }
    }
    if (!evaluator.FeatureCreated()) {
        source.Close();result.detail = L"Feature 18 was not created.";return result;
    }
    const NeuralRuntimeEvidence armedEvidence=
        ParseNeuralRuntimeEvidence(evidenceProvider());
    if(!armedEvidence.Valid()){
        source.Close();
        result.detail=L"Feature 18 inline interception was not armed before frame capture.";
        return result;
    }
    result.feature18ArmedBeforeCapture=true;
    uint64_t successfulAttemptBaseline=armedEvidence.highestObservedEvaluation;

    auto reopenFromZero = [&](bool preserveHistory=false) {
        source.Close();
        if (!source.Open(request.sourcePath, stop)) return false;
        if(!preserveHistory)evaluator.ResetTemporal();
        return true;
    };
    // A retained feature consumed no priming frames. Its decoder is already at
    // frame zero and prefetching; reopening discards that work every chunk.
    if (!(verifiedContinuation && primed == 0) && !reopenFromZero(verifiedContinuation)) {
        if (stop.stop_requested()) return cancelled(L"Neural render was cancelled.");
        result.detail = L"The source could not be restarted from frame zero.";return result;
    }

    auto runAttempt = [&](EncoderKind kind) {
        AttemptResult attempt;
        const EncodeError startError = encoder.Start(
            EncoderSpec{request.width, request.height, request.fps, kind, request.reuseSession},
            request.stagingVideoPath);
        if (startError != EncodeError::None) {
            attempt.failure = startError == EncodeError::Cancelled
                ? AttemptFailure::Cancelled : AttemptFailure::Encoder;
            attempt.encoderError = startError;return attempt;
        }
        bool temporalReset = !verifiedContinuation;
        std::deque<int64_t> pendingTimestamps;
        int64_t lastSourceTimestamp=-1;
        auto encode=[&](std::vector<uint8_t>& pixels,int64_t timestamp){
            if(pixels.size()!=expectedBytes){attempt.failure=AttemptFailure::Neural;encoder.Cancel();return false;}
            const auto error=encoder.WriteFrame(pixels,stop);
            if(error!=EncodeError::None){attempt.failure=error==EncodeError::Cancelled?AttemptFailure::Cancelled:AttemptFailure::Encoder;attempt.encoderError=error;encoder.Cancel();return false;}
            ++attempt.frames;++attempt.evaluations;attempt.bytes+=expectedBytes;
            if(!attempt.hasTimestamp){attempt.firstTimestamp=timestamp;attempt.hasTimestamp=true;}
            attempt.lastTimestamp=timestamp;emit(NeuralRenderPhase::NeuralRendering,attempt.frames,attempt.bytes,true);return true;
        };
        auto resolve=[&]{
            std::vector<uint8_t> pixels;
            if(!evaluator.Resolve(pixels)){attempt.failure=AttemptFailure::Neural;encoder.Cancel();return false;}
            const auto timestamp=pendingTimestamps.front();pendingTimestamps.pop_front();
            return encode(pixels,timestamp);
        };
        for (;;) {
            if (stop.stop_requested()) {
                attempt.failure = AttemptFailure::Cancelled;attempt.encoderError=EncodeError::Cancelled;
                encoder.Cancel();return attempt;
            }
            JobFrame frame;
            const JobRead read = source.Read(frame, stop);
            if (read == JobRead::EndOfStream) {
                while(!pendingTimestamps.empty())if(!resolve())return attempt;
                if(attempt.frames==0){attempt.failure=AttemptFailure::Source;encoder.Cancel();}
                break;
            }
            if (read == JobRead::Cancelled) {
                attempt.failure=AttemptFailure::Cancelled;attempt.encoderError=EncodeError::Cancelled;
                encoder.Cancel();return attempt;
            }
            if (read != JobRead::FrameReady) {
                attempt.failure=AttemptFailure::Source;encoder.Cancel();return attempt;
            }
            if (frame.timestamp100ns < 0 ||
                (lastSourceTimestamp>=0 && frame.timestamp100ns <= lastSourceTimestamp)) {
                attempt.failure=AttemptFailure::Source;encoder.Cancel();return attempt;
            }
            lastSourceTimestamp=frame.timestamp100ns;
            // Keep the receipt-producing first frame synchronous. Subsequent
            // captures use upstream's FIFO readback-slot design; neural commands
            // and their copies are ordered on the same D3D12 queue.
            if(request.reuseSession&&attempt.frames>0){
                if(!evaluator.Enqueue(frame,temporalReset||frame.discontinuity)){
                    attempt.failure=AttemptFailure::Neural;encoder.Cancel();return attempt;
                }
                temporalReset=false;pendingTimestamps.push_back(frame.timestamp100ns);
                if(pendingTimestamps.size()>=3&&!resolve())return attempt;
                continue;
            }
            std::vector<uint8_t> captured;
            for (uint64_t capture = 1; ; ++capture) {
                if (stop.stop_requested()) {
                    attempt.failure=AttemptFailure::Cancelled;attempt.encoderError=EncodeError::Cancelled;
                    encoder.Cancel();return attempt;
                }
                const uint64_t before = evaluator.EvaluationCount();
                captured.clear();
                if (!evaluator.Submit(frame, temporalReset || (capture == 1 && frame.discontinuity), true, captured) ||
                    evaluator.EvaluationCount() <= before || captured.size() != expectedBytes) {
                    attempt.failure=AttemptFailure::Neural;encoder.Cancel();return attempt;
                }
                temporalReset = false;
                // The same persistent feature/session has already passed the
                // fresh-receipt check. Continue its per-call success/size checks;
                // RenoDX's sparse logging must not require synthetic warm-up
                // frames at every media chunk. Errors are still checked below.
                if (attempt.frames > 0 || verifiedContinuation) break;
                // The runtime logs successful evaluations sparsely. Capture the
                // first source frame until a fresh receipt exists, retaining
                // only its latest pixels for encoding. Each retry has its own
                // baseline, and these extra captures never extend the timeline.
                if (capture == 1 || capture % 10 == 0) {
                    const auto receipt = ParseNeuralRuntimeEvidence(evidenceProvider());
                    if (!receipt.Valid()) {
                        attempt.failure=AttemptFailure::Neural;encoder.Cancel();return attempt;
                    }
                    if (receipt.highestObservedEvaluation > successfulAttemptBaseline) break;
                }
                if (capture >= 120) {
                    attempt.failure=AttemptFailure::Neural;encoder.Cancel();return attempt;
                }
            }
            if(!encode(captured,frame.timestamp100ns))return attempt;
        }
        const EncodeError finishError=encoder.Finish(stop);
        if(finishError!=EncodeError::None){
            attempt.failure=finishError==EncodeError::Cancelled
                ? AttemptFailure::Cancelled:AttemptFailure::Encoder;
            attempt.encoderError=finishError;return attempt;
        }
        return attempt;
    };

    // NVENC accepts odd dimensions but silently pads them to an even size,
    // which breaks exact source/neural pairing and cache validation. libx264's
    // yuv444p path preserves odd image dimensions.
    EncoderKind selected = (request.width % 2 || request.height % 2)
        ? EncoderKind::H264Software : EncoderKind::HevcNvenc;
    AttemptResult attempt = runAttempt(selected);
    if (attempt.failure == AttemptFailure::Cancelled)
        return cancelled(L"Neural render was cancelled.");
    if (attempt.failure == AttemptFailure::Encoder &&
        ShouldRetryWithSoftware(selected, attempt.encoderError)) {
        encoder.Cancel();
        std::error_code removeError;std::filesystem::remove(request.stagingVideoPath, removeError);
        if (!reopenFromZero()) {
            if (stop.stop_requested()) return cancelled(L"Neural render was cancelled.");
            result.detail=L"The source could not be restarted for software encoding.";return result;
        }
        const NeuralRuntimeEvidence retryEvidence=
            ParseNeuralRuntimeEvidence(evidenceProvider());
        if(!retryEvidence.Valid()){
            result.detail=L"Feature 18 evidence was not valid before the software retry.";
            return result;
        }
        successfulAttemptBaseline=retryEvidence.highestObservedEvaluation;
        verifiedContinuation=false; // Retrying from zero is a real discontinuity.
        selected=EncoderKind::H264Software;
        attempt=runAttempt(selected);
    }
    if (attempt.failure == AttemptFailure::Cancelled)
        return cancelled(L"Neural render was cancelled.");
    if (attempt.failure != AttemptFailure::None) {
        encoder.Cancel();source.Close();
        result.detail = attempt.failure == AttemptFailure::Neural
            ? L"A frame was not produced by feature 18."
            : attempt.failure == AttemptFailure::Source
                ? L"The source decoder failed during neural rendering."
                : L"The neural video encoder failed.";
        return result;
    }
    source.Close();
    emit(NeuralRenderPhase::Encoding,attempt.frames,attempt.bytes,false);
    emit(NeuralRenderPhase::Validating,attempt.frames,attempt.bytes,false);
    result.evidence=ParseNeuralRuntimeEvidence(evidenceProvider());
    if(!result.evidence.Valid()||
       (!verifiedContinuation&&result.evidence.highestObservedEvaluation<=successfulAttemptBaseline)){
        result.detail=L"Feature 18 runtime evidence did not advance after captured rendering or contained a later failure.";
        return result;
    }
    result.ok=true;result.encoder=selected;result.frameCount=attempt.frames;
    result.nativeEvaluations=attempt.evaluations;
    result.verifiedNeuralFrames=attempt.frames;
    const int64_t nominalFrameDuration=static_cast<int64_t>(
        std::llround(10000000.0/request.fps));
    result.duration100ns=attempt.lastTimestamp-attempt.firstTimestamp+nominalFrameDuration;
    emit(NeuralRenderPhase::Ready,attempt.frames,attempt.bytes,false);
    return result;
}

#ifdef OFFLINE_NEURAL_RENDERER_TESTING
struct TestSourceAdapter {
    IFrameSource& source;
    bool Open(const std::filesystem::path& path,std::stop_token stop){return source.Open(path,stop);}
    void Close(){source.Close();}
    JobRead Read(JobFrame& frame,std::stop_token stop){
        OfflineDecodedFrame decoded;const auto read=source.Read(decoded,stop);
        frame={std::move(decoded.bgra),decoded.timestamp100ns,decoded.discontinuity};
        switch(read){
            case OfflineFrameRead::FrameReady:return JobRead::FrameReady;
            case OfflineFrameRead::EndOfStream:return JobRead::EndOfStream;
            case OfflineFrameRead::Cancelled:return JobRead::Cancelled;
            default:return JobRead::Error;
        }
    }
};
struct TestEvaluatorAdapter {
    INeuralFrameEvaluator& evaluator;
    bool Initialize(HWND window,uint32_t width,uint32_t height,double fps){return evaluator.Initialize(window,width,height,fps);}
    bool Submit(const JobFrame& frame,bool reset,bool capture,std::vector<uint8_t>& output){
        return evaluator.Submit(OfflineDecodedFrame{frame.bgra,frame.timestamp100ns,frame.discontinuity},
                                reset,capture,output);
    }
    bool FeatureCreated()const{return evaluator.FeatureCreated();}
    uint64_t EvaluationCount()const{return evaluator.EvaluationCount();}
    std::deque<std::vector<uint8_t>> pending;
    bool Enqueue(const JobFrame& frame,bool reset){std::vector<uint8_t> pixels;if(!Submit(frame,reset,true,pixels))return false;pending.push_back(std::move(pixels));return true;}
    bool Resolve(std::vector<uint8_t>& pixels){if(pending.empty())return false;pixels=std::move(pending.front());pending.pop_front();return true;}
    void ResetTemporal(){pending.clear();evaluator.ResetTemporal();}
};
struct TestEncoderAdapter {
    IFrameEncoder& encoder;
    EncodeError Start(const EncoderSpec& spec,const std::filesystem::path& path){return encoder.Start(spec,path);}
    EncodeError WriteFrame(std::span<const uint8_t> frame,std::stop_token stop){return encoder.WriteFrame(frame,stop);}
    EncodeError Finish(std::stop_token stop){return encoder.Finish(stop);}
    void Cancel(){encoder.Cancel();}
};
#else
struct ProductionSourceAdapter {
    VideoDecoder decoder;double readSeconds{};
    bool Open(const std::filesystem::path& path,std::stop_token stop){
        // Hardware decoding and the bounded prefetch queue overlap source I/O
        // with neural rendering. Queue order preserves temporal history.
        return decoder.Open(path.wstring(),MediaSourceKind::LocalFile,stop);
    }
    void Close(){decoder.Close();}
    JobRead Read(JobFrame& frame,std::stop_token stop){
        const auto started=SteadyClock::now();
        for(;;){
            VideoFrame decoded;const auto read=decoder.ReadNextAvailable(decoded,stop);
            if(read==VideoReadResult::NotReady){std::this_thread::sleep_for(std::chrono::milliseconds(1));continue;}
            readSeconds+=std::chrono::duration<double>(SteadyClock::now()-started).count();
            frame={std::move(decoded.bgra),decoded.timestamp100ns,decoded.discontinuity};
            if(read==VideoReadResult::FrameReady)return JobRead::FrameReady;
            if(read==VideoReadResult::EndOfStream)return JobRead::EndOfStream;
            if(read==VideoReadResult::Cancelled)return JobRead::Cancelled;
            return JobRead::Error;
        }
    }
};

struct ProductionEvaluatorAdapter {
    D3D12RendererOwner renderer;uint64_t successfulEvaluations{};
    TemporalGuideGenerator guides;
    double guideSeconds{},renderSeconds{};uint64_t profiledFrames{};
    uint32_t width{},height{};double fps{};bool forceReset{true},stableVideo{false};
    bool Initialize(HWND window,uint32_t w,uint32_t h,double rate){
        if(renderer&&width==w&&height==h&&fps==rate)return true;
        width=w;height=h;fps=rate;const auto [gridW,gridH]=TemporalGuideGenerator::AnalysisGrid(w,h,rate);
        renderer=MakeD3D12Renderer();
        if(!renderer||!renderer->Initialize(window,w,h,w,h,gridW,gridH,DefaultNeuralCarrierQuality()))return false;
        renderer->SetDLSS(true);
        // Decoded video has no genuine jittered samples or geometry depth.
        // Match the unjittered live DLAA path; don't invent moving depth from
        // image gradients/flow, which can make neural video geometry breathe.
        renderer->SetSyntheticJitter(!stableVideo);
        if(stableVideo)guides.SetDepthMode(TemporalGuideGenerator::DepthMode::Flat);
        return true;
    }
    bool Submit(const JobFrame& frame,bool reset,bool capture,std::vector<uint8_t>& output,bool queued=false){
        const auto started=SteadyClock::now();
        // Estimated video flow is not reliable enough for cross-frame neural
        // reconstruction: even a confidence mask left visible trails in motion
        // tests. Buffered mode evaluates each source frame independently while
        // retaining the GPU/model session (no feature recreation).
        GuideFrame guide;bool temporalReset=forceReset||reset||stableVideo;forceReset=false;
        if(!guides.Generate(frame.bgra.data(),width,height,width,height,fps,temporalReset,guide))return false;
        temporalReset=temporalReset||!guide.hasHistory; // Propagate detected cuts to NGX, not only the guide field.
        const auto prepared=SteadyClock::now();
        guideSeconds+=std::chrono::duration<double>(prepared-started).count();
        const float frameMs=static_cast<float>(1000.0/fps);
        if(!capture){const bool ok=renderer->RenderFrame(frame.bgra.data(),frame.bgra.size(),
            guide.guideGridRGBA32F.data(),guide.guideGridRGBA32F.size()*sizeof(float),
            guide.gridW,guide.gridH,temporalReset,frameMs);if(ok)++successfulEvaluations;return ok;}
        if(queued){
            const bool ok=renderer->QueueFrameForCache(frame.bgra.data(),frame.bgra.size(),guide.guideGridRGBA32F.data(),guide.guideGridRGBA32F.size()*sizeof(float),guide.gridW,guide.gridH,temporalReset,frameMs);
            renderSeconds+=std::chrono::duration<double>(SteadyClock::now()-prepared).count();
            if(ok){++successfulEvaluations;++profiledFrames;}return ok;
        }
        CapturedVideoFrame captured;
        if(!renderer->RenderFrameForCache(frame.bgra.data(),frame.bgra.size(),
            guide.guideGridRGBA32F.data(),guide.guideGridRGBA32F.size()*sizeof(float),
            guide.gridW,guide.gridH,temporalReset,frameMs,captured))return false;
        renderSeconds+=std::chrono::duration<double>(SteadyClock::now()-prepared).count();++profiledFrames;
        output=std::move(captured.bgra);++successfulEvaluations;return true;
    }
    bool FeatureCreated()const{return renderer&&renderer->DLSSFeatureCreated();}
    uint64_t EvaluationCount()const{return successfulEvaluations;}
    bool Enqueue(const JobFrame& frame,bool reset){std::vector<uint8_t> ignored;return Submit(frame,reset,true,ignored,true);}
    bool Resolve(std::vector<uint8_t>& output){CapturedVideoFrame frame;if(!renderer->ResolveOldestCapture(frame))return false;output=std::move(frame.bgra);return true;}
    void ResetTemporal(){CapturedVideoFrame discard;while(renderer&&renderer->ResolveOldestCapture(discard)){}guides.Reset();forceReset=true;}
};

struct ProductionEncoderAdapter {
    RawVideoEncoder encoder;
    std::mutex mutex;
    std::condition_variable_any cv;
    std::deque<std::vector<uint8_t>> queue;
    std::jthread worker;
    EncodeError error{EncodeError::None};
    bool finishing{};double enqueueSeconds{},finishSeconds{};
    ~ProductionEncoderAdapter(){Cancel();}
    EncodeError Start(const EncoderSpec& spec,const std::filesystem::path& path){
        Cancel(); error=encoder.Start(spec,path); finishing=false;
        if(error!=EncodeError::None)return error;
        worker=std::jthread([this](std::stop_token stop){
            for(;;){
                std::vector<uint8_t> frame;
                {
                    std::unique_lock lock(mutex);
                    if(!cv.wait(lock,stop,[this]{return finishing||!queue.empty();}))return;
                    if(queue.empty())return;
                    frame=std::move(queue.front());queue.pop_front();cv.notify_all();
                }
                auto result=encoder.WriteFrame(frame,stop);
                if(result!=EncodeError::None){std::lock_guard lock(mutex);error=result;cv.notify_all();return;}
            }
        });
        return EncodeError::None;
    }
    EncodeError WriteFrame(std::vector<uint8_t>& frame,std::stop_token stop){
        const auto started=SteadyClock::now();
        std::unique_lock lock(mutex);
        if(!cv.wait(lock,stop,[this]{return queue.size()<3||error!=EncodeError::None;}))return EncodeError::Cancelled;
        if(error!=EncodeError::None)return error;
        queue.emplace_back(std::move(frame));cv.notify_all();
        enqueueSeconds+=std::chrono::duration<double>(SteadyClock::now()-started).count();return EncodeError::None;
    }
    EncodeError Finish(std::stop_token stop){
        const auto started=SteadyClock::now();
        std::stop_callback cancel(stop,[this]{worker.request_stop();cv.notify_all();});
        {std::lock_guard lock(mutex);finishing=true;cv.notify_all();}
        if(worker.joinable())worker.join();
        if(stop.stop_requested())return EncodeError::Cancelled;
        if(error!=EncodeError::None)return error;
        const auto result=encoder.Finish(stop);finishSeconds+=std::chrono::duration<double>(SteadyClock::now()-started).count();return result;
    }
    void Cancel(){
        if(worker.joinable()){worker.request_stop();cv.notify_all();worker.join();}
        encoder.Cancel();queue.clear();
    }
};

std::filesystem::path ModuleDirectory()
{
    std::wstring path(32768,L'\0');const DWORD length=GetModuleFileNameW(nullptr,path.data(),DWORD(path.size()));
    if(!length||length>=path.size())return {};path.resize(length);return std::filesystem::path(path).parent_path();
}

std::string ReadLogSegment(const std::filesystem::path& path,uintmax_t offset)
{
    constexpr uintmax_t Limit=4u*1024u*1024u;std::string latest;
    uintmax_t lastSegmentSize=std::numeric_limits<uintmax_t>::max();
    int stableSamples=0;
    for(int attempt=0;attempt<20;++attempt){
        std::error_code error;const auto size=std::filesystem::file_size(path,error);
        if(!error&&size>=offset&&size-offset<=Limit){
            const uintmax_t segmentSize=size-offset;
            std::ifstream input(path,std::ios::binary);
            if(input){input.seekg(static_cast<std::streamoff>(offset));
                latest={std::istreambuf_iterator<char>(input),std::istreambuf_iterator<char>()};
                const auto evidence=ParseNeuralRuntimeEvidence(latest);
                stableSamples=segmentSize==lastSegmentSize?stableSamples+1:1;
                lastSegmentSize=segmentSize;
                if((evidence.Valid()||evidence.laterFailure)&&stableSamples>=3)return latest;
            }
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(100));
    }
    return latest;
}
#endif

} // namespace

NeuralRuntimeEvidence ParseNeuralRuntimeEvidence(std::string_view reshadeLogSegment)
{
    NeuralRuntimeEvidence evidence;const std::string lower=LowerAscii(reshadeLogSegment);
    evidence.upscalingOff=lower.find("active settings: upscaling=off")!=std::string::npos;
    evidence.inlineInterceptionContract=
        lower.find("enablehooks=2: ngx hooks only")!=std::string::npos&&
        lower.find("private feature-18 gpu ordering active")!=std::string::npos;
    const size_t created=lower.find("feature 18 created");
    const size_t evaluated=lower.find("inline feature 18 evaluation succeeded");
    evidence.feature18Created=created!=std::string::npos;
    evidence.feature18Evaluated=evaluated!=std::string::npos;
    const std::array<std::string_view,9> failures{
        "feature 18 create failed","feature 18 evaluation failed",
        "inline feature 18 evaluation failed","feature 18 evaluate raised an exception",
        "nr skipped:","nr declined an evaluate:","nr workset pool exhausted",
        "the game dlss output was retained","nr is paused for this feature"};
    for(const auto failure:failures){
        if(lower.find(failure)!=std::string::npos)evidence.laterFailure=true;
    }
    evidence.highestObservedEvaluation=HighestEvaluationCount(lower);
    return evidence;
}

#ifdef OFFLINE_NEURAL_RENDERER_TESTING
OfflineNeuralRenderer::OfflineNeuralRenderer(
    IFrameSource& source,INeuralFrameEvaluator& evaluator,IFrameEncoder& encoder,
    std::function<std::string()> evidenceProvider,Clock clock)
    : testSource_(&source),testEvaluator_(&evaluator),testEncoder_(&encoder),
      testEvidenceProvider_(std::move(evidenceProvider)),testClock_(std::move(clock)) {}
#endif

NeuralRenderResult OfflineNeuralRenderer::Run(const NeuralRenderRequest& request,
                                               ProgressCallback progress,std::stop_token stop)
{
#ifdef OFFLINE_NEURAL_RENDERER_TESTING
    if(!testSource_||!testEvaluator_||!testEncoder_||!testEvidenceProvider_)
        return NeuralRenderResult{.detail=L"Offline renderer test dependencies are incomplete."};
    TestSourceAdapter source{*testSource_};TestEvaluatorAdapter evaluator{*testEvaluator_};
    TestEncoderAdapter encoder{*testEncoder_};
    const Clock clock=testClock_?testClock_:[]{return SteadyClock::now();};
    return RunJob(request,std::move(progress),stop,source,evaluator,encoder,
                  testEvidenceProvider_,clock);
#else
    const auto logPath=ModuleDirectory()/L"ReShade.log";std::error_code error;
    uintmax_t logOffset=std::filesystem::file_size(logPath,error);if(error)logOffset=0;
    static thread_local std::unique_ptr<ProductionEvaluatorAdapter> session;
    ProductionSourceAdapter source;ProductionEvaluatorAdapter oneShot;ProductionEncoderAdapter encoder;
    const bool continuation=request.reuseSession&&session!=nullptr;
    if(request.reuseSession){
        if(session&&(session->width!=request.width||session->height!=request.height||session->fps!=request.fps))
            return NeuralRenderResult{.detail=L"Buffered source format changed. Restart playback to rebuild the render session."};
        if(session)logOffset=0; // Include the existing feature's creation receipt.
        else session=std::make_unique<ProductionEvaluatorAdapter>();
    }
    auto& evaluator=request.reuseSession?*session:oneShot;
    evaluator.stableVideo=request.reuseSession;
    evaluator.guideSeconds=0;evaluator.renderSeconds=0;evaluator.profiledFrames=0;
    auto result=RunJob(request,std::move(progress),stop,source,evaluator,encoder,
        [logPath,logOffset]{return ReadLogSegment(logPath,logOffset);},
        []{return SteadyClock::now();},continuation);
    if(result.ok&&evaluator.profiledFrames){
        const double ms=1000.0/evaluator.profiledFrames;
        std::cout<<"DLSS_PROFILE frames="<<evaluator.profiledFrames<<" decode_wait_ms="<<source.readSeconds*ms
            <<" guides_ms="<<evaluator.guideSeconds*ms<<" render_readback_ms="<<evaluator.renderSeconds*ms
            <<" encoder_enqueue_ms="<<encoder.enqueueSeconds*ms<<" encoder_finish_s="<<encoder.finishSeconds<<std::endl;
    }
    if(request.reuseSession&&!result.ok)session.reset();
    return result;
#endif
}
