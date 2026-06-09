# flcd_nvdrs.exe - NVIDIA driver-profile optimizer (talks to nvapi64.dll at runtime).
add_executable(flcd_nvdrs nvdrs_main.cpp)
set_property(TARGET flcd_nvdrs PROPERTY CXX_STANDARD 17)
target_compile_definitions(flcd_nvdrs PRIVATE UNICODE _UNICODE NOMINMAX)
# no link libs: nvapi64.dll is loaded dynamically (ships with the NVIDIA driver).
