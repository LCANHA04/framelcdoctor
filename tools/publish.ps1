# Build a portable, self-contained FrameLCDoctor.exe (no .NET install needed) with its
# proxies/ and profiles/ alongside, ready to run or share.
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

# proxies aren't bundled into the single file; copy them next to the exe
New-Item -ItemType Directory -Force "$Out\proxies" | Out-Null
Copy-Item "$root\companion\src\FrameLCDoctor.App\proxies\*.dll" "$Out\proxies\" -Force
Remove-Item "$Out\FrameLCDoctor.pdb" -ErrorAction SilentlyContinue

Write-Host "Portable app -> $Out\FrameLCDoctor.exe"
