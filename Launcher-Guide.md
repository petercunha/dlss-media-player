# DLSS Media Launcher — smoothing and export

Open **DLSS-Media-Launcher.exe** (or **DLSS Media Launcher.lnk**). Reopen the launcher to get the new controls; an already-open older launcher keeps its previous interface.

## Quick start

1. Browse for a video, drag one onto the window, or paste a video URL.
2. Choose **Live playback**, **Prepare enhanced & play**, or **Export enhanced MP4**.
3. Choose a motion option and press the main action button.

Your option selections persist across launches. Submitted file paths and URLs are not saved in launcher settings.

## Motion options

| Option | Behavior |
| --- | --- |
| Off · original frames | Keeps the source frame rate. |
| Display smoothing (live) | MPV synchronizes to the display and blends around frame transitions to reduce cadence judder. This is not RIFE or DLSS Frame Generation. Press **F8** during playback to toggle it. It changes display output, not exported frame counts. |
| RIFE 2× (prepare first) | Generates intermediate frames with RIFE v4.6 on the GPU: 24→48, 25→50, 30→60, etc. Finishes preparing the video before playback or export. Selecting RIFE with Live playback automatically uses Prepare & play. |

**For maximum playback smoothness:** choose Prepare enhanced & play + RIFE 2×. The saved result includes DLSS enhancement and generated frames. Playback uses a separate MPV copy that does not reapply the neural effect, so the GPU does not have to perform neural rendering while watching.

**For a quick live improvement:** choose Display smoothing and try 75% in Live DLSS work size. That reduces the private neural-rendering dimensions before expanding the output to the player window. 100% gives full work resolution; 50% is faster with more detail loss. Bigger player windows cost more GPU time. Display smoothing can add rendering work, so it does not fix every performance bottleneck.

RIFE can create artifacts around cuts, occlusions and fast motion. It requires preparation time, GPU resources and temporary disk space. The original file is unchanged. Temporary PNGs are processed in bounded chunks and removed as each chunk completes.

## Export

Choose Export enhanced MP4, then select a new destination filename. Offline URL jobs download the source first; Stream only is not used for offline jobs.

The export pipeline:

1. Check the source format. Ordinary SDR videos skip the intermediate re-encode. Rotation or odd dimensions are corrected with NVENC when needed; constant frame timing is produced during decoding.
2. Apply actual DLSS neural enhancement through an isolated native renderer and verify its per-frame neural receipts.
3. Optionally generate 2× frames with RIFE.
4. Resize when requested, encode H.264 MP4 and include the first source audio track as AAC.

Export size can retain the source size or enlarge to at least 1080p, 1440p or 2160p. **Enlargement uses Lanczos scaling of the DLSS-enhanced result. It is not DLSS Super Resolution.** Smaller targets do not downscale a larger source. Neural enhancement runs at source resolution; its guides differ from MPV's live Feeder path. Current RenoDX neural settings are copied for each job, with neural enhancement explicitly enabled.

Exports are SDR, 8-bit H.264. HDR sources are rejected for offline processing instead of being silently misinterpreted. Subtitles, chapters, multiple audio tracks and HDR metadata are not preserved by this exporter. Very long inputs need substantial temporary storage. Live streams have no finite export duration and should use live playback.

Prepare & play keeps its finished MP4 in **Exports**. Manual export uses your chosen path, defaulting to Exports. Open output folder shows Exports. The original downloads remain in Downloads. Finished MP4s play in ordinary media players without DLSS hardware.

Stop cancels the operation and its helpers. Closing a busy launcher cancels before closing. Partial exports are not published; existing destination files are never overwritten. Concurrent offline renders are refused to protect the neural runtime's shared configuration and logs.

## URL playback

Auto streams through MPV + yt-dlp and downloads first if the player exits with a playback error. Closing the player normally does not trigger a download. Stream only and Download first remain available for live playback. The source quality menu controls downloaded/streamed quality, separately from export size. Unsupported, protected or sign-in-only videos may still fail; the launcher does not automatically import browser cookies.

### Streamlink live

Choose **Streamlink live** in Action, paste a supported channel or protocol URL, choose the source quality and DLSS work size, then Start. Streamlink feeds the bundled DLSS-enabled MPV over local HTTP. Twitch and other Streamlink plugins use this same route. RIFE, Prepare and Export do not apply to this mode. Stop cancels Streamlink and its player; closing the player also ends the stream.

Streamlink 8.4.0 is bundled. Optional site-specific settings can be placed in `streamlink.conf` beside the launcher; system Streamlink configuration is otherwise ignored. Authentication, offline channels and site restrictions can still prevent playback. Quality caps prefer the highest available named resolution and frame rate, falling back to best when the site has no matching named resolution.

Verified the supplied Twitch channel at 720p60 through Streamlink, with successful DLSS neural evaluations in ReShade and process-tree cancellation.

### Export performance update

