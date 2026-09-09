# Live RTX integration verification

- Custom feeder based on v0.14.0-beta.5, adding D3D11 video processing after the fenced DLSS output becomes available.
- Local SDR test: neural 640×360 → RTX VSR + HDR → RGB10 1280×720 output. NVIDIA HDR support query and processor operations succeeded; neural feature-18 evaluations continued.
- Twitch Streamlink test: 720p30 source → neural work 1728×1116 → output 3456×2234. Both NVIDIA RTX VSR and RTX HDR on-screen indicators were visible. Cancellation ended the Streamlink/MPV process tree.
- Original example channel and Monstercat were unavailable; Lofi Girl was live and used for the successful test.
- GUI preview checked at desktop DPI: independent live/export size controls, RTX selector, hint, buttons and log fit the window.
- Launcher self-test and C# compile passed. Native feeder compiled successfully with MSVC; source and binary are included locally, with binaries excluded from Git.
- Windows HDR was already enabled; no OS HDR setting was changed. Screenshot appearance is not a calibrated HDR measurement. A transient audio underrun occurred.
