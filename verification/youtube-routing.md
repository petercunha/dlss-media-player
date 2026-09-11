# YouTube premature exit — 2026-09-11

The reported regular YouTube video was claimed by Streamlink's YouTube plugin.
Streamlink offered a 360p progressive stream, reached transfer EOF quickly, then
closed its MPV child. The launcher received exit code 0 after 4.79 seconds even
though the video duration was 232 seconds. This was a routing/lifecycle error,
not a neural renderer crash or the reverted buffered-VOD feature.

Automatic YouTube routing now checks yt-dlp's `live_status` without downloading.
Actual live broadcasts retain Streamlink routing; regular videos use MPV's
yt-dlp integration, including separate video/audio formats and requested quality.
Unknown/failed status resolution also leaves playback with MPV/yt-dlp.

The reported URL stayed open through a 25-second test, until intentional test
cancellation. MPV decoded video/audio and the custom renderer logged successful
DLSS followed by RTX HDR. A transient A/V desynchronization warning occurred at
fullscreen startup; this test does not establish sustained realtime rendering.
Twitch routing, hostname validation and existing live-control tests passed.

`YouTubeVodTests.cs` exercises this regression with a supplied regular YouTube
video longer than 30 seconds. It requires Windows HDR and the configured neural
runtime. The buffered-playback rollback remains in place.
