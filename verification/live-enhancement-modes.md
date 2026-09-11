# Live enhancement modes — 2026-09-11

The live selector retains DLSS 100%, 75%, 50% and adds Off and RTX VSR.
Both new modes bypass neural submissions in the feeder. HDR is independent.
VSR uses MPV's `d3d11vpp` NVIDIA scaling extension on decoder frames, avoiding
the former RGB-to-NV12 VSR path. The Lua controller targets selected output bounds
with up to 4× scaling and skips enlargement when no upscale is needed.
Driver activation still depends on NVIDIA settings and source/hardware support.

Removed the render-buffer UI, C# capture/render/server pipeline and native batch
CLI. Old buffer settings are ignored; old DLSS/HDR selections are retained.
Prepare/export and RIFE remain supported. Historical buffer verification files
describe retired versions and are not current features.

Verified:
- Off with HDR disabled and enabled: playback completed, no neural evaluations.
- VSR filter accepted, decoder video scaled from 640×360 to 1920×1080, no neural
  evaluations, and optional HDR processed the resulting MPV output.
- Normal DLSS+HDR playback passed its neural receipt and HDR checks.
- Ordinary export preserved 640×360, 30 fps and 10-second duration with audio.
- Fresh defaults, legacy settings migration and persistence of Off/VSR passed.
- Launcher, feeder and exporter rebuilt; the exporter no longer accepts --batch.

VSR support: [MPV d3d11vpp documentation](https://mpv.io/manual/master/#video-filters-d3d11vpp).
