<#
  Copies the parts of libobs that PixlReplay needs from an installed OBS Studio into PixlReplay's program folder (one
  folder: DLLs and helpers beside PixlReplay.exe, obs-plugins\64bit, data). OBS itself is only read, never modified.
#>
param(
    [string]$Dest = 'D:\PixlReplay',
    [string]$Obs = 'C:\Program Files\obs-studio'
)

$ErrorActionPreference = 'Stop'

$plugins = 'win-capture', 'win-wasapi', 'obs-nvenc', 'obs-ffmpeg', 'obs-qsv11', 'obs-x264'
# Not loaded by anything PixlReplay ships: the OBS window (Qt), scripting, OpenGL, and WebRTC (datachannel, obs-webrtc's only).
$skipDlls = '^(Qt6.*|obs-frontend-api|obs-scripting|lua51|libobs-opengl|python.*|datachannel)\.dll$'
$helperExes = 'obs-ffmpeg-mux.exe', 'obs-nvenc-test.exe', 'obs-amf-test.exe', 'obs-qsv-test.exe'

if (-not (Test-Path "$Obs\bin\64bit\obs.dll")) { throw "OBS runtime not found at $Obs" }

$pluginBin = Join-Path $Dest 'obs-plugins\64bit'
$dataRoot = Join-Path $Dest 'data'
New-Item -ItemType Directory -Force $Dest, $pluginBin, "$dataRoot\obs-plugins" | Out-Null

Get-ChildItem "$Obs\bin\64bit" -Filter *.dll |
    Where-Object { $_.Name -notmatch $skipDlls } |
    Copy-Item -Destination $Dest -Force
foreach ($exe in $helperExes) {
    if (Test-Path "$Obs\bin\64bit\$exe") { Copy-Item "$Obs\bin\64bit\$exe" $Dest -Force }
}

function Copy-Tree($from, $to) {
    robocopy $from $to /E /XF *.pdb @args /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE): $from" }
}

Copy-Tree "$Obs\data\libobs" "$dataRoot\libobs"

foreach ($p in $plugins) {
    Copy-Item "$Obs\obs-plugins\64bit\$p.dll" $pluginBin -Force
    $data = "$Obs\data\obs-plugins\$p"
    if (Test-Path $data) {
        # libobs starts in en-US (ObsHost): of a plugin's translations only its en-US.ini is ever read.
        Copy-Tree $data "$dataRoot\obs-plugins\$p" /XD "$data\locale"
        if (Test-Path "$data\locale\en-US.ini") {
            New-Item -ItemType Directory -Force "$dataRoot\obs-plugins\$p\locale" | Out-Null
            Copy-Item "$data\locale\en-US.ini" "$dataRoot\obs-plugins\$p\locale" -Force
        }
    }
}

$version = (Get-Item "$Obs\bin\64bit\obs.dll").VersionInfo.ProductVersion
Set-Content -Path (Join-Path $Dest 'libobs-version.txt') -Value $version -Encoding ascii
$mb = [math]::Round(((Get-ChildItem $Dest -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Output "Staged libobs $version into $Dest ($mb MB)"
exit 0