The separate CPU timing re-encode is removed for ordinary inputs, whether RIFE is on or off. Variable-rate sources are still converted to a constant output cadence while decoding; exact variable-frame timestamps are not preserved.

DLSS uses queued hardware decoding, parallel CPU motion-guide generation and a bounded background encoder queue. Neural evaluations remain in temporal order. NVENC uses the faster p4 preset with quality-based encoding. Hardware decoder fallbacks remain available.

RIFE explicitly uses Vulkan device 0 (verified as the RTX 5070 Ti on this PC), with 4 load / 4 inference / 4 save workers through 1440p, and 2 inference workers above that to limit VRAM pressure. Next-chunk extraction and previous-chunk NVENC encoding overlap inference. Extraction uses uncompressed PNGs; frame counting reads encoded packets without decoding the whole video. Same-size RIFE exports copy the encoded video into the final MP4 instead of encoding it again. Lossless image intermediates still incur disk and CPU work.

Short synthetic 1080p benchmark: 120 neural frames took 11.2 seconds before and 8.5 seconds after; complete DLSS + RIFE export took 22.7 seconds before and 14.3 seconds after. Both RIFE outputs decoded to 240 frames at 60 fps with a four-second duration. These are single-run measurements on this PC, not guaranteed speedups or 100% GPU utilization. Faster NVENC presets change the compression tradeoff.

Regression checks covered a RIFE chunk boundary with audio, variable-rate input, 90-degree rotation, and cancellation during the pipelined RIFE stage. Details are in `verification/performance-update.md`.

## Frame-generation alternatives

The installed ReShade/Feeder setup does not expose DLSS Frame Generation. NVIDIA Smooth Motion is a separate driver-level feature for compatible applications, and is not wired to this launcher's switch. The implemented choices are MPV display smoothing and offline RIFE 2×.

## Verification on this PC

RTX 5070 Ti, driver 616.64:

- Native neural exporter verified 48/48 frames on a two-second clip.
- DLSS + RIFE test: 144 input frames became 288 frames at 48 fps; duration remained exactly six seconds and an audio track was present. Tested across a chunk boundary.
- Export enlargement produced 1280×720 output from a smaller source.
- Prepared 48 fps playback with display smoothing completed normally.
- Cancellation left no published partial export or temporary frame directory.
- Concurrent offline render attempts were rejected.
- Native GUI layout inspected; compiled launcher self-check passed.

These are short synthetic tests, not a guarantee of performance or image quality for every movie or website. Test evidence is in verification. Source and rebuild scripts are in launcher-source; the native exporter is adapted from the MIT-licensed 2600th project, with its source revision recorded in native-export/BUILD-NOTES.txt.

## Sources

- [MPV interpolation and display synchronization](https://mpv.io/manual/stable/)
- [RIFE ncnn Vulkan](https://github.com/nihui/rife-ncnn-vulkan)
- [Native offline DLSS renderer](https://github.com/2600th/dlss5-video-player)
- [NVIDIA Smooth Motion](https://nvidia.custhelp.com/app/answers/detail/a_id/5621/~/enabling-smooth-motion-in-nvidia-app)
- [yt-dlp](https://github.com/yt-dlp/yt-dlp)

### Direct-download URL fix

Download names now use a bounded title and a stable URL/quality hash. Extractor IDs are not put into filenames, because some direct-download services return signed URLs or malformed Content-Disposition values as IDs. This prevents invalid or excessively long Windows paths in Prepare and Export. A different source-quality setting receives a different cache filename.
Verified the reported direct-download endpoint: the complete 439 MiB file downloaded and probed successfully as 3840×2160, 60 fps, 197.226 seconds.

## Live RTX processing and output size

The launcher now has separate Live Output Size and Export Size controls. Live Output Size offers window fit, 1080p/1440p/2160p bounds, and Display · fullscreen. Use F in MPV to leave fullscreen. Resizing changes the live render dimensions; export dimensions affect saved files only.

Live RTX Processing offers Off, VSR, HDR, or both. The modified feeder applies NVIDIA video processing directly to the completed neural texture, before final presentation: DLSS → VSR/HDR → display. Set DLSS work size to 50% as a useful starting point with VSR for final enlargement. Processing is kept on the GPU. HDR requires Windows HDR enabled on the playback display; the log reports actual activation or fallback. The mode uses an SDR intermediate, so native HDR content is tone-mapped before neural enhancement and HDR expansion.

Verified local 640×360 neural output enlarged to 1280×720 with VSR/HDR, and Twitch through Streamlink at full 3456×2234 display resolution. Both NVIDIA VSR/HDR indicators were visible and neural evaluations were logged. The original sample channel was offline during this test, so Lofi Girl's live channel was used. A transient audio underrun occurred. This is not a guarantee of continuous real-time performance at every size.

Live RTX processing is independent from offline export and is not baked into saved MP4 files. The original addon is unchanged when the launcher passes RTX mode Off. Restart MPV after changing launcher processing settings.
