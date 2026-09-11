# DLSS Media Player

Windows media player for local videos, URLs, online videos, and live streams, with experimental DLSS neural enhancement, RTX Video HDR  and optional offline RIFE frame generation.

## Screenshots

| Live playback | Export with RIFE |
| --- | --- |
| [![Live playback controls](docs/images/launcher-live.jpg)](docs/images/launcher-live.jpg) | [![Export controls with RIFE frame generation](docs/images/launcher-export.jpg)](docs/images/launcher-export.jpg) |

Click either screenshot for the full-size view. These original screenshots show the earlier interface; the current version offers DLSS, Off and VSR with optional HDR.

## Live playback

Open `DLSS-Media-Launcher.exe`. Select **Live playback** for a file or URL. Twitch and other URLs recognized by an installed Streamlink plugin automatically use Streamlink. Regular YouTube videos use MPV/yt-dlp; detected YouTube live broadcasts use Streamlink. **Streamlink live** also allows explicit selection and Streamlink protocol URLs.

- **Live Output Size**: fit the window, fit within 1080p/1440p/2160p, or **Display · fullscreen**. Press F to return to a window. Aspect ratio is preserved. Fixed fit sizes are window bounds, not encoded dimensions.
- **RTX Video HDR**: Off or HDR. HDR requires Windows HDR on the playback display.
- **Live Enhancement**: **100% full quality** is the default DLSS mode. 75% and 50% reduce DLSS work dimensions. **Off** disables DLSS. **RTX VSR** uses NVIDIA video scaling instead of DLSS. HDR remains independent in all modes.
- **Motion**: original frames or MPV display smoothing. RIFE remains an offline option.

Normal playback scales in MPV, applies DLSS neural enhancement, then optionally converts SDR BT.709 to HDR on GPU textures. No rendered-video intermediate is needed in this mode. HDR failures fall back to SDR. Native HDR material is tone-mapped to SDR first when RTX HDR is selected; this mode is primarily intended for SDR sources.

VSR processes decoded frames before MPV window scaling, with up to 4× enlargement toward the selected output bounds. It skips enlargement when the source already meets those bounds. Enable RTX Video Super Resolution in NVIDIA settings; driver activation depends on source and hardware support. Buffered playback has been removed.

## Prepare and export

Choose **Prepare enhanced & play** or **Export enhanced MP4**, optionally enable **RIFE 2×**, and select **Export Size**. Live modes display a separate Live Output Size control.

Offline exports use DLSS neural enhancement, optional Vulkan RIFE, NVENC encoding and source audio. Enlarged exports currently use Lanczos after enhancement, **not DLSS Super Resolution**. RTX HDR is not baked into exports. Ordinary inputs skip a redundant timing re-encode; variable-rate inputs become constant cadence during decoding.

Stop cancels helpers and removes partial exports. Existing destinations are never overwritten. Downloads, exports, temporary files, videos, binaries, and local settings are excluded from Git.

## Images

Choose **Enhance image (PNG)** and open a local image or paste a direct HTTP(S) image URL. Choose an export size and save the enhanced PNG. Browsing to an image selects this action automatically. PNG, JPEG, WebP, BMP, TIFF, AVIF, and GIF inputs are accepted through FFmpeg; animated images use their first frame.

Images use the verified offline DLSS neural renderer, retain transparency, and preserve source dimensions unless a larger output is selected. Larger output uses Lanczos after neural enhancement. The renderer uses a temporary encoded intermediate, so the result is not a lossless pixel round-trip. VSR, HDR, and frame generation are not used for this action. Image URLs must point directly to an image and downloads are limited to 100 MB.

## Build and setup

See [BUILDING.md](BUILDING.md) for runtime dependencies and build commands, and [Launcher-Guide.md](Launcher-Guide.md) for usage. This is a source repository; NVIDIA models and third-party executable distributions must be provisioned separately. [Third-party notices](THIRD_PARTY.md).

See [performance measurements](verification/render-performance.md) for the September 10 optimization pass and remaining GPU readback limitations.

## Verification

Tested on RTX 5070 Ti 16 GB, driver 616.64:

- Historical Twitch live DLSS/RTX tests are retained in verification; VSR has since been removed.
- RIFE: 144 frames became 288 at 48 fps, retaining six-second duration and audio across a chunk boundary.
- Variable-rate input, 90-degree rotation, and cancellation during RIFE.
- Short 1080p benchmark: native DLSS 11.2 → 8.5 seconds; complete DLSS + RIFE export 22.7 → 14.3 seconds. Single runs; performance varies.

This is experimental community integration. Video uses estimated motion/depth guides and visual quality varies. It does not implement DLSS Frame Generation. RIFE retains image-file overhead. The live test had a transient audio underrun; sustained glitch-free playback at every resolution is not established.
