# Fetch native dependencies (ImGui) needed to build the core overlay.
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$im = Join-Path $root "third_party\imgui"
if (Test-Path "$im\imgui.h") { Write-Host "imgui already present"; return }
git clone --depth 1 https://github.com/ocornut/imgui $im
Write-Host "imgui fetched."
