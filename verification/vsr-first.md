# VSR before DLSS (supersedes source-resolution mode ordering)

Tested on RTX 5070 Ti, driver 616.64, 3456×2234 HDR display using synthetic local video.

| Input and output | Observed pipeline | Result |
| --- | --- | --- |
| 720p, fullscreen | VSR 1280×720 → 1920×1080; DLSS Quality 1920×1080 → 3456×1944; neural feature 18 at output size; HDR into letterboxed display | Passed |
| 1080p, smaller window | Original 1920×1080 remains VSR-eligible; rendered video is 1280×720 and receives a same-size cleanup request | Passed |
| 1440p, fullscreen | VSR skipped; DLSS Quality 2560×1440 → 3456×1944; neural enhancement then HDR | Passed |
| 1440p, smaller window, HDR off | VSR still skipped based on original dimensions; neural processing and clean end of file | Passed |

The final 720p run verified the DLSS Super Resolution feature-ready dimensions, successful neural feature 18 evaluation, post-DLSS HDR, and clean end of file. VSR extension requests succeeded; these runs did not independently verify the NVIDIA VSR indicator or quantify artifact removal.

Launcher regression checks passed: 100% full quality remains the default, saved work-size choices survive, toggling VSR does not change work size, and HDR-only/manual modes keep their prior routing. Source-mode selection now means VSR-first rather than VSR-after-DLSS. Frame generation and export paths are unchanged.

The 1080p cap is an aspect-preserving 1920×1080 bounding box, also capped by output size. Sources exceeding either original dimension skip VSR. Smaller windows may downscale before processing. Unsupported DLSS scale ratios retain the feeder's logged spatial fallback; the tested fullscreen ratios used actual DLSS Super Resolution. Performance and visual quality vary; these are routing and execution checks, not quality benchmarks.
