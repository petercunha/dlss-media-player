param([string]$DependencyDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) '.build'))
$ErrorActionPreference='Stop'
$app=Split-Path $PSScriptRoot -Parent
New-Item -ItemType Directory -Force $DependencyDirectory | Out-Null
$DependencyDirectory=(Resolve-Path $DependencyDirectory).Path
function Git-Checked([string[]]$Arguments) { & git @Arguments; if($LASTEXITCODE -ne 0){throw "Git failed: $($Arguments[0])"} }
function Checkout-Dependency($url,$revision,$path) {
 if(!(Test-Path (Join-Path $path '.git'))){Git-Checked -Arguments @('clone','--no-checkout',$url,$path);Git-Checked -Arguments @('-C',$path,'checkout','--detach',$revision)}
 $head=& git -C $path rev-parse HEAD
 if($head -ne $revision){throw "Unexpected revision at $path. Use a fresh dependency directory."}
}
$feeder=Join-Path $DependencyDirectory 'feeder'
$sdk=Join-Path $DependencyDirectory 'DLSS'
$vulkan=Join-Path $DependencyDirectory 'Vulkan-Headers'
$native=Join-Path $DependencyDirectory 'native-player'
Checkout-Dependency 'https://github.com/jlrouzies-fr/DLSS5-Feeder.git' 'cc68576c569aa35deb5e01d9a6fad5d30ac64dbc' $feeder
Checkout-Dependency 'https://github.com/NVIDIA/DLSS.git' 'a291cc7d2cc642a51566f3dfd5376f635cd1b284' $sdk
Checkout-Dependency 'https://github.com/KhronosGroup/Vulkan-Headers.git' 'ee2ec5fd83dafce291024683b50dc89219333076' $vulkan
Checkout-Dependency 'https://github.com/2600th/dlss5-video-player.git' '335ddc4523e3614e6dfc507c7d271b8fe0c71ebb' $native
Copy-Item (Join-Path $PSScriptRoot 'live-rtx-feeder\dlss5-feed.cpp') (Join-Path $feeder 'src') -Force
Copy-Item (Join-Path $PSScriptRoot 'live-rtx-feeder\media_source.h') (Join-Path $feeder 'src') -Force
Copy-Item (Join-Path $PSScriptRoot 'live-rtx-feeder\media_rtx.h') (Join-Path $feeder 'src') -Force
Copy-Item (Join-Path $sdk 'include\*') (Join-Path $feeder 'external\ngx') -Recurse -Force
New-Item -ItemType Directory -Force (Join-Path $feeder 'external\ngx\libs') | Out-Null
Copy-Item (Join-Path $sdk 'lib\Windows_x86_64\x64\nvsdk_ngx_d.lib') (Join-Path $feeder 'external\ngx\libs') -Force
Copy-Item (Join-Path $vulkan 'include\*') (Join-Path $feeder 'external\vulkan') -Recurse -Force
$started=Get-Date
& cmd /c (Join-Path $feeder 'build.bat')
$addon=Join-Path $feeder 'build\dlss5-feed.addon64'
if(!(Test-Path $addon) -or (Get-Item $addon).LastWriteTime -lt $started){throw 'Feeder build failed'}

Copy-Item (Join-Path $PSScriptRoot 'native-export\src\*') (Join-Path $native 'src') -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot 'native-export\CMakeLists.txt') $native -Force
$nativeSdk=Join-Path $native 'external\DLSS'
if(!(Test-Path $nativeSdk)){New-Item -ItemType Junction -Path $nativeSdk -Target $sdk | Out-Null}
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$cmake=Join-Path $vs 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
if(!(Test-Path $cmake)){throw 'Install Visual Studio C++ Build Tools with CMake support'}
& $cmake -S $native -B (Join-Path $native 'build') -G 'Visual Studio 17 2022' -A x64 -DBUILD_TESTING=OFF
if($LASTEXITCODE -ne 0){throw 'Native CMake configuration failed'}
& $cmake --build (Join-Path $native 'build') --config Release --target NeuralExport --parallel 4
if($LASTEXITCODE -ne 0){throw 'Native exporter build failed'}
Copy-Item $addon (Join-Path $app 'dlss5-feed.addon64') -Force
New-Item -ItemType Directory -Force (Join-Path $app 'tools\neural-export') | Out-Null
Copy-Item (Join-Path $native 'build\Release\NeuralExport.exe') (Join-Path $app 'tools\neural-export') -Force
Write-Host 'Built the live RTX feeder and offline neural exporter.'
