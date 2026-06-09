# Build a portable, self-contained FrameLCDoctor.exe (no .NET install needed) with its
# proxies/, profiles/ and the native helper exes (upscaler, nvdrs, amddrs) alongside,
# ready to run or share.
#   ./tools/publish.ps1 [-Out <folder>]
param([string]$Out = "$env:USERPROFILE\FrameLCDoctor-app")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$csproj = "$root\companion\src\FrameLCDoctor.App\FrameLCDoctor.App.csproj"

# make sure the prebuilt proxies exist (the launcher ships them)
if (-not (Test-Path "$root\companion\src\FrameLCDoctor.App\proxies\d3d11.dll")) {
    & "$root\tools\make_proxies.ps1"
}

dotnet publish $csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $Out

# content files aren't reliably emitted next to a single-file publish; copy them ourselves.
$app = "$root\companion\src\FrameLCDoctor.App"
New-Item -ItemType Directory -Force "$Out\proxies" | Out-Null
Copy-Item "$app\proxies\*.dll" "$Out\proxies\" -Force
New-Item -ItemType Directory -Force "$Out\profiles" | Out-Null
Copy-Item "$root\profiles\*.toml" "$Out\profiles\" -Force

# native helper exes (built via CMake). amddrs only exists on a machine with the ADLX SDK.
foreach ($h in "upscaler", "nvdrs", "amddrs") {
    if (Test-Path "$app\$h\*.exe") {
        New-Item -ItemType Directory -Force "$Out\$h" | Out-Null
        Copy-Item "$app\$h\*.exe" "$Out\$h\" -Force
    } else {
        Write-Host "warn: no exe in $app\$h (build it with CMake to ship the $h tool)"
    }
}

Remove-Item "$Out\FrameLCDoctor.pdb" -ErrorAction SilentlyContinue

Write-Host "Portable app -> $Out\FrameLCDoctor.exe"
