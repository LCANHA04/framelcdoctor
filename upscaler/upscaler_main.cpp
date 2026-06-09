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

// ---- frame generation (toggle PAGE UP) ----
// MVP: motion-compensated interpolation between the last two captured frames. A compute
// shader does block-matching optical flow (per-block motion vectors); a pixel shader warps
// both frames toward the midpoint and blends. We present the interpolated frame, then the
// real one -> ~2x displayed frames. External (capture-based) so it works on any API, but it
// adds ~1 frame of latency and can show artifacts on disocclusion/fast motion (expected MVP).
static const int FG_BLOCK  = 16;   // motion-search block size (px)
static const int FG_SEARCH = 8;    // motion-search radius (px)
static bool g_fg = false;          // requested via --framegen
static bool g_fgOn = false;        // live toggle (PAGE UP)
static bool g_prevPgUp = false;
static com_ptr<ID3D11Texture2D>          g_prevTex;   // previous captured frame (SRV)
static com_ptr<ID3D11ShaderResourceView> g_prevSrv;
static bool g_havePrev = false;                        // a real previous frame exists
static com_ptr<ID3D11ComputeShader>      g_csMotion;
static com_ptr<ID3D11PixelShader>        g_psInterp;
static com_ptr<ID3D11Texture2D>          g_mvTex;     // per-block motion vectors (RG16F)
static com_ptr<ID3D11UnorderedAccessView> g_mvUav;
static com_ptr<ID3D11ShaderResourceView> g_mvSrv;
static com_ptr<ID3D11Buffer>             g_cbMotion;  // {fullDims, mvDims, block, search}
static com_ptr<ID3D11Buffer>             g_cbInterp;  // {invFull, tPhase}
static UINT g_blocksX = 0, g_blocksY = 0;

// ---- shaders (fullscreen triangle, linear-sampled upscale) ----
static const char* kVS =
"struct VSOut{float4 pos:SV_POSITION;float2 uv:TEXCOORD;};"
"VSOut main(uint id:SV_VertexID){VSOut o;float2 t=float2((id<<1)&2,id&2);"
"o.uv=t;o.pos=float4(t*float2(2,-2)+float2(-1,1),0,1);return o;}";
static const char* kPS =
"Texture2D tx:register(t0);SamplerState sm:register(s0);"
"float4 main(float4 p:SV_POSITION,float2 uv:TEXCOORD):SV_TARGET{return tx.Sample(sm,uv);}";

// block-matching optical flow: one motion vector per FG_BLOCK tile. For each tile, search a
// +/-srch window in the previous frame for the lowest SAD match (subsampled by 4 for speed).
// MV stored in pixels such that Prev(p + mv) ~= Cur(p).
static const char* kCSMotion =
"cbuffer Cb:register(b0){uint2 fullDims;uint2 mvDims;int blk;int srch;float2 pad;};"
"Texture2D<float4> Cur:register(t0);Texture2D<float4> Prev:register(t1);"
"RWTexture2D<float2> MV:register(u0);"
"[numthreads(8,8,1)]void main(uint3 id:SV_DispatchThreadID){"
" if(id.x>=mvDims.x||id.y>=mvDims.y)return;"
" int2 ctr=int2(id.xy)*blk+blk/2;int2 lo=int2(0,0);int2 hi=int2(fullDims)-1;"
" float best=1e30;int2 bmv=int2(0,0);"
" for(int dy=-srch;dy<=srch;dy++){for(int dx=-srch;dx<=srch;dx++){"
"  float sad=0;"
"  for(int sy=-blk/2;sy<blk/2;sy+=4){for(int sx=-blk/2;sx<blk/2;sx+=4){"
"   int2 pc=clamp(ctr+int2(sx,sy),lo,hi);int2 pp=clamp(pc+int2(dx,dy),lo,hi);"
"   float3 a=Cur.Load(int3(pc,0)).rgb;float3 b=Prev.Load(int3(pp,0)).rgb;"
"   sad+=abs(a.r-b.r)+abs(a.g-b.g)+abs(a.b-b.b);}}"
"  if(sad<best){best=sad;bmv=int2(dx,dy);}}}"
" MV[id.xy]=float2(bmv);}";

