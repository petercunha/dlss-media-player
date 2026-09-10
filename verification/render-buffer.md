# Render-ahead engine verification

Tested September 9, 2026 on RTX 5070 Ti 16 GB, driver 616.64, Windows HDR enabled.

## Architecture

The producer captures/decodes short chunks, scales to the selected neural work dimensions, and sends them to a persistent `NeuralExport --batch` process. Completed neural output is encoded as timestamped H.264/AAC MPEG-TS chunks. A bounded queue serves only completed output to MPV over loopback HTTP. MPV's cache pause/refill controls therefore buffer enhanced frames, not network input. The feeder's child-scoped prerendered flag bypasses live DLSS while retaining optional HDR.

The first chunk requires a fresh feature-18 receipt. Later chunks retain the exact same initialized evaluator/device, verify successful render calls and captured frame sizes, and reject runtime failure evidence. Sparse RenoDX receipts are not forced to advance at every chunk boundary; they may be hundreds of frames apart. A format change stops playback instead of reusing a mismatched session. Ordinary exports still use the original fresh-receipt validation path.

## Passed checks

- Launcher compilation, full-quality default, persisted buffer selection, migration from VSR-only and VSR+HDR settings, removal of VSR controls.
- Local 1280×720, 30 fps video: completed neural chunks delivered to MPV, HDR active, live DLSS bypass logged, finite EOF.
- Streamlink HTTP plugin fixture: 13-second 640×360/30 fps video with AAC audio. All 390 video frames were neural-rendered across four chunks; MPV opened the audio track and completed playback. `--player-no-close` is required so Streamlink lets FFmpeg flush its final chunks.
- Repeated the Streamlink fixture with 1920×1080 neural work/output: all 390 frames, audio, HDR, EOF and cancellation checks passed. Later four-second chunks ran at 1.08–1.10× realtime; startup and the short final chunk were slower.
- Normal unbuffered playback regression: live DLSS evaluation and HDR succeeded with VSR disabled.
- Deliberate 16-second producer stall using the production completed-output HTTP consumer. MPV reported `paused-for-cache=true` at 7.788 seconds and `false` after refill at 7.855 seconds, then reached EOF. Audio/video sync at the end was reported as 0.000 seconds.
- Cancellation during preparation/discovery and normal completion removed the session's cache directory. Existing unrelated cache directories were preserved.
- Image regression after the native change: 321×241 PNG, preserved alpha, verified neural output, and overwrite protection.

## Timing observations

With a fresh neural process per chunk, the initial 720p prototype rendered around 0.52–0.54× realtime. Retaining the neural device/model improved subsequent 720p chunks to 1.69–1.75×. Startup still cost about six seconds. The 360p Streamlink fixture's later four-second chunks measured around 2.05–2.25×.

These are short single-run observations, not controlled benchmark averages. Another live player was active during some early tests. They do not establish sustained 4K performance.

## Limits

- The supplied Twitch channel had no playable streams when checked. Streamlink's actual HTTP plugin/transport was exercised with a controlled local fixture; uninterrupted Twitch playback is not claimed by this test.
- HDR and display smoothing still run during presentation. Their own stalls are not pre-rendered.
- Temporary SDR encoding adds overhead and quality loss; chunk boundaries reset neural history. There is no buffered seeking or subtitle/multi-audio support.
- A sustained render rate below 1× eventually causes refill pauses. Temporary capture stops at approximately 4 GB or below 1 GB free disk space.
- Forced termination of an early failed test left a diagnostic cache directory in the isolated test workspace. Successful/cancelled runs removed their own directories; forced process termination is not equivalent to Stop.

Reproducible harnesses: `BufferedPlaybackTests.cs`, `RenderRefillTests.cs`, `SourceModeTests.cs`, and `ImageTests.cs`. Test media and raw machine logs remain outside Git.
