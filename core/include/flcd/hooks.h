// Graphics hooks. P0: D3D11/DXGI present via a bare factory (no dummy device),
// catching the game's real swap chain and hooking Present/Present1. The hook calls
// a single per-frame orchestrator callback (limiter + profiler live there).
#pragma once

namespace flcd::hooks {

// Called once per top-level present (re-entrancy guarded so engines that issue
// nested Present1->Present don't double-count).
using OnPresentFn = void (*)();

void SetOnPresent(OnPresentFn fn);

// Arms a bare IDXGIFactory1/2 vtable hook on CreateSwapChain(#10) and
// CreateSwapChainForHwnd(#15). When the game creates its swap chain we hook its
// Present(#8)/Present1(#22). Returns false if DXGI/factory could not be set up.
bool InstallD3D11PresentHook();

// Fire the per-present orchestrator from a non-DXGI present source (OpenGL), with the
// same re-entrancy guard so a wglSwapBuffers -> gdi32 SwapBuffers nest counts once.
void FirePresentExternal();

// Inline-hook gdi32!SwapBuffers (MinHook). This is the present point for OpenGL on Windows:
// modern GLFW apps (Minecraft 1.13+) call it directly, and legacy wglSwapBuffers funnels
// into it too - so one hook catches both. Only meaningful in the injected build (FLCD_HAS_GL).
bool InstallOpenGLPresentHook();

} // namespace flcd::hooks
