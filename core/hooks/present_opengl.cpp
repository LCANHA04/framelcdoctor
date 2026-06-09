// OpenGL present hook (P5). On Windows the OpenGL frame is presented by gdi32!SwapBuffers:
// modern GLFW apps (Minecraft 1.13+ via LWJGL3) call it directly, and legacy
// opengl32!wglSwapBuffers funnels into it as well - so a single inline hook on
// gdi32!SwapBuffers catches every OpenGL frame. We can't proxy gdi32 (it's a KnownDLL), so
// this build is injected (CreateRemoteThread) and uses MinHook for the inline hook.
//
// Only compiled into the injectable core (FLCD_HAS_GL); the beside-the-exe D3D proxies don't
// need it. The hook just times the frame via the shared FirePresentExternal() and forwards.

#include <windows.h>
#include "flcd/hooks.h"
#include "flcd/log.h"
#include "MinHook.h"

namespace flcd::hooks {
namespace {

typedef BOOL(WINAPI* SwapBuffers_t)(HDC);
SwapBuffers_t oSwapBuffers = nullptr;

BOOL WINAPI hkSwapBuffers(HDC hdc)
{
    FirePresentExternal();
    return oSwapBuffers(hdc);
}

} // namespace

bool InstallOpenGLPresentHook()
{
    if (MH_Initialize() != MH_OK) { flcd::Log("gl: MH_Initialize failed"); return false; }
    MH_STATUS s = MH_CreateHookApi(L"gdi32", "SwapBuffers", (void*)&hkSwapBuffers, (void**)&oSwapBuffers);
    if (s != MH_OK) { flcd::Log("gl: hook gdi32!SwapBuffers failed (%d)", (int)s); return false; }
    if (MH_EnableHook(MH_ALL_HOOKS) != MH_OK) { flcd::Log("gl: MH_EnableHook failed"); return false; }
    flcd::Log("gl: gdi32!SwapBuffers hooked (orig=%p)", oSwapBuffers);
    return true;
}

} // namespace flcd::hooks
