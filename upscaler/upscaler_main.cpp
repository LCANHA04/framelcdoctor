// flcd_upscaler.exe - external window upscaler (Magpie/Lossless-Scaling style).
// Captures a target window via Windows.Graphics.Capture (no injection) and renders it
// upscaled to a borderless fullscreen window. The game runs windowed at a low resolution;
// the GPU draws fewer pixels -> more fps; we upscale cheaply.
//
//   flcd_upscaler.exe --hwnd <decimal>     (target window handle, from the companion)
//   flcd_upscaler.exe --title <substring>  (find a window by title - handy for testing)
//
// Esc / End quits. Quits automatically if the target window closes.

#include <windows.h>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <d3dcompiler.h>
#include <string>
#include <mutex>

#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>

#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "d3dcompiler.lib")
#pragma comment(lib, "windowsapp.lib")
#pragma comment(lib, "dwmapi.lib")

namespace wgc = winrt::Windows::Graphics::Capture;
namespace wgd = winrt::Windows::Graphics::DirectX;
using winrt::com_ptr;

// ---- globals ----
static HWND  g_target = nullptr;
static HWND  g_wnd = nullptr;
static bool  g_running = true;

// The frame pool is free-threaded: OnFrame fires on a pool thread while the main loop
// renders. ID3D11DeviceContext (immediate) is NOT thread-safe, and g_shaderTex/g_srv are
// swapped by OnFrame while RenderFrame reads them -> guard both with one lock.
static std::mutex g_ctxMtx;

// cursor forwarding (toggle with HOME): map a virtual cursor over the upscaled view to
// the real pixel in the small game window, so menu clicks land right.
static int   g_mx = 0, g_my = 0, g_sw = 0, g_sh = 0;   // upscaler monitor rect
static float g_vx = 0, g_vy = 0;                        // virtual cursor (monitor space)
static bool  g_forward = false;
static bool  g_prevHome = false;

static com_ptr<ID3D11Device>           g_dev;
static com_ptr<ID3D11DeviceContext>    g_ctx;
static com_ptr<IDXGISwapChain1>        g_swap;
static com_ptr<ID3D11RenderTargetView> g_rtv;
static com_ptr<ID3D11VertexShader>     g_vs;
static com_ptr<ID3D11PixelShader>      g_ps;
static com_ptr<ID3D11SamplerState>     g_sampler;
static com_ptr<ID3D11Texture2D>        g_shaderTex;   // SRV-bindable copy of the captured frame
static com_ptr<ID3D11ShaderResourceView> g_srv;
static UINT g_texW = 0, g_texH = 0;

static wgc::Direct3D11CaptureFramePool g_framePool{ nullptr };
static wgc::GraphicsCaptureSession     g_session{ nullptr };
static wgc::GraphicsCaptureItem        g_item{ nullptr };

// ---- shaders (fullscreen triangle, linear-sampled upscale) ----
static const char* kVS =
"struct VSOut{float4 pos:SV_POSITION;float2 uv:TEXCOORD;};"
"VSOut main(uint id:SV_VertexID){VSOut o;float2 t=float2((id<<1)&2,id&2);"
"o.uv=t;o.pos=float4(t*float2(2,-2)+float2(-1,1),0,1);return o;}";
static const char* kPS =
"Texture2D tx:register(t0);SamplerState sm:register(s0);"
"float4 main(float4 p:SV_POSITION,float2 uv:TEXCOORD):SV_TARGET{return tx.Sample(sm,uv);}";

static com_ptr<ID3D11Device> CreateDeviceBGRA()
{
    com_ptr<ID3D11Device> dev;
    D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr,
        D3D11_CREATE_DEVICE_BGRA_SUPPORT, nullptr, 0, D3D11_SDK_VERSION,
        dev.put(), nullptr, g_ctx.put());
    return dev;
}

static winrt::Windows::Graphics::DirectX::Direct3D11::IDirect3DDevice WinrtDevice()
{
    com_ptr<IDXGIDevice> dxgi;
    g_dev->QueryInterface(dxgi.put());
    winrt::com_ptr<::IInspectable> insp;
    CreateDirect3D11DeviceFromDXGIDevice(dxgi.get(), insp.put());
    return insp.as<winrt::Windows::Graphics::DirectX::Direct3D11::IDirect3DDevice>();
}

