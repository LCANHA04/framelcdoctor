# flcd_inject.exe - remote-thread injector for the core (OpenGL / no-file-drop targets).
add_executable(flcd_inject inject_main.cpp)
set_property(TARGET flcd_inject PROPERTY CXX_STANDARD 17)
target_compile_definitions(flcd_inject PRIVATE UNICODE _UNICODE NOMINMAX)
