// D3D11/DXGI present hook (P0). Generalized from the NieR Replicant work.
//
// Strategy (validated): create a bare IDXGIFactory1 (no device, no GPU resources -
// a dummy *device* was what black-screened earlier), vtable-hook the factory's
// CreateSwapChain / CreateSwapChainForHwnd, and when the game creates its swap chain
// hook that chain's Present / Present1. Vtables are per-class shared, so the game's
// objects route through our hooks. We never touch SyncInterval here; pacing and
// telemetry happen in the OnPresent callback.

#include <windows.h>
#include <dxgi1_2.h>
#include "flcd/hooks.h"
#include "flcd/overlay.h"
#include "flcd/log.h"

namespace flcd::hooks {
namespace {

OnPresentFn  g_onPresent = nullptr;
volatile LONG g_inPresent = 0;   // re-entrancy guard (Present1 -> Present nesting)

typedef HRESULT(STDMETHODCALLTYPE* Present_t)(IDXGISwapChain*, UINT, UINT);
typedef HRESULT(STDMETHODCALLTYPE* Present1_t)(IDXGISwapChain1*, UINT, UINT, const DXGI_PRESENT_PARAMETERS*);
Present_t  oPresent  = nullptr;
Present1_t oPresent1 = nullptr;

void FireOnce(IDXGISwapChain* sc)
{
    if (InterlockedCompareExchange(&g_inPresent, 1, 0) == 0) {
        if (g_onPresent) g_onPresent();
        flcd::overlay::OnPresent(sc);   // draw HUD into the back buffer before present
        InterlockedExchange(&g_inPresent, 0);
    }
}

HRESULT STDMETHODCALLTYPE hkPresent(IDXGISwapChain* s, UINT sync, UINT flags)
{
    FireOnce(s);
    return oPresent(s, sync, flags);
}
HRESULT STDMETHODCALLTYPE hkPresent1(IDXGISwapChain1* s, UINT sync, UINT flags, const DXGI_PRESENT_PARAMETERS* p)
{
    FireOnce((IDXGISwapChain*)s);
    return oPresent1(s, sync, flags, p);
}

bool PatchVtable(void** vtbl, int idx, void* hook, void** saveOrig)
{
    if (*saveOrig) return true;
    DWORD old;
    if (!VirtualProtect(&vtbl[idx], sizeof(void*), PAGE_EXECUTE_READWRITE, &old)) return false;
    *saveOrig = vtbl[idx];
    vtbl[idx] = hook;
    VirtualProtect(&vtbl[idx], sizeof(void*), old, &old);
    return true;
}

void HookSwapChain(IDXGISwapChain* sc)
{
    if (!sc || oPresent) return;     // hook once
    void** vt = *reinterpret_cast<void***>(sc);
    PatchVtable(vt, 8, (void*)&hkPresent, (void**)&oPresent);
    IDXGISwapChain1* sc1 = nullptr;
    if (SUCCEEDED(sc->QueryInterface(__uuidof(IDXGISwapChain1), (void**)&sc1)) && sc1) {
        void** vt1 = *reinterpret_cast<void***>(sc1);
        PatchVtable(vt1, 22, (void*)&hkPresent1, (void**)&oPresent1);
        sc1->Release();
    }
    flcd::Log("present hooked: Present=%p Present1=%p", oPresent, oPresent1);
}

// ---- factory hooks ----
typedef HRESULT(STDMETHODCALLTYPE* CreateSwapChain_t)(IDXGIFactory*, IUnknown*, DXGI_SWAP_CHAIN_DESC*, IDXGISwapChain**);
typedef HRESULT(STDMETHODCALLTYPE* CreateSwapChainForHwnd_t)(IDXGIFactory2*, IUnknown*, HWND,
                  const DXGI_SWAP_CHAIN_DESC1*, const DXGI_SWAP_CHAIN_FULLSCREEN_DESC*, IDXGIOutput*, IDXGISwapChain1**);
CreateSwapChain_t        oCreateSwapChain        = nullptr;
CreateSwapChainForHwnd_t oCreateSwapChainForHwnd = nullptr;

HRESULT STDMETHODCALLTYPE hkCreateSwapChain(IDXGIFactory* self, IUnknown* dev,
                  DXGI_SWAP_CHAIN_DESC* desc, IDXGISwapChain** pp)
{
    HRESULT hr = oCreateSwapChain(self, dev, desc, pp);
    if (SUCCEEDED(hr) && pp) HookSwapChain(*pp);
    return hr;
}
HRESULT STDMETHODCALLTYPE hkCreateSwapChainForHwnd(IDXGIFactory2* self, IUnknown* dev, HWND hwnd,
                  const DXGI_SWAP_CHAIN_DESC1* desc, const DXGI_SWAP_CHAIN_FULLSCREEN_DESC* fs,
                  IDXGIOutput* restrict_, IDXGISwapChain1** pp)
{
    HRESULT hr = oCreateSwapChainForHwnd(self, dev, hwnd, desc, fs, restrict_, pp);
    if (SUCCEEDED(hr) && pp) HookSwapChain((IDXGISwapChain*)*pp);
    return hr;
}

} // namespace

void SetOnPresent(OnPresentFn fn) { g_onPresent = fn; }

// Shared per-present fire for non-DXGI present sources (OpenGL). Reuses the same guard so a
// nested wglSwapBuffers -> gdi32 SwapBuffers (or any re-entry) is counted exactly once.
void FirePresentExternal()
{
    if (InterlockedCompareExchange(&g_inPresent, 1, 0) == 0) {
        if (g_onPresent) g_onPresent();
        InterlockedExchange(&g_inPresent, 0);
    }
}

bool InstallD3D11PresentHook()
{
    HMODULE dxgi = LoadLibraryW(L"dxgi.dll");
    if (!dxgi) { flcd::Log("hooks: no dxgi.dll"); return false; }
    typedef HRESULT(WINAPI* CF1_t)(REFIID, void**);
    auto pCF1 = (CF1_t)GetProcAddress(dxgi, "CreateDXGIFactory1");
    if (!pCF1) { flcd::Log("hooks: no CreateDXGIFactory1"); return false; }

    IDXGIFactory1* f1 = nullptr;
    if (FAILED(pCF1(__uuidof(IDXGIFactory1), (void**)&f1)) || !f1) { flcd::Log("hooks: factory failed"); return false; }
    void** vt = *reinterpret_cast<void***>(f1);
    PatchVtable(vt, 10, (void*)&hkCreateSwapChain, (void**)&oCreateSwapChain);
    IDXGIFactory2* f2 = nullptr;
    if (SUCCEEDED(f1->QueryInterface(__uuidof(IDXGIFactory2), (void**)&f2)) && f2) {
        void** vt2 = *reinterpret_cast<void***>(f2);
        PatchVtable(vt2, 15, (void*)&hkCreateSwapChainForHwnd, (void**)&oCreateSwapChainForHwnd);
        f2->Release();
    }
    f1->Release();
    flcd::Log("hooks: D3D11 factory hooks armed");
    return true;
}

} // namespace flcd::hooks