static void CreateRTV()
{
    com_ptr<ID3D11Texture2D> bb;
    g_swap->GetBuffer(0, __uuidof(ID3D11Texture2D), bb.put_void());
    g_rtv = nullptr;
    g_dev->CreateRenderTargetView(bb.get(), nullptr, g_rtv.put());
}

static void OnFrame(wgc::Direct3D11CaptureFramePool const& pool, winrt::Windows::Foundation::IInspectable const&)
{
    auto frame = pool.TryGetNextFrame();
    if (!frame) return;
    auto access = frame.Surface().as<Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();
    com_ptr<ID3D11Texture2D> src;
    access->GetInterface(__uuidof(ID3D11Texture2D), src.put_void());
    if (!src) return;

    D3D11_TEXTURE2D_DESC sd; src->GetDesc(&sd);
    std::lock_guard<std::mutex> lk(g_ctxMtx);
    if (!g_shaderTex || sd.Width != g_texW || sd.Height != g_texH) {
        g_texW = sd.Width; g_texH = sd.Height;
        D3D11_TEXTURE2D_DESC td = {};
        td.Width = g_texW; td.Height = g_texH; td.MipLevels = 1; td.ArraySize = 1;
        td.Format = sd.Format; td.SampleDesc.Count = 1; td.Usage = D3D11_USAGE_DEFAULT;
        td.BindFlags = D3D11_BIND_SHADER_RESOURCE;
        g_shaderTex = nullptr; g_srv = nullptr;
        g_dev->CreateTexture2D(&td, nullptr, g_shaderTex.put());
        if (g_shaderTex) g_dev->CreateShaderResourceView(g_shaderTex.get(), nullptr, g_srv.put());
    }
    if (g_shaderTex) g_ctx->CopyResource(g_shaderTex.get(), src.get());
}

static void RenderFrame()
{
    std::lock_guard<std::mutex> lk(g_ctxMtx);
    if (!g_srv) return;
    RECT rc; GetClientRect(g_wnd, &rc);
    D3D11_VIEWPORT vp = {}; vp.Width = (float)(rc.right - rc.left); vp.Height = (float)(rc.bottom - rc.top); vp.MaxDepth = 1;
    g_ctx->RSSetViewports(1, &vp);
    ID3D11RenderTargetView* rtv = g_rtv.get();
    g_ctx->OMSetRenderTargets(1, &rtv, nullptr);
    float clear[4] = { 0,0,0,1 }; g_ctx->ClearRenderTargetView(rtv, clear);
    g_ctx->VSSetShader(g_vs.get(), nullptr, 0);
    g_ctx->PSSetShader(g_ps.get(), nullptr, 0);
    ID3D11ShaderResourceView* srv = g_srv.get(); g_ctx->PSSetShaderResources(0, 1, &srv);
    ID3D11SamplerState* smp = g_sampler.get(); g_ctx->PSSetSamplers(0, 1, &smp);
    g_ctx->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    g_ctx->IASetInputLayout(nullptr);
    g_ctx->Draw(3, 0);
    g_swap->Present(1, 0);
}

static LRESULT CALLBACK WndProc(HWND h, UINT m, WPARAM w, LPARAM l)
{
    if (m == WM_DESTROY) { g_running = false; PostQuitMessage(0); return 0; }
    if (m == WM_INPUT && g_forward) {
        UINT sz = 0;
        GetRawInputData((HRAWINPUT)l, RID_INPUT, nullptr, &sz, sizeof(RAWINPUTHEADER));
        BYTE buf[64];
        if (sz && sz <= sizeof(buf) && GetRawInputData((HRAWINPUT)l, RID_INPUT, buf, &sz, sizeof(RAWINPUTHEADER)) == sz) {
            auto* ri = (RAWINPUT*)buf;
            if (ri->header.dwType == RIM_TYPEMOUSE && !(ri->data.mouse.usFlags & MOUSE_MOVE_ABSOLUTE)) {
                g_vx += ri->data.mouse.lLastX; g_vy += ri->data.mouse.lLastY;
                if (g_vx < g_mx) g_vx = (float)g_mx; if (g_vy < g_my) g_vy = (float)g_my;
                if (g_vx > g_mx + g_sw - 1) g_vx = (float)(g_mx + g_sw - 1);
                if (g_vy > g_my + g_sh - 1) g_vy = (float)(g_my + g_sh - 1);
            }
        }
        return 0;
    }
    return DefWindowProc(h, m, w, l);
}

