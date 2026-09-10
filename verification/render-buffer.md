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

## September 9 follow-up: automatic routing and temporal stability

- Live playback now detects Twitch and installed Streamlink plugins automatically, before buffered URL downloading. UI routing rejects lookalike Twitch hosts and keeps display smoothing available. AutoRoutingTests passed.
- An actual buffered run of the supplied Twitch channel succeeded at 720p60 with a five-second target buffer: retained neural worker, enhanced output, audio, RTX HDR and MPV reporting interpolation=yes and display-sync-active=yes. Cancellation cleaned up. A later unbuffered check found the channel unavailable; that check verified the Streamlink error path, not unbuffered playback.
- A synthetic 300-frame 640x360/30 fps moving fixture embeds pixel frame IDs. BufferedOrderTests captured two enhanced segments. Pixel IDs were exactly 0 through 299, with no duplicates/skips; decoded timestamps advanced 0.033333–0.033334 seconds across the boundary. This checks ordering and cadence, not absence of perceptual artifacts. The marker decoder deliberately tolerates dim neural ghosting.
- Persistent video mode disables synthetic camera jitter and uses a flat depth guide. Verified consecutive chunks retain neural history; detected scene cuts reset NGX history. Software retry starts a fresh verification baseline. Segment offsets and lengths use rendered frame counts, avoiding cumulative AAC duration padding.
- MPV display smoothing options now come from the same function for normal, Streamlink and buffered playback. config/playback-state.lua logs actual active display synchronization. Normal live playback already retained its renderer; this revision does not replace that renderer.
- NeuralPrerenderTests passed. Native worker shutdown now pumps window messages through actual worker-thread termination, including thread-local GPU teardown.

These findings supersede the earlier unavailable-channel and per-chunk-history-reset limitations above. Buffered guide generation remains different from live rendering; identical visual quality and complete elimination of warping are not established.

To repeat the frame-order check, run tests/order-fixture.py with the player root and a scratch directory; run BufferedOrderTests with player root, generated order-source.mp4 and an empty capture directory; then run tests/check-frame-order.py with player root and captured joined.ts. The Python tools require NumPy and Pillow. Compile the C# harnesses alongside launcher-source/*.cs, choosing the harness class as /main. No test media or stream captures are committed.
