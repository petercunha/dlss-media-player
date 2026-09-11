# Upstream integration checkpoint — 2026-09-11

Installed DLSS SR/DLAA 310.9.1 and rebased the custom live feeder onto stable
v0.15.1 (`3f624855276c4bde55145c712782477639b30e85`). The neural reconstruction
model remains NR 310.8.SF-v2 and RenoDX remains 4.70: both match the audited
upstream runtime lock. This is not a new neural model.

The native exporter remains our pinned fork with selected upstream ports:

- A bounded, persistently mapped readback ring and per-capture fences, based on
  `fa4b7a0`. Buffered jobs queue captures and retire them in source timestamp order.
- GPU BGRA output, based on `24e1c4f`, replaces the CPU per-pixel channel swap.
- Explicit CUDA device initialization and NVENC automatic split encoding from
  `111aed1`. Existing CUDA decoding, bounded encoder queue and chunk preparation /
  rendering / packaging overlap are retained.

Each buffered source frame still resets temporal reconstruction and guide history.
The model and GPU resources remain loaded, and required presents are retained.
Normal live DLSS and postprocessing HDR remain supported; prepared video bypasses
live DLSS and receives HDR only. The feeder merge preserves source-region constants
alongside upstream's PQ bridge constants. VSR remains disabled.

GPU NV12 color conversion and a separate readback CPU worker were not ported.
This is a selected optimization pass, not a wholesale upgrade to native v0.19.
The existing `render_readback_ms` profile field includes synchronous captures and
queued submissions, but excludes deferred capture resolution; use wall-clock job
times for before/after comparisons.

## Validation

- Release native exporter and feeder compiled; native prerender tests passed.
- 300 synthetic frame IDs preserved in exact order, without duplicates or skips.
  Frame PTS increments remained 0.033333–0.033334 seconds across chunk boundaries.
- Motion fixture: maximum dark marker 16.24/255 and zero red trail pixels.
  Red-object checks also guard against BGRA/RGBA channel reversal.
- Buffered playback with resizing, display smoothing and HDR: finite EOF,
  cancellation and cache cleanup passed.
- Live local SDR playback verified neural evaluation followed by RTX HDR.

One 1080p60 comparison (240 frames per chunk) measured a warm chunk at 7.13 seconds
before and 4.53 seconds after the native ports. First-job times were 9.19 and 9.42
seconds. These are single runs under varying GPU load, not a controlled speedup
guarantee; the warm run remained below realtime. Twitch/network behavior was not
retested for this dependency update.

DLSS 310.9.1 archive SHA-256:
`aaba83b288bd145c3808e8d7a0ba03cc8c8676d18ad984b1bfa6563046a3ba37`.
Runtime binaries, test clips and raw logs remain excluded from Git.