struct FindCtx { std::wstring needle; HWND found; };
static BOOL CALLBACK EnumProc(HWND h, LPARAM p)
{
    auto* c = (FindCtx*)p;
    if (!IsWindowVisible(h)) return TRUE;
    wchar_t t[256]; GetWindowTextW(h, t, 256);
    std::wstring title(t);
    if (!title.empty() && title.find(c->needle) != std::wstring::npos) { c->found = h; return FALSE; }
    return TRUE;
}

int WINAPI wWinMain(HINSTANCE hInst, HINSTANCE, LPWSTR cmd, int)
{
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    winrt::init_apartment(winrt::apartment_type::single_threaded);

    // parse args
    int argc = 0; LPWSTR* argv = CommandLineToArgvW(cmd, &argc);
    for (int i = 0; i + 1 < argc; ++i) {
        if (!wcscmp(argv[i], L"--hwnd")) g_target = (HWND)(intptr_t)_wtoi64(argv[i + 1]);
        else if (!wcscmp(argv[i], L"--title")) { FindCtx c{ argv[i + 1], nullptr }; EnumWindows(EnumProc, (LPARAM)&c); g_target = c.found; }
    }
    if (!g_target || !IsWindow(g_target)) { MessageBoxW(nullptr, L"Ventana objetivo no encontrada.", L"flcd_upscaler", MB_ICONERROR); return 1; }

    // fullscreen borderless window on the primary monitor
    WNDCLASSW wc = {}; wc.lpfnWndProc = WndProc; wc.hInstance = hInst; wc.lpszClassName = L"flcd_upscaler";
    wc.hCursor = LoadCursor(nullptr, IDC_ARROW); RegisterClassW(&wc);
    // cover the monitor that the target window is on (multi-monitor aware), not always primary
    HMONITOR mon = MonitorFromWindow(g_target, MONITOR_DEFAULTTONEAREST);
    MONITORINFO mi = { sizeof(mi) }; GetMonitorInfo(mon, &mi);
    int mx = mi.rcMonitor.left, my = mi.rcMonitor.top;
    int sw = mi.rcMonitor.right - mi.rcMonitor.left, sh = mi.rcMonitor.bottom - mi.rcMonitor.top;
    g_mx = mx; g_my = my; g_sw = sw; g_sh = sh; g_vx = mx + sw / 2.f; g_vy = my + sh / 2.f;
    // NOACTIVATE: never steal focus. TRANSPARENT: click-through, so a physical click passes
    // through to the game window at the forwarded cursor position (works for single-window
    // games; modern WinUI apps like Notepad ignore this - they're not the target).
    g_wnd = CreateWindowExW(WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT, wc.lpszClassName,
        L"FrameLCDoctor Upscaler", WS_POPUP, mx, my, sw, sh, nullptr, nullptr, hInst, nullptr);
    ShowWindow(g_wnd, SW_SHOWNA);

    RAWINPUTDEVICE rid = { 0x01, 0x02, RIDEV_INPUTSINK, g_wnd };   // generic mouse, even unfocused
    RegisterRawInputDevices(&rid, 1, sizeof(rid));

    // D3D11 + swapchain
    g_dev = CreateDeviceBGRA();
    if (!g_dev) { MessageBoxW(nullptr, L"No pude crear el device D3D11.", L"flcd_upscaler", MB_ICONERROR); return 1; }
    com_ptr<IDXGIDevice> dxgiDev; g_dev->QueryInterface(dxgiDev.put());
    com_ptr<IDXGIAdapter> adapter; dxgiDev->GetAdapter(adapter.put());
    com_ptr<IDXGIFactory2> factory; adapter->GetParent(__uuidof(IDXGIFactory2), factory.put_void());
    DXGI_SWAP_CHAIN_DESC1 scd = {}; scd.Width = sw; scd.Height = sh; scd.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    scd.SampleDesc.Count = 1; scd.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT; scd.BufferCount = 2;
    scd.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;
    factory->CreateSwapChainForHwnd(g_dev.get(), g_wnd, &scd, nullptr, nullptr, g_swap.put());
    CreateRTV();

    // shaders + sampler
    com_ptr<ID3DBlob> vsb, psb, err;
    D3DCompile(kVS, strlen(kVS), nullptr, nullptr, nullptr, "main", "vs_5_0", 0, 0, vsb.put(), err.put());
    D3DCompile(kPS, strlen(kPS), nullptr, nullptr, nullptr, "main", "ps_5_0", 0, 0, psb.put(), nullptr);
    g_dev->CreateVertexShader(vsb->GetBufferPointer(), vsb->GetBufferSize(), nullptr, g_vs.put());
    g_dev->CreatePixelShader(psb->GetBufferPointer(), psb->GetBufferSize(), nullptr, g_ps.put());
    D3D11_SAMPLER_DESC sm = {}; sm.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
    sm.AddressU = sm.AddressV = sm.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
    g_dev->CreateSamplerState(&sm, g_sampler.put());

    // capture session on the target window
    auto interop = winrt::get_activation_factory<wgc::GraphicsCaptureItem, ::IGraphicsCaptureItemInterop>();
    interop->CreateForWindow(g_target, winrt::guid_of<wgc::GraphicsCaptureItem>(), winrt::put_abi(g_item));
    g_framePool = wgc::Direct3D11CaptureFramePool::CreateFreeThreaded(
        WinrtDevice(), wgd::DirectXPixelFormat::B8G8R8A8UIntNormalized, 2, g_item.Size());
    g_framePool.FrameArrived({ &OnFrame });
    g_session = g_framePool.CreateCaptureSession(g_item);
    g_session.IsCursorCaptureEnabled(true);   // so the forwarded cursor shows in the view
    g_item.Closed([](auto&&, auto&&) { g_running = false; PostQuitMessage(0); });
    g_session.StartCapture();

    // give input focus back to the game (we're a no-activate display on top of it)
    SetForegroundWindow(g_target);
    SetFocus(g_target);

    // loop. The window doesn't take focus, so quitting is a polled global hotkey (END).
    MSG msg{};
    while (g_running) {
        while (PeekMessage(&msg, nullptr, 0, 0, PM_REMOVE)) {
            if (msg.message == WM_QUIT) { g_running = false; break; }
            TranslateMessage(&msg); DispatchMessage(&msg);
        }
        if (!IsWindow(g_target)) break;
        if (GetAsyncKeyState(VK_END) & 0x8000) break;   // END = quit upscaler

        // HOME toggles cursor forwarding (off = gameplay/mouse-look; on = clickable menus)
        bool home = (GetAsyncKeyState(VK_HOME) & 0x8000) != 0;
        if (home && !g_prevHome) { g_forward = !g_forward; g_vx = g_mx + g_sw / 2.f; g_vy = g_my + g_sh / 2.f; }
        g_prevHome = home;

        if (g_forward) {   // map virtual cursor over the view -> real pixel in the game window
            POINT o = { 0, 0 }; ClientToScreen(g_target, &o);
            RECT cr; GetClientRect(g_target, &cr);
            int cw = cr.right - cr.left, chh = cr.bottom - cr.top;
            if (cw > 0 && chh > 0) {
                float relX = (g_vx - g_mx) / (float)g_sw, relY = (g_vy - g_my) / (float)g_sh;
                // put the real OS cursor over the matching pixel of the game window; clicks
                // pass through our transparent window to the game there.
                SetCursorPos((int)(o.x + relX * cw), (int)(o.y + relY * chh));
            }
        }
        RenderFrame();
    }

    if (g_session) g_session.Close();
    if (g_framePool) g_framePool.Close();
    return 0;
}
