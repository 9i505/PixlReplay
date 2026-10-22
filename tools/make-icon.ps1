<#
  Writes src\PixlReplay\app.ico (the exe, Explorer and Start menu icon) from the drawing the app itself uses for its
  window (src\PixlReplay\App\AppIconArt.cs): the warm plate, and the ring and dot in the theme's rose.
  Build the solution first, then build again so the exe takes the new icon.
#>
param([string]$Out = (Join-Path $PSScriptRoot '..\src\PixlReplay\app.ico'))
$exe = Join-Path $PSScriptRoot '..\src\PixlReplay\bin\x64\Debug\net9.0-windows\win-x64\PixlReplay.exe'
if (-not (Test-Path $exe)) { throw "Build PixlReplay.sln first: $exe not found" }
& $exe --write-icon ([System.IO.Path]::GetFullPath($Out)) | Out-String | Write-Output
