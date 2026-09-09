# Third-party notices

- Live feeder: [jlrouzies-fr/DLSS5-Feeder](https://github.com/jlrouzies-fr/DLSS5-Feeder), MIT. Copyright and license retained in launcher-source/live-rtx-feeder/LICENSE. Local modifications add post-neural NVIDIA D3D11 video processing. Upstream includes attributed bridge and other integration code.
- Offline renderer: [2600th/dlss5-video-player](https://github.com/2600th/dlss5-video-player), MIT. License and third-party notices retained in launcher-source/native-export/.
- NVIDIA's video-processor extension ABI is also used by [MPV d3d11vpp](https://github.com/mpv-player/mpv/blob/master/video/filter/vf_d3d11vpp.c) and Chromium. The local integration invokes this driver interface directly.
- NVIDIA SDK/runtime/model files retain NVIDIA's terms and are provisioned separately, not included in Git.
- MPV, FFmpeg, ReShade, LumeniteFX, yt-dlp, Deno, Streamlink and RIFE retain their respective upstream licenses. Consult their upstream projects before redistributing a complete portable bundle.
