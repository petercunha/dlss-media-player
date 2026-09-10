# Buffered motion stability — September 10, 2026

The user reported persistent warping on moving content, despite verified frame order. Normal live playback uses Lumenite/ReShade motion estimation and validation; buffered playback used a different compact CPU flow grid expanded for the native neural renderer. Its temporal output was not visually equivalent.

A diagnostic point-sampled guide plus full-resolution current/previous source validation did not improve the measured trails enough. That experimental shader/root-signature change was reverted and is not in this build.

The shipped correction uses independent-frame neural evaluation in buffered mode: reset the guide history and NGX temporal history each frame, with synthetic jitter disabled and flat depth. The same GPU device, feature and model stay allocated. It does not bypass DLSS, recreate the model per frame, change normal live playback, or disable MPV display smoothing/HDR. Prepare/export retains its previous behavior. The tradeoff is losing neural temporal accumulation; fine-detail flicker remains possible, and this is not an exact port of the live renderer.

## Evidence

The 300-frame 640×360/30 fps synthetic fixture has a moving red circle, a stationary grid and binary frame markers that alternate between bright/dark. Previous temporal rendering produced dark marker cores as bright as about 170/255 even though the source dark value is approximately 15, plus up to 81 red trail pixels outside the moving object's padded bounds.

The final built native renderer was tested through the production buffered pipeline across two chunks:

- All 300 decoded frame IDs remain exactly in order; no drops/duplicates.
- Timestamp steps remain 0.033333–0.033334 seconds across the boundary.
- Maximum dark marker core brightness: 16.24/255.
- Red trail pixels outside the object's three-pixel allowance: zero.
- Native feature-18 output receipts verify all 300 frames.
- MPV display smoothing active; finite EOF completed.
- NeuralPrerenderTests passed.

Run tests/check-buffered-motion.py with the player root and joined.ts from BufferedOrderTests, alongside the existing check-frame-order.py. This is a motion-artifact regression on a controlled fixture, not proof that every real-world video is artifact-free. Test videos and raw logs are not committed.
