# VSR color and image checks

On RTX 5070 Ti / driver 616.64, the VSR RGB10-output path reduced a full-white RGB10 test patch to approximately (534,535,535) out of 1023 and altered red. The same conversion without VSR retained white at (1023,1023,1022). Using SDR RGBA8 output for VSR returned expected black, middle gray, white and red patches within five 8-bit levels. Pre-DLSS VSR now uses this SDR intermediate; optional HDR remains a separate final stage. VSR-only playback also selects an SDR RGBA8 swapchain.

- ColorConversionTests passed for RGB10 input → VSR → RGBA8 SDR output.
- VSR-first live playback passed with RGBA8 VSR output and verified neural rendering.
- Local and HTTP image tests each produced one verified neural frame and a PNG, preserving 321×241 dimensions and alpha. Existing-output protection passed.
- Direct URL testing used a temporary localhost HTTP image server; it was stopped afterward.

Images use the existing offline renderer with single-frame warm-up. PNG alpha is restored after neural rendering. Enlargements use Lanczos after enhancement. The temporary neural video intermediate is encoded, so output is not a lossless round-trip. These tests do not establish image quality for every source, profile, or driver.
