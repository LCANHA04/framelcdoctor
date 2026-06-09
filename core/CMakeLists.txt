# Core injected DLL: injection + present hook (D3D11 + OpenGL) + profiler + limiter + IPC
# + ImGui overlay. The CMake build is the INJECTABLE core (FLCD_HAS_GL): it also inline-hooks
# gdi32!SwapBuffers via MinHook for OpenGL games. The beside-the-exe D3D proxies are built
# separately by tools/make_proxies.ps1 and omit the OpenGL/MinHook bits.

set(IMGUI ${CMAKE_SOURCE_DIR}/third_party/imgui)
set(MINHOOK ${CMAKE_SOURCE_DIR}/third_party/minhook)

add_library(flcd_core SHARED
    inject/dllmain.cpp
    hooks/present_d3d11.cpp
    hooks/present_opengl.cpp
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
    ${MINHOOK}/src/buffer.c
    ${MINHOOK}/src/hook.c
    ${MINHOOK}/src/trampoline.c
    ${MINHOOK}/src/hde/hde32.c
    ${MINHOOK}/src/hde/hde64.c
)

target_include_directories(flcd_core PRIVATE include ${IMGUI} ${MINHOOK}/include)
target_compile_definitions(flcd_core PRIVATE UNICODE _UNICODE NOMINMAX FLCD_HAS_GL)
target_link_libraries(flcd_core PRIVATE winmm d3dcompiler dwmapi)  # imgui dx11/win32 backends

set_target_properties(flcd_core PROPERTIES OUTPUT_NAME "flcd_core")