// synthesis: warp Cur and Prev toward the midpoint by the (bilinearly-sampled) motion field
// and blend. tPhase=0.5 = halfway frame.
static const char* kPSInterp =
"cbuffer Cb:register(b0){float2 invFull;float tPhase;float pad;};"
"Texture2D Cur:register(t0);Texture2D Prev:register(t1);Texture2D<float2> MV:register(t2);"
"SamplerState sm:register(s0);"
"float4 main(float4 p:SV_POSITION,float2 uv:TEXCOORD):SV_TARGET{"
" float2 mvUV=MV.SampleLevel(sm,uv,0)*invFull;"
" float3 a=Cur.SampleLevel(sm,uv+tPhase*mvUV,0).rgb;"
" float3 b=Prev.SampleLevel(sm,uv-(1.0-tPhase)*mvUV,0).rgb;"
" return float4(lerp(a,b,tPhase),1);}";

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
        if (g_fg) {   // matching previous-frame texture for interpolation
            g_prevTex = nullptr; g_prevSrv = nullptr; g_havePrev = false;
            g_mvTex = nullptr; g_mvUav = nullptr; g_mvSrv = nullptr; g_blocksX = 0;
            g_dev->CreateTexture2D(&td, nullptr, g_prevTex.put());
            if (g_prevTex) g_dev->CreateShaderResourceView(g_prevTex.get(), nullptr, g_prevSrv.put());
        }
    }
    if (g_shaderTex) {
        // keep the just-displayed frame as "previous" before overwriting with the new one
        if (g_fg && g_prevTex) { g_ctx->CopyResource(g_prevTex.get(), g_shaderTex.get()); g_havePrev = true; }
        g_ctx->CopyResource(g_shaderTex.get(), src.get());
    }
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

// lazily build the frame-gen pipeline once the captured frame size is known. Returns false
// if anything fails (caller then just shows real frames).
static bool FrameGenEnsure()
{
    if (!g_csMotion) {
        com_ptr<ID3DBlob> csb, psb, err;
        D3DCompile(kCSMotion, strlen(kCSMotion), nullptr, nullptr, nullptr, "main", "cs_5_0", 0, 0, csb.put(), err.put());
        if (!csb) return false;
        g_dev->CreateComputeShader(csb->GetBufferPointer(), csb->GetBufferSize(), nullptr, g_csMotion.put());
        D3DCompile(kPSInterp, strlen(kPSInterp), nullptr, nullptr, nullptr, "main", "ps_5_0", 0, 0, psb.put(), nullptr);
        if (!psb) return false;
        g_dev->CreatePixelShader(psb->GetBufferPointer(), psb->GetBufferSize(), nullptr, g_psInterp.put());
        D3D11_BUFFER_DESC bd = {}; bd.Usage = D3D11_USAGE_DEFAULT; bd.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
        bd.ByteWidth = 32; g_dev->CreateBuffer(&bd, nullptr, g_cbMotion.put());
        bd.ByteWidth = 16; g_dev->CreateBuffer(&bd, nullptr, g_cbInterp.put());
    }
    if (!g_csMotion || !g_psInterp || !g_cbMotion || !g_cbInterp) return false;

    if (!g_mvTex && g_texW) {
        g_blocksX = (g_texW + FG_BLOCK - 1) / FG_BLOCK;
        g_blocksY = (g_texH + FG_BLOCK - 1) / FG_BLOCK;
        D3D11_TEXTURE2D_DESC td = {};
        td.Width = g_blocksX; td.Height = g_blocksY; td.MipLevels = 1; td.ArraySize = 1;
        td.Format = DXGI_FORMAT_R16G16_FLOAT; td.SampleDesc.Count = 1; td.Usage = D3D11_USAGE_DEFAULT;
        td.BindFlags = D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_UNORDERED_ACCESS;
        g_dev->CreateTexture2D(&td, nullptr, g_mvTex.put());
        if (g_mvTex) { g_dev->CreateUnorderedAccessView(g_mvTex.get(), nullptr, g_mvUav.put());
                       g_dev->CreateShaderResourceView(g_mvTex.get(), nullptr, g_mvSrv.put()); }
    }
    return g_mvUav && g_mvSrv;
}

