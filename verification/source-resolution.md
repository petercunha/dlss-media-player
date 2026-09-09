# Source-resolution live mode

Verified on RTX 5070 Ti, driver 616.64, 3456×2234 HDR display.

- Local 1280×720 synthetic video: source-mode DLSS feature/input/output remained 1280×720; RTX processing targeted 3456×2234. Neural evaluation and HDR processing checks passed.
- Twitch through Streamlink at 720p: source crop was (1088,757), 1280×720 within the fullscreen backbuffer. Both NVIDIA VSR and HDR indicators were visible. This run ended before the timed cancellation assertion, so it is not a passing sustained-stream test. MPV also reported an audio/video desynchronization warning during startup.
- Local windowed source mode with VSR alone: 1280×720 → 1280×720, VSR indicator visible. The final native build passed processing and clean end-of-file checks.
- Manual 50% mode: retained the 640×360 → 1280×720 path and passed processing/end-of-file checks.
- Launcher regression checks passed: old settings migrate to source mode with VSR; saved manual choices survive subsequent launches; switching VSR on selects source mode; manual and HDR-only playback retain their existing MPV scaling path.

Source mode copies the video rectangle from MPV's unscaled render into the neural textures on the GPU. It does not resample an enlarged image back down. RTX presentation fits the enhanced source into the display while preserving aspect ratio. The source bridge passes dimensions only, using a unique child-scoped temporary file that is cleaned up after playback.

NVIDIA extension success is logged as a request, not proof of VSR activation. Sources above the documented 1440p input range may still be declined. These checks establish routing and activation, not a sustained performance or image-quality benchmark.
