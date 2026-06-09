# flcd_upscaler.exe - external window upscaler (C++/WinRT Graphics.Capture + D3D11).
add_executable(flcd_upscaler WIN32 upscaler_main.cpp)

set_property(TARGET flcd_upscaler PROPERTY CXX_STANDARD 17)
target_compile_definitions(flcd_upscaler PRIVATE UNICODE _UNICODE NOMINMAX)
target_compile_options(flcd_upscaler PRIVATE /EHsc)   # C++/WinRT uses exceptions

# cppwinrt + Graphics.Capture interop headers ship with the Windows SDK; the libs:
target_link_libraries(flcd_upscaler PRIVATE d3d11 dxgi d3dcompiler windowsapp dwmapi)
