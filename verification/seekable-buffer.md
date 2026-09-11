# Seekable buffered video — 2026-09-11

Finite files and URL videos now use a complete VOD HLS timeline backed by on-demand
rendering. MPV knows the full duration before all sections are rendered. Requests
for an uncached section extract and enhance that section, using the retained neural
worker. They do not render the intervening video. Completed sections are cached
with a 512 MiB eviction target. Streamlink live capture keeps its existing queue.

Direct HTTP video is probed and range-read by FFmpeg. Site pages are resolved by
yt-dlp without downloading; selected separate video/audio URLs and HTTP headers
are passed to FFmpeg. The explicit Download first setting remains available.
Efficient remote seeking requires a seekable source/server. Extracted URLs can
still fail when authentication expires or a site restricts access.

## Verified

- MPV reports the full 60.006771-second duration and `seekable=true` on a synthetic
  fixture. Exact seeks to 45s, back to 2s, then to 54s finish rendering and leave
  cache pause. Screenshots contain the corresponding source-frame markers.
- The same test passes over a local HTTP range server. Initial metadata/first
  section read about 397 KB of a 2.13 MB source before playback; later requests
  jump to offsets near the seek destination. No full source download is performed.
  This tests direct HTTP, not every yt-dlp site or authentication scheme.
- Backward-seek testing exposed negative DTS wrapping at the 33-bit MPEG-TS
  boundary. A consistent positive timestamp origin fixes it.
- Full playback: 300 ordered frame IDs, no duplicates/skips, timestamp steps
  0.033333–0.033334s across sections. Motion test: maximum dark marker 15.53/255,
  no red trails, red object/channel check passed.
- 50% work size with 1080p output, display smoothing and HDR: verified neural
  output, HDR-only presentation, audio, finite EOF, cancellation and cleanup.

`BufferedSeekTests.cs` takes player root, a 60-second local file/direct URL, and
optionally an existing screenshot directory. It waits for seeking/cache pause to
finish; checking `time-pos` alone would falsely pass before a seek was rendered.

## Shimmering

Source extraction for VOD and pre-DLSS resizing now use lossless intermediates.
Resizing uses bilinear rather than Lanczos, matching MPV's default filter and
avoiding an extra source of ringing. Final enhanced segments are still encoded.
These changes preserve the independent-frame motion fix. They do not restore the
live renderer's temporal history, so they cannot guarantee identical temporal
detail stability. The user's specific shimmer has not yet been reproduced or
visually verified as fixed; a matching source/timestamp is needed for comparison.
