#Requires -Version 7
<#
  Builds a PixlReplay release into releases\ (git-ignored, and the local update folder for testing):
  - PixlReplay-Setup.exe, the install wizard people download (Inno Setup 6, installer\PixlReplay.iss), which carries
    PixlReplay-win-Setup.exe, Velopack's one-click installer, and runs it into the folder chosen in the wizard;
  - the update files (full and delta packages, releases.win.json); the Visual C++ runtime is installed by the
    Setup only when the PC lacks it;
  - PixlReplay-<version>-source.zip, the release's complete source (the GPL), from the commit it is built from;
  - captions-runtime\pixlreplay-captions-runtime-1.9.5.zip, auto captions' engine, made once from third_party\whisper\bin.
  Nothing is uploaded: publishing is a separate step, run only when the owner says so.
#>
param(
    [string]$PackId = 'PixlReplay',
    [string]$Version,
    [string]$Out = 'releases',
    [string]$ExpectedObs = '32.2.2',
    # Without OBS installed: take libobs from an installed Pixl Replay's ...\current folder (see stage-runtime.ps1).
    [string]$ObsFromInstall
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
# Auto captions' engine as CaptionRuntime pins it (AssetName, Files); this order makes the same zip again.
$captionsZip = 'pixlreplay-captions-runtime-1.9.5.zip'
$whisperDlls = 'whisper.dll', 'ggml.dll', 'ggml-base.dll', 'ggml-cpu.dll', 'ggml-vulkan.dll'
# The source zip is made from the last commit: refuse a build that differs from it.
if (git -C $repo status --porcelain --untracked-files=no) { throw 'Commit your changes first: the source zip must match the build' }
[xml]$proj = Get-Content (Join-Path $repo 'src\PixlReplay\PixlReplay.csproj')
if (-not $Version) { $Version = $proj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1 }
$notes = Join-Path $repo "docs\release-notes\$Version.md"
if (-not (Test-Path $notes)) { throw "Write the release notes first: docs\release-notes\$Version.md" }
$out = if ([IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path $repo $Out }
$vpk = (Get-Command vpk -ErrorAction SilentlyContinue)?.Source ?? (Join-Path $env:USERPROFILE '.dotnet\tools\vpk.exe')
if (-not (Test-Path $vpk)) { throw 'vpk not found: dotnet tool install -g vpk --version 1.2.161' }

# The program, in one folder: libobs from the installed OBS (read only), then PixlReplay with .NET inside.
$stage = Join-Path $repo "dist\stage-$PackId-$Version"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
if ($ObsFromInstall) { & (Join-Path $PSScriptRoot 'stage-runtime.ps1') -Dest $stage -FromInstall $ObsFromInstall | Write-Output }
else { & (Join-Path $PSScriptRoot 'stage-runtime.ps1') -Dest $stage | Write-Output }
$obsVersion = (Get-Content (Join-Path $stage 'libobs-version.txt')).Trim()
if ($obsVersion -ne $ExpectedObs) { throw "Installed OBS is $obsVersion, expected $ExpectedObs (pass -ExpectedObs after re-testing)" }
dotnet publish (Join-Path $repo 'src\PixlReplay') -c Release -o $stage --self-contained true "-p:Version=$Version" --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
# Debug symbols and import libraries are only for developers.
Get-ChildItem $stage -File | Where-Object { $_.Extension -in '.pdb', '.lib', '.xml' } | Remove-Item
foreach ($f in 'LICENSE', 'THIRD-PARTY-NOTICES.md') { Copy-Item (Join-Path $repo $f) $stage }
Copy-Item (Join-Path $repo 'licenses') $stage -Recurse

# The installer and the update files.
New-Item -ItemType Directory -Force $out | Out-Null
& $vpk pack --packId $PackId --packVersion $Version --packDir $stage --mainExe PixlReplay.exe --packTitle $PackId --packAuthors PixlReplay `
    --icon (Join-Path $repo 'src\PixlReplay\app.ico') --shortcuts StartMenuRoot --framework vcredist145-x64 --releaseNotes $notes --outputDir $out
if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed' }
# The release page carries the Setup only: no portable zip.
Get-ChildItem $out -Filter "$PackId-win-Portable.zip" | Remove-Item

# The install wizard around Velopack's Setup: what the release page offers.
$iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe" |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found: winget install JRSoftware.InnoSetup --scope user' }
& $iscc /Qp "/DAppVersion=$Version" "/DPackId=$PackId" "/DOutDir=$out" (Join-Path $repo 'installer\PixlReplay.iss')
if ($LASTEXITCODE -ne 0) { throw 'the install wizard did not build' }

if ($PackId -eq 'PixlReplay') {
    # The GPL: this release's complete source, from the commit it was built from.
    $source = Join-Path $out "PixlReplay-$Version-source.zip"
    if (Test-Path $source) { Remove-Item $source }
    git -C $repo archive --format=zip --prefix="PixlReplay-$Version-source/" -o $source HEAD
    if ($LASTEXITCODE -ne 0) { throw 'git archive failed' }
}

# Auto captions' engine, downloaded on first use (made once: its size and fingerprint are pinned in CaptionRuntime).
$captions = Join-Path $out "captions-runtime\$captionsZip"
if (-not (Test-Path $captions)) {
    New-Item -ItemType Directory -Force (Split-Path $captions) | Out-Null
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::Open($captions, 'Create')
    foreach ($dll in $whisperDlls) {
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $repo "third_party\whisper\bin\$dll"), $dll, 'SmallestSize')
    }
    $zip.Dispose()
    Write-Warning "Made ${captions}: pin its size and SHA-256 in CaptionRuntime if it changed."
}

Get-ChildItem $out -File | Where-Object { $_.Name -like "$PackId-*" -or $_.Name -like 'releases.*' -or $_.Name -like 'assets.*' } |
    ForEach-Object { '{0,-48} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) }
