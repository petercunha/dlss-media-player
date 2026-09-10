$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$target = Join-Path (Split-Path $PSScriptRoot -Parent) 'DLSS-Media-Launcher.exe'
& $compiler /nologo /target:winexe /optimize+ "/out:$target" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'Launcher.cs') (Join-Path $PSScriptRoot 'Processing.cs') (Join-Path $PSScriptRoot 'LauncherForm.cs') (Join-Path $PSScriptRoot 'Streamlink.cs') (Join-Path $PSScriptRoot 'LiveVideo.cs') (Join-Path $PSScriptRoot 'Images.cs') (Join-Path $PSScriptRoot 'BufferedVideo.cs')
if ($LASTEXITCODE -ne 0) { throw 'Build failed. Close the launcher before rebuilding.' }
