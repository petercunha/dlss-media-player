# Render pipeline performance — September 10, 2026

Test machine: RTX 5070 Ti 16 GB, driver 616.64. Source for live checks: the user-supplied Twitch channel syanne, selected at 1920×1080/60 fps. HDR and MPV display smoothing were active. Live samples are short and sequential, with different source content; they are observations, not controlled benchmark averages.

## Changes

- Preparation/resize, serial neural rendering and packaging now overlap. Each interstage queue holds one waiting chunk, in addition to each stage's active chunk. Capture and completed-output queues remain bounded. The GPU neural session and frame order are unchanged.
- Full-size neural output is remuxed without another decode/encode. The native buffered intermediate now uses MP4 with a 90 kHz clock; the previous Matroska millisecond clock failed the existing strict cadence test when copied. Non-buffered exports retain their previous container.
- A verified retained feature skips an unnecessary decoder reopen when no priming frame was consumed. Real resets and encoder retry still reopen/reset as before.
- Encoding takes ownership of the captured frame's memory rather than copying the entire frame into its bounded queue. Progress byte accounting uses the validated size before ownership transfer.
- Logs expose preparation, neural and packaging wall time, plus native mean decoding wait, guide generation, render/readback, encoder enqueue and encoder finish time. Startup samples include priming/receipt captures and should not be compared directly to steady chunks.
- Cancellation awaits all pipeline stages and briefly retries deletion while Windows releases child-process file handles.

## Measurements

A repeatable ten-second, 300-frame moving fixture, rendered at 1920×1080/30 fps, took about 12 seconds of chunk processing before this pass and 10.85 seconds including capture/prepare startup after initial pipelining/remux changes. That comparison preceded the decoder-reopen and frame-memory changes.

With the live 1080p60 source and 1080p neural output:

| Sample | Steady neural chunk time (4 s video) | Neural rate |
| --- | --- | --- |
| Pipelining/remux changes only | 4.15 s | 0.96× |
| Plus decoder reuse and frame ownership transfer | 3.66–3.68 s | 1.09× |

Final 1080p stage means were approximately 0.01–0.08 ms decode wait, 1.72–1.75 ms guides, 10.28–10.42 ms render/readback and 0.47 ms encoder enqueue per captured frame. Packaging took 0.15–0.16 seconds per four-second chunk and overlaps subsequent rendering. The render/readback metric is CPU wall time around submission, presentation, GPU synchronization and readback; it is not a GPU timestamp query.

At 3840×2160, 100% neural work, the same 1080p60 live source remained below realtime: roughly 0.40× on steady chunks. Rendering/readback alone was about 35.5 ms per frame. Four chunks opened successfully with HDR and smoothing, but the first cancellation test left temporary files; that led to the bounded cleanup retry and a stricter live harness.

## What remains

The renderer still synchronously waits for each evaluated frame's GPU readback. Decode and encode workers overlap, but this is not a fully asynchronous GPU pipeline. A deeper redesign could use multiple readback slots and explicit fences, preserve serial neural history on its GPU queue, and ultimately feed GPU textures directly to NVENC. More independent neural workers would duplicate model/history state and are not a safe substitute for that work. No claim of sustained full-quality 4K60 or 100% GPU utilization is made.

## Verification

- Native NeuralPrerenderTests passed.
- Exact decoded pixel IDs 0–299 and 33.333/33.334 ms timestamp steps passed across the remuxed chunk boundary; no test tolerance was relaxed.
- LivePerformanceTests exercises four chunks, automatic Streamlink routing, active MPV smoothing, HDR and cancellation. It now asserts that no new cache directory remains.
- Test captures, binaries and raw machine logs are excluded from Git.
