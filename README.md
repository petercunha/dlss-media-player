# DLSS Media Player

Windows media player for local videos, URLs, online videos, and live streams, with experimental DLSS neural enhancement, live RTX Video Super Resolution/HDR, and optional offline RIFE frame generation.

## Screenshots

| Live playback | Export with RIFE |
| --- | --- |
| [![Live playback controls with DLSS work size, output size, and RTX VSR plus HDR](docs/images/launcher-live.jpg)](docs/images/launcher-live.jpg) | [![Export controls with RIFE 2× frame generation and 1080p output](docs/images/launcher-export.jpg)](docs/images/launcher-export.jpg) |

Click either screenshot for the full-size view. Live playback offers RTX VSR/HDR after DLSS; offline export offers optional RIFE frame generation and a separate export size.

## Live playback

Open `DLSS-Media-Launcher.exe`. Select **Live playback** for a file or yt-dlp URL, or **Streamlink live** for Twitch and other Streamlink-supported sites.

- **Live Output Size**: fit the window, fit within 1080p/1440p/2160p, or **Display · fullscreen**. Press F to return to a window. Aspect ratio is preserved. Fixed fit sizes are window bounds, not encoded dimensions.
- **Live RTX Processing**: Off, RTX VSR, RTX Video HDR, or both. The custom feeder consumes the completed neural texture: **DLSS → RTX VSR/HDR → display**.
- **Live DLSS Work Size**: **Source � VSR upscale** is the default with VSR. MPV renders without enlargement; the feeder crops the video area, runs DLSS at that size, and lets VSR enlarge it to the display with aspect ratio preserved. Manual 100%, 75%, and 50% modes remain available and use percentages of the display-sized image. Source mode falls back to 100% when VSR is off.
- **Motion**: original frames or MPV display smoothing. RIFE remains an offline option.

The RTX path works on GPU textures without downloading or encoding the stream. HDR requires Windows HDR enabled on the playback display. The input is rendered as SDR BT.709 into a 10-bit surface, then converted to HDR after neural processing. Native HDR sources are tone-mapped to SDR first in this mode; it is primarily intended for SDR streams. The log reports requested RTX features, processing dimensions, and failures. A successful VSR request does not confirm driver activation; check NVIDIA�s indicator. Unsupported processing falls back to SDR DLSS output. A short DLSS warm-up also occurs after resizing. Source mode uses MPV�s rendered dimensions after rotation and pixel-aspect correction; a window smaller than the source is downscaled to fit, and odd dimensions are trimmed by at most one pixel for NV12. Inputs above the driver�s documented 1440p VSR range can still be declined. During warm-up, the unscaled picture is visible until enhancement starts.

## Prepare and export

Choose **Prepare enhanced & play** or **Export enhanced MP4**, optionally enable **RIFE 2×**, and select **Export Size**. Live modes display a separate Live Output Size control.

Offline exports use DLSS neural enhancement, optional Vulkan RIFE, NVENC encoding and source audio. Enlarged exports currently use Lanczos after enhancement, **not DLSS Super Resolution**. Live RTX VSR/HDR is not baked into exports. Ordinary inputs skip a redundant timing re-encode; variable-rate inputs become constant cadence during decoding.

Stop cancels helpers and removes partial exports. Existing destinations are never overwritten. Downloads, exports, temporary files, videos, binaries, and local settings are excluded from Git.

## Build and setup

See [BUILDING.md](BUILDING.md) for runtime dependencies and build commands, and [Launcher-Guide.md](Launcher-Guide.md) for usage. This is a source repository; NVIDIA models and third-party executable distributions must be provisioned separately. [Third-party notices](THIRD_PARTY.md).

## Verification

Tested on RTX 5070 Ti 16 GB, driver 616.64:

- Twitch via Streamlink at 720p, displayed at 3456×2234. NVIDIA's RTX VSR and RTX HDR indicators were visible alongside successful DLSS neural-evaluation logs.
- Local live test: 640×360 neural texture → RTX VSR/HDR → 1280×720.
- RIFE: 144 frames became 288 at 48 fps, retaining six-second duration and audio across a chunk boundary.
- Variable-rate input, 90-degree rotation, and cancellation during RIFE.
- Short 1080p benchmark: native DLSS 11.2 → 8.5 seconds; complete DLSS + RIFE export 22.7 → 14.3 seconds. Single runs; performance varies.

This is experimental community integration. Video uses estimated motion/depth guides and visual quality varies. It does not implement DLSS Frame Generation. RIFE retains image-file overhead. The live test had a transient audio underrun; sustained glitch-free playback at every resolution is not established.
