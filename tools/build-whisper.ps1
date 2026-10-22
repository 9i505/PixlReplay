<#
  Builds the auto captions runtime (whisper.cpp 1.9.5 with the Vulkan backend) into third_party\whisper\bin, where the
  release script zips it as the captions engine people download on first use. Needs: git, the Vulkan SDK (VULKAN_SDK set by its installer) and the
  Visual Studio 2022 Build Tools (C++ and CMake). Only the five DLLs PixlReplay loads are kept.
  If whisper.cpp is ever updated, measure WhisperNative's struct sizes and offsets again (see that file).
#>
param(
    [string]$Work = (Join-Path $env:TEMP 'pixlreplay-whisper-build'),
    [string]$Tag = 'v1.9.5'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$dest = Join-Path $repo 'third_party\whisper\bin'
if (-not $env:VULKAN_SDK) { throw 'The Vulkan SDK is not installed (VULKAN_SDK is not set).' }
$cmake = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
if (-not (Test-Path $cmake)) { $cmake = 'cmake' }

$src = Join-Path $Work 'src'
$build = Join-Path $Work 'build'
if (-not (Test-Path $src)) {
    git clone --depth 1 --branch $Tag https://github.com/ggml-org/whisper.cpp $src
    if ($LASTEXITCODE -ne 0) { throw 'git clone failed' }
}
# Source paths in the DLLs (assert messages) lose this machine's folders: they start at the source tree.
$env:CFLAGS = "/d1trimfile:$src\"
$env:CXXFLAGS = "/d1trimfile:$src\"
& $cmake -S $src -B $build -G 'Visual Studio 17 2022' -A x64 -DGGML_VULKAN=ON -DBUILD_SHARED_LIBS=ON -DGGML_NATIVE=OFF `
    -DWHISPER_BUILD_TESTS=OFF -DWHISPER_BUILD_EXAMPLES=OFF
if ($LASTEXITCODE -ne 0) { throw 'cmake configure failed' }
& $cmake --build $build --config Release -j
if ($LASTEXITCODE -ne 0) { throw 'cmake build failed' }

New-Item -ItemType Directory -Force $dest | Out-Null
foreach ($dll in 'whisper.dll', 'ggml.dll', 'ggml-base.dll', 'ggml-cpu.dll', 'ggml-vulkan.dll') {
    Copy-Item (Join-Path $build "bin\Release\$dll") $dest -Force
}
Copy-Item (Join-Path $src 'LICENSE') (Join-Path $repo 'licenses\whisper.cpp-LICENSE.txt') -Force
$mb = [math]::Round(((Get-ChildItem $dest -File | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Output "Built whisper.cpp $Tag into $dest ($mb MB)"
exit 0
