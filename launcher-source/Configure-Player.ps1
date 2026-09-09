# Run after installing the third-party runtime files described in BUILDING.md.
$ErrorActionPreference='Stop'
$app=Split-Path $PSScriptRoot -Parent
$required=@('mpv.exe','dxgi.dll','renodx-dlss5.addon64','nvngx_dlss.dll','nvngx_dlssnr.dll','tools\ffmpeg.exe','tools\ffprobe.exe','tools\yt-dlp.exe','tools\deno.exe','tools\streamlink\bin\streamlink.exe','tools\rife\rife-ncnn-vulkan.exe')
foreach($name in $required){if(!(Test-Path (Join-Path $app $name))){throw "Missing runtime: $name (see BUILDING.md)"}}
New-Item -ItemType Directory -Force (Join-Path $app 'portable_config\scripts'),(Join-Path $app 'tools\plain-player\portable_config'),(Join-Path $app 'tools\neural-export') | Out-Null
foreach($name in @('ReShade.ini','ReShadePreset.ini','dlss5-feed.cfg')){if(!(Test-Path (Join-Path $app $name))){Copy-Item (Join-Path $app "config\$name") $app}}
if(!(Test-Path (Join-Path $app 'portable_config\mpv.conf'))){Copy-Item (Join-Path $app 'config\mpv.conf') (Join-Path $app 'portable_config')}
Copy-Item (Join-Path $app 'config\motion-toggle.lua') (Join-Path $app 'portable_config\scripts') -Force
Copy-Item (Join-Path $app 'mpv.exe') (Join-Path $app 'tools\plain-player') -Force
Set-Content (Join-Path $app 'tools\plain-player\portable_config\mpv.conf') "vo=gpu`ngpu-api=d3d11`nhwdec=auto-safe"
foreach($name in @('dxgi.dll','renodx-dlss5.addon64','nvngx_dlss.dll','nvngx_dlssnr.dll')){Copy-Item (Join-Path $app $name) (Join-Path $app 'tools\neural-export') -Force}
foreach($name in @('ffmpeg.exe','ffprobe.exe')){Copy-Item (Join-Path $app "tools\$name") (Join-Path $app 'tools\neural-export') -Force}
& (Join-Path $PSScriptRoot 'build.ps1')
Write-Host 'Player configured. Native builds and shader installation must also be completed.'
