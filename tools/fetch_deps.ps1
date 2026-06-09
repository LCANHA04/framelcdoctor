# Fetch native dependencies needed to build the core: ImGui (overlay) + MinHook (inline hooks
# for the OpenGL/gdi32 present hook used by the injectable core).
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

$im = Join-Path $root "third_party\imgui"
if (Test-Path "$im\imgui.h") { Write-Host "imgui already present" }
else { git clone --depth 1 https://github.com/ocornut/imgui $im; Write-Host "imgui fetched." }

$mh = Join-Path $root "third_party\minhook"
if (Test-Path "$mh\include\MinHook.h") { Write-Host "minhook already present" }
else { git clone --depth 1 https://github.com/TsudaKageyu/minhook $mh; Write-Host "minhook fetched." }
