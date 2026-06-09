# flcd_amddrs.exe - AMD driver optimizer (ADLX). Built ONLY when the ADLX SDK is present,
# so the repo keeps building for everyone without it. Drop the ADLX SDK (the folder that
# contains "SDK/") into third_party/adlx, or pass -DADLX_SDK_DIR=<path>.
set(ADLX_SDK_DIR "${CMAKE_SOURCE_DIR}/third_party/adlx" CACHE PATH "ADLX SDK root (the dir containing SDK/)")

if(EXISTS "${ADLX_SDK_DIR}/SDK/Include/I3DSettings.h")
    add_executable(flcd_amddrs
        amddrs_main.c
        "${ADLX_SDK_DIR}/SDK/ADLXHelper/Windows/C/ADLXHelper.c"
        "${ADLX_SDK_DIR}/SDK/platform/Windows/WinAPIs.c")
    target_include_directories(flcd_amddrs PRIVATE "${ADLX_SDK_DIR}")
    target_compile_definitions(flcd_amddrs PRIVATE UNICODE _UNICODE NOMINMAX)
    # ADLXHelper loads amdadlx64.dll at runtime - nothing to link.
    message(STATUS "ADLX SDK found - building flcd_amddrs (AMD driver optimizer).")
else()
    message(STATUS "ADLX SDK not found at ${ADLX_SDK_DIR} - skipping flcd_amddrs. "
                   "Drop the ADLX SDK there to build the AMD driver optimizer.")
endif()