// estimate motion (compute) then present one interpolated mid-frame. Returns false if not
// ready (no previous frame yet, resources missing) so the caller can skip it this iteration.
static bool RenderInterp()
{
    std::lock_guard<std::mutex> lk(g_ctxMtx);
    if (!g_fgOn || !g_srv || !g_prevSrv || !g_havePrev) return false;
    if (!FrameGenEnsure()) return false;

    struct { UINT fw, fh, mw, mh; int blk, srch, p0, p1; } mcb =
        { g_texW, g_texH, g_blocksX, g_blocksY, FG_BLOCK, FG_SEARCH, 0, 0 };
    g_ctx->UpdateSubresource(g_cbMotion.get(), 0, nullptr, &mcb, 0, 0);

    // --- motion estimation pass ---
    ID3D11ShaderResourceView* csIn[2] = { g_srv.get(), g_prevSrv.get() };
    ID3D11UnorderedAccessView* uav = g_mvUav.get();
    ID3D11Buffer* mcbuf = g_cbMotion.get();
    g_ctx->CSSetShader(g_csMotion.get(), nullptr, 0);
    g_ctx->CSSetShaderResources(0, 2, csIn);
    g_ctx->CSSetUnorderedAccessViews(0, 1, &uav, nullptr);
    g_ctx->CSSetConstantBuffers(0, 1, &mcbuf);
    g_ctx->Dispatch((g_blocksX + 7) / 8, (g_blocksY + 7) / 8, 1);
    // unbind so the MV texture can be read as an SRV in the synthesis pass
    ID3D11UnorderedAccessView* noUav = nullptr; g_ctx->CSSetUnorderedAccessViews(0, 1, &noUav, nullptr);
    ID3D11ShaderResourceView* noSrv2[2] = { nullptr, nullptr }; g_ctx->CSSetShaderResources(0, 2, noSrv2);

    // --- synthesis pass (warp + blend to the midpoint) ---
    struct { float ifx, ify, t, p; } icb = { 1.0f / g_texW, 1.0f / g_texH, 0.5f, 0 };
    g_ctx->UpdateSubresource(g_cbInterp.get(), 0, nullptr, &icb, 0, 0);
    RECT rc; GetClientRect(g_wnd, &rc);
    D3D11_VIEWPORT vp = {}; vp.Width = (float)(rc.right - rc.left); vp.Height = (float)(rc.bottom - rc.top); vp.MaxDepth = 1;
    g_ctx->RSSetViewports(1, &vp);
    ID3D11RenderTargetView* rtv = g_rtv.get();
    g_ctx->OMSetRenderTargets(1, &rtv, nullptr);
    g_ctx->VSSetShader(g_vs.get(), nullptr, 0);
    g_ctx->PSSetShader(g_psInterp.get(), nullptr, 0);
    ID3D11ShaderResourceView* psIn[3] = { g_srv.get(), g_prevSrv.get(), g_mvSrv.get() };
    g_ctx->PSSetShaderResources(0, 3, psIn);
    ID3D11SamplerState* smp = g_sampler.get(); g_ctx->PSSetSamplers(0, 1, &smp);
    ID3D11Buffer* icbuf = g_cbInterp.get(); g_ctx->PSSetConstantBuffers(0, 1, &icbuf);
    g_ctx->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    g_ctx->IASetInputLayout(nullptr);
    g_ctx->Draw(3, 0);
    g_swap->Present(1, 0);
    ID3D11ShaderResourceView* noSrv3[3] = { nullptr, nullptr, nullptr }; g_ctx->PSSetShaderResources(0, 3, noSrv3);
    return true;
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
    for (int i = 0; i < argc; ++i) {
        if (!wcscmp(argv[i], L"--framegen")) { g_fg = true; g_fgOn = true; }
        else if (i + 1 < argc && !wcscmp(argv[i], L"--hwnd")) g_target = (HWND)(intptr_t)_wtoi64(argv[i + 1]);
        else if (i + 1 < argc && !wcscmp(argv[i], L"--title")) { FindCtx c{ argv[i + 1], nullptr }; EnumWindows(EnumProc, (LPARAM)&c); g_target = c.found; }
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

        // PAGE UP toggles frame generation (only meaningful if launched with --framegen)
        bool pgup = (GetAsyncKeyState(VK_PRIOR) & 0x8000) != 0;
        if (pgup && !g_prevPgUp && g_fg) g_fgOn = !g_fgOn;
        g_prevPgUp = pgup;

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
        // frame gen: present an interpolated mid-frame, then the real one (~2x displayed).
        // When off / not ready, just show the real frame.
        RenderInterp();
        RenderFrame();
    }

    if (g_session) g_session.Close();
    if (g_framePool) g_framePool.Close();
    return 0;
}
