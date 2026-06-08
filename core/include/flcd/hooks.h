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

} // namespace flcd::hooks
