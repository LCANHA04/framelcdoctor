cmake_minimum_required(VERSION 3.21)
project(FrameLCDoctor LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

if(NOT WIN32)
  message(FATAL_ERROR "FrameLCDoctor core targets Windows (x64) only.")
endif()

# Core: the injected DLL. Companion (C#/.NET) builds separately via dotnet.
add_subdirectory(core)

# Upscaler: standalone external window upscaler exe.
add_subdirectory(upscaler)

# Driver optimizer: standalone NVIDIA driver-profile tool (nvapi).
add_subdirectory(nvdrs)
