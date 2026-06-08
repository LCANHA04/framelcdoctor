# Build the prebuilt proxy DLLs the launcher deploys (d3d11.dll, dxgi.dll).
# Each is flcd_core compiled with forwarders to <name>_orig.dll. Output goes to the
# companion's proxies/ folder (copied to the app output at build time).
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$vs = "C:\Program Files\Microsoft Visual Studio\2022\Community"
$dumpbin = (Get-ChildItem "$vs\VC\Tools\MSVC" -Recurse -Filter dumpbin.exe | Where-Object FullName -match 'Hostx64\\x64' | Select-Object -First 1).FullName
$vcvars = "$vs\VC\Auxiliary\Build\vcvars64.bat"
$prox = "$root\companion\src\FrameLCDoctor.App\proxies"
New-Item -ItemType Directory -Force $prox, "$root\build-pkg" | Out-Null

$im = "third_party\imgui"
$src = "core\inject\dllmain.cpp core\hooks\present_d3d11.cpp core\profiler\frame_profiler.cpp core\remedies\frame_limiter.cpp core\remedies\control.cpp core\ipc\pipe_server.cpp core\classify\headroom.cpp core\overlay\overlay_d3d11.cpp $im\imgui.cpp $im\imgui_draw.cpp $im\imgui_tables.cpp $im\imgui_widgets.cpp $im\backends\imgui_impl_dx11.cpp $im\backends\imgui_impl_win32.cpp"
foreach ($name in @("d3d11", "dxgi")) {
    python "$root\tools\gen_forwarders.py" "C:\Windows\System32\$name.dll" "${name}_orig" --dumpbin $dumpbin |
        Out-File -Encoding ASCII "$root\core\inject\forwarders.generated.h"
    $out = "$prox\$name.dll"
    $cmd = "call `"$vcvars`" >nul && cd /d `"$root`" && cl /nologo /O2 /MT /LD /EHsc /std:c++17 /DUNICODE /D_UNICODE /DNOMINMAX /Icore\include /I$im $src /Fobuild-pkg\ /Fe:`"$out`" /link winmm.lib d3dcompiler.lib dwmapi.lib"
    cmd /c $cmd | Out-Null
    foreach ($ext in 'lib','exp') { [System.IO.File]::Delete("$prox\$name.$ext") }
    Write-Host "built $out"
}
[System.IO.File]::Delete("$root\core\inject\forwarders.generated.h")
Write-Host "done."
