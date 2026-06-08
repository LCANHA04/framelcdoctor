// In-game ImGui overlay (display-only HUD), rendered into the game's D3D11 swap chain
// from the Present hook. Lazy-inits on first present. Hidden until a hotkey toggles it,
// so it never touches the rendered frame unless the user asks for it.
#include "flcd/overlay.h"
#include "flcd/profiler.h"
#include "flcd/headroom.h"
#include "flcd/log.h"

#include <windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include "imgui.h"
#include "backends/imgui_impl_dx11.h"
#include "backends/imgui_impl_win32.h"

namespace flcd::overlay {
namespace {

enum class State { Uninit, Ready, Failed };
State g_state = State::Uninit;

bool g_enabled = true;
int  g_vk      = VK_INSERT;   // toggle key
bool g_show    = false;       // hidden until toggled (safety)
bool g_prevDown = false;

ID3D11Device*        g_dev = nullptr;
ID3D11DeviceContext* g_ctx = nullptr;
HWND                 g_hwnd = nullptr;

void IniPath(wchar_t* p)
{
    GetModuleFileNameW(GetModuleHandleW(nullptr), p, MAX_PATH);
    if (wchar_t* s = wcsrchr(p, L'\\')) wcscpy_s(s + 1, MAX_PATH - (s + 1 - p), L"framelcdoctor.ini");
}

bool LazyInit(IDXGISwapChain* sc)
{
    if (FAILED(sc->GetDevice(__uuidof(ID3D11Device), (void**)&g_dev)) || !g_dev) { g_state = State::Failed; return false; }
    g_dev->GetImmediateContext(&g_ctx);
    DXGI_SWAP_CHAIN_DESC d = {};
    sc->GetDesc(&d);
    g_hwnd = d.OutputWindow;

    IMGUI_CHECKVERSION();
    ImGui::CreateContext();
    ImGuiIO& io = ImGui::GetIO();
    io.IniFilename = nullptr; io.LogFilename = nullptr;
    ImGui::StyleColorsDark();
    if (!ImGui_ImplWin32_Init(g_hwnd)) { g_state = State::Failed; return false; }
    if (!ImGui_ImplDX11_Init(g_dev, g_ctx)) { g_state = State::Failed; return false; }
    g_state = State::Ready;
    flcd::Log("overlay: ready (hwnd=%p). Toggle with hotkey 0x%X.", g_hwnd, g_vk);
    return true;
}

const char* BnText(Bottleneck b)
{
    switch (b) {
    case Bottleneck::Gpu:             return "GPU-bound";
    case Bottleneck::CpuSingleThread: return "CPU (1 core)";
    case Bottleneck::CpuMultiThread:  return "CPU (varios)";
    case Bottleneck::FrameCap:        return "Cap / liviano";
    case Bottleneck::Balanced:        return "Balanceado";
    default:                          return "--";
    }
}

void DrawHud()
{
    FrameSignals s = flcd::profiler::Snapshot();
    HeadroomReport h = flcd::headroom::Assess(s);

    ImGui::SetNextWindowPos(ImVec2(16, 16), ImGuiCond_Always);
    ImGui::SetNextWindowBgAlpha(0.55f);
    ImGuiWindowFlags flags = ImGuiWindowFlags_NoTitleBar | ImGuiWindowFlags_NoResize |
        ImGuiWindowFlags_NoMove | ImGuiWindowFlags_NoScrollbar | ImGuiWindowFlags_AlwaysAutoResize |
        ImGuiWindowFlags_NoInputs | ImGuiWindowFlags_NoNav;

    if (ImGui::Begin("FrameLCDoctor", nullptr, flags)) {
        ImGui::TextColored(ImVec4(0.31f, 0.64f, 1.0f, 1.0f), "FrameLCDoctor");
        ImGui::Separator();
        ImGui::Text("%.0f fps", s.displayFps);
        ImGui::SameLine(); ImGui::TextDisabled("(%.1f ms)", s.frametimeMs);
        ImGui::Text("1%% low: %.0f   0.1%% low: %.0f", s.low1Fps, s.low01Fps);
        ImGui::Separator();
        if (s.gpuBusyPct >= 0) ImGui::Text("GPU %.0f%%   CPU pico %.0f%%   total %.0f%%",
                                           s.gpuBusyPct, s.cpuMainPct, s.cpuTotalPct);
        else                   ImGui::TextDisabled("GPU/CPU: abri el panel de FrameLCDoctor");
        ImGui::Text("Cuello: %s   margen %.0f%%", BnText(h.bottleneck), h.headroomIndex);
        if (h.moreFpsLikely) { ImGui::SameLine(); ImGui::TextColored(ImVec4(0.36f, 0.84f, 0.48f, 1.0f), "+fps"); }
    }
    ImGui::End();
}

} // namespace

void Init()
{
    wchar_t ini[MAX_PATH]; IniPath(ini);
    g_enabled = GetPrivateProfileIntW(L"overlay", L"Enabled", 1, ini) != 0;
    g_vk      = GetPrivateProfileIntW(L"overlay", L"Hotkey", VK_INSERT, ini);
}

void OnPresent(IDXGISwapChain* sc)
{
    if (!g_enabled || !sc || g_state == State::Failed) return;
    if (g_state == State::Uninit && !LazyInit(sc)) return;

    bool down = (GetAsyncKeyState(g_vk) & 0x8000) != 0;
    if (down && !g_prevDown) g_show = !g_show;
    g_prevDown = down;
    if (!g_show) return;

    ImGui_ImplDX11_NewFrame();
    ImGui_ImplWin32_NewFrame();
    ImGui::NewFrame();
    DrawHud();
    ImGui::Render();

    ID3D11Texture2D* bb = nullptr;
    if (SUCCEEDED(sc->GetBuffer(0, __uuidof(ID3D11Texture2D), (void**)&bb)) && bb) {
        ID3D11RenderTargetView* rtv = nullptr;
        if (SUCCEEDED(g_dev->CreateRenderTargetView(bb, nullptr, &rtv)) && rtv) {
            g_ctx->OMSetRenderTargets(1, &rtv, nullptr);
            ImGui_ImplDX11_RenderDrawData(ImGui::GetDrawData());
            rtv->Release();
        }
        bb->Release();
    }
}

void Shutdown()
{
    if (g_state == State::Ready) {
        ImGui_ImplDX11_Shutdown();
        ImGui_ImplWin32_Shutdown();
        ImGui::DestroyContext();
    }
    g_state = State::Uninit;
}

} // namespace flcd::overlay
