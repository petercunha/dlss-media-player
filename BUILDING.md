# Building and provisioning

Requires Windows x64, Git, Visual Studio 2022 C++ Build Tools with Windows SDK and CMake, and .NET Framework 4.x. No .NET SDK is needed for the launcher.

```powershell
powershell -ExecutionPolicy Bypass -File .\launcher-source\build.ps1
powershell -ExecutionPolicy Bypass -File .\launcher-source\Build-Native.ps1
```

The native script downloads pinned upstream source into ignored `.build/`, overlays the checked-in modifications, and builds the live feeder and offline exporter. Close MPV before replacing a loaded add-on. NVIDIA's SDK download is sizeable and subject to its license. Build dependencies contain a directory junction for the SDK; check targets before recursively cleaning them.

Pinned source revisions:

- Feeder: `3f624855276c4bde55145c712782477639b30e85` (stable v0.15.1, with our HDR and prepared-video integration)
- NVIDIA/DLSS SDK: `a291cc7d2cc642a51566f3dfd5376f635cd1b284`
- Vulkan-Headers: `ee2ec5fd83dafce291024683b50dc89219333076`
- Native renderer: `335ddc4523e3614e6dfc507c7d271b8fe0c71ebb`

## Runtime dependencies

Install these separately; their binaries and models are intentionally excluded from Git:

| Destination | Upstream |
|---|---|
| `mpv.exe` | [MPV Windows build](https://github.com/zhongfly/mpv-winbuild/releases), D3D11-enabled |
| `dxgi.dll` | [ReShade](https://reshade.me/) x64 with add-on support; tested 6.8 |
| `renodx-dlss5.addon64`, `nvngx_dlss.dll`, `nvngx_dlssnr.dll` | Compatible set described by [DLSS5oneclick](https://github.com/faisalkindi/DLSS5oneclick); tested RenoDX v4.70, DLSS 310.9.1 and NR 310.8.SF-v2 |
| `reshade-shaders/Shaders/DLSS5_Feed.fx` | Pinned [DLSS5-Feeder](https://github.com/jlrouzies-fr/DLSS5-Feeder) shader |
| `reshade-shaders/Shaders/`, `Textures/` | [LumeniteFX](https://github.com/umar-afzaal/LumeniteFX), including shader includes, textures and ReShade shader headers |
| `tools/ffmpeg.exe`, `tools/ffprobe.exe` | [FFmpeg Windows build](https://www.gyan.dev/ffmpeg/builds/), with CUDA/NVENC |
| `tools/yt-dlp.exe` | [yt-dlp](https://github.com/yt-dlp/yt-dlp/releases) |
| `tools/deno.exe` | [Deno](https://github.com/denoland/deno/releases) |
| `tools/streamlink/` | Complete [Streamlink portable distribution](https://github.com/streamlink/windows-builds/releases), including `bin/streamlink.exe`, pkgs and Python; tested 8.4.0 |
| `tools/rife/` | Complete [rife-ncnn-vulkan Windows 20221029 release](https://github.com/nihui/rife-ncnn-vulkan/releases/tag/20221029), including rife-v4.6 and runtime DLL |

After provisioning:

```powershell
powershell -ExecutionPolicy Bypass -File .\launcher-source\Configure-Player.ps1
```

This stages defaults only when absent, configures the plain prepared-video player and exporter helpers, and compiles the launcher. It does not download runtime models or shaders. Native builds and shader installation must also be completed. Existing ReShade settings are preserved. Enable NVIDIA video enhancements in NVIDIA app, and Windows HDR for HDR playback. The configuration template selects an NVIDIA adapter generically.

RIFE explicitly selects Vulkan GPU 0, verified as the RTX 5070 Ti on the original machine. Adjust Processing.cs if your Vulkan device order differs.

## Integration tests

Compile a test from `tests/` with all six top-level launcher `.cs` files (including `Images.cs` and `LiveVideo.cs`), `/target:exe /main:<test class>`, and the same assembly references as build.ps1.

- ExportRegressionTests: player root, synthetic source path, new destination, and `off`, `rife`, or `cancel`.
- LiveRtxTests: player root, local SDR path or live HTTPS URL, and output target index (4 = fullscreen). Its HDR assertions require Windows HDR enabled.
- SourceModeTests: player root. Checks defaults and migration to the DLSS/Off/VSR selector.
- The launcher also supports `--self-test`.

Generate test media outside Git. Raw logs are ignored because they may contain source URLs and local paths. Sanitized summaries are in verification/.

ImageTests exercises local files/direct URLs, PNG dimensions, alpha, and overwrite protection. Compile like the other launcher tests and pass player root, synthetic PNG source (or URL), and a new PNG destination. ColorConversionTests.cpp is a historical VSR diagnostic and is not part of the current HDR/buffering test suite.
