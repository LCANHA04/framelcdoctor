# Core injected DLL: injection + present hook + profiler + limiter + IPC + ImGui overlay.

set(IMGUI ${CMAKE_SOURCE_DIR}/third_party/imgui)

add_library(flcd_core SHARED
    inject/dllmain.cpp
    hooks/present_d3d11.cpp
    profiler/frame_profiler.cpp
    remedies/frame_limiter.cpp
    remedies/control.cpp
    ipc/pipe_server.cpp
    classify/headroom.cpp
    overlay/overlay_d3d11.cpp
    ${IMGUI}/imgui.cpp
    ${IMGUI}/imgui_draw.cpp
    ${IMGUI}/imgui_tables.cpp
    ${IMGUI}/imgui_widgets.cpp
    ${IMGUI}/backends/imgui_impl_dx11.cpp
    ${IMGUI}/backends/imgui_impl_win32.cpp
)

target_include_directories(flcd_core PRIVATE include ${IMGUI})
target_compile_definitions(flcd_core PRIVATE UNICODE _UNICODE NOMINMAX)
target_link_libraries(flcd_core PRIVATE winmm d3dcompiler dwmapi)  # imgui dx11/win32 backends

set_target_properties(flcd_core PROPERTIES OUTPUT_NAME "flcd_core")
