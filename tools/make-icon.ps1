<#
  Writes src\PixlReplay\app.ico (the exe, Explorer and Start menu icon) and the install wizard's two pictures
  (installer\wizard.png, wizard-small.png) from the drawing the app itself uses (src\PixlReplay\App\AppIconArt.cs):
  the Pixl P with a play mark in Pixl Replay's coral, on its graphite plate.
  Build the solution first, then build again so the exe takes the new icon.
#>
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'src\PixlReplay\bin\Release\net10.0-windows\win-x64\PixlReplay.exe'
if (-not (Test-Path $exe)) { throw "Build PixlReplay.sln (Release) first: $exe not found" }
# A throwaway data folder: the default look, never the developer's own colors.
$env:PIXLREPLAY_DATA = Join-Path ([System.IO.Path]::GetTempPath()) 'pixlreplay-make-icon'
& $exe --write-icon (Join-Path $root 'src\PixlReplay\app.ico') | Out-String | Write-Output
& $exe --write-wizard (Join-Path $root 'installer') | Out-String | Write-Output
