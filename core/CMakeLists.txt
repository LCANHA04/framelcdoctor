# Core injected DLL (P0): injection bootstrap + present hook + profiler + limiter + IPC.

add_library(flcd_core SHARED
    inject/dllmain.cpp
    hooks/present_d3d11.cpp
    profiler/frame_profiler.cpp
    remedies/frame_limiter.cpp
    ipc/pipe_server.cpp
    # classify/classifier.cpp     # P1
    # remedies/dxvk_advisor.cpp   # P3
    # overlay/imgui_overlay.cpp   # P1
)

target_include_directories(flcd_core PRIVATE include)
target_compile_definitions(flcd_core PRIVATE UNICODE _UNICODE NOMINMAX)
target_link_libraries(flcd_core PRIVATE winmm)   # timeBeginPeriod

# When packaged for a specific game the output is renamed to the proxied DLL
# (d3d11.dll / dxgi.dll / winmm.dll) and shipped with a generated forwarders header
# (tools/gen_forwarders.py). The framework itself is game-agnostic.
set_target_properties(flcd_core PROPERTIES OUTPUT_NAME "flcd_core")

# vcpkg deps (uncomment as modules are wired in):
# find_package(minhook CONFIG REQUIRED)
# find_package(imgui CONFIG REQUIRED)
# find_package(tomlplusplus CONFIG REQUIRED)
# target_link_libraries(flcd_core PRIVATE minhook::minhook imgui::imgui tomlplusplus::tomlplusplus)
