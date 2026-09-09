# Streamlink and export performance verification

Machine: RTX 5070 Ti 16 GB, NVIDIA driver 616.64. Short synthetic benchmarks, single run per configuration; other desktop GPU activity was not controlled.

| Measurement | Before | After |
|---|---:|---:|
| Native DLSS, 1920×1080, 120 frames | 11.218 s | 8.497 s |
| Complete DLSS + RIFE export, same 4-second source | 22.739 s | 14.323 s |

Native results verified 120/120 neural frames. Both full exports were independently decoded by ffprobe and contained exactly 240 frames at 1920×1080, 60 fps, duration 4.000 seconds. The old benchmark harness incorrectly asserted that every output must be 720p, reporting an assertion failure after successfully saving these 1080p outputs; the independent full-decode checks confirmed their actual correct geometry and timing.

Additional checks:

- 320×180, 24 fps, six seconds with AAC audio: DLSS + RIFE across the 120-frame chunk boundary produced 288 decoded frames at 48 fps, duration 6.000 seconds, with audio. Requested enlargement produced 1280×720.
- Variable-rate source: exported without a source intermediate, producing constant cadence at the probed average rate, with duration within one output frame of the source. This does not preserve individual variable-rate timestamps.
- Source carrying a 90-degree display matrix: GPU compatibility encode produced the correct 360×640 orientation at 24 fps, two seconds.
- Cancellation during RIFE: no destination published, job directories removed, helper processes exited.
- Streamlink: supplied Twitch channel opened at 720p60 in DLSS MPV; ReShade logged successful feature-18 evaluations; cancellation terminated the Streamlink/player tree. A transient audio underrun occurred during the live test.

Implementation changes: remove redundant software timing encode for ordinary geometry; enable native hardware decoding and prefetch; parallelize independent motion-guide rows; add a three-frame asynchronous encoding queue; use NVENC p4; packet-count the controlled neural intermediate; overlap next RIFE extraction and previous encoding with inference; uncompressed input PNGs; explicit NVIDIA Vulkan device and increased workers; remux same-size RIFE results instead of a final video re-encode.

Neural processing remains ordered to preserve temporal history and per-frame verification. RIFE still uses lossless PNG intermediates and incurs CPU/disk overhead; this update does not promise constant 100% GPU utilization. NVENC preset changes affect compression efficiency. No long-film or 4K performance benchmark was run.
