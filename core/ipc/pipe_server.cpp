// Named-pipe server (P0). Streams profiler snapshots as JSON lines to the companion;
// reads inbound JSON lines to merge OS metrics (GPU%/CPU%) and (TODO) commands.
#include "flcd/ipc.h"
#include "flcd/profiler.h"
#include "flcd/headroom.h"
#include "flcd/log.h"
#include <windows.h>
#include <stdio.h>
#include <string.h>
#include <stdlib.h>

namespace flcd::ipc {
namespace {

volatile bool g_run = false;
HANDLE        g_thread = nullptr;

// minimal: pull a numeric field "key": <num> out of a JSON line
bool FindNum(const char* json, const char* key, double* out)
{
    char pat[64]; _snprintf_s(pat, sizeof(pat), _TRUNCATE, "\"%s\"", key);
    const char* p = strstr(json, pat);
    if (!p) return false;
    p = strchr(p, ':'); if (!p) return false;
    *out = atof(p + 1);
    return true;
}

void HandleInbound(const char* line)
{
    double gpu = -1, cpuPeak = -1, cpuTotal = -1;
    bool any = false;
    any |= FindNum(line, "gpu",      &gpu);
    any |= FindNum(line, "cpuPeak",  &cpuPeak);
    any |= FindNum(line, "cpuTotal", &cpuTotal);
    if (any) profiler::MergeOsMetrics(gpu, cpuPeak, cpuTotal);
    // TODO P2: parse {"cmd":"limiter","fps":..,"ppf":..} and drive the limiter.
}

const char* BottleneckStr(Bottleneck b)
{
    switch (b) {
        case Bottleneck::Gpu:             return "gpu";
        case Bottleneck::CpuSingleThread: return "cpu-single";
        case Bottleneck::CpuMultiThread:  return "cpu-multi";
        case Bottleneck::FrameCap:        return "cap";
        case Bottleneck::Balanced:        return "balanced";
        default:                          return "unknown";
    }
}

DWORD WINAPI ServerThread(LPVOID)
{
    while (g_run) {
        HANDLE pipe = CreateNamedPipeW(kPipeName,
            PIPE_ACCESS_DUPLEX,
            PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_NOWAIT,
            1, 4096, 4096, 0, nullptr);
        if (pipe == INVALID_HANDLE_VALUE) { Sleep(500); continue; }

        flcd::Log("ipc: waiting for companion...");
        // PIPE_NOWAIT: poll for a connection
        while (g_run) {
            BOOL ok = ConnectNamedPipe(pipe, nullptr);
            DWORD e = GetLastError();
            if (ok || e == ERROR_PIPE_CONNECTED) break;
            if (e == ERROR_NO_DATA || e == ERROR_PIPE_LISTENING) { Sleep(100); continue; }
            Sleep(100);
        }
        if (!g_run) { CloseHandle(pipe); break; }
        flcd::Log("ipc: companion connected");

        char rx[4096];
        while (g_run) {
            // inbound (non-blocking)
            DWORD n = 0;
            if (ReadFile(pipe, rx, sizeof(rx) - 1, &n, nullptr) && n) { rx[n] = 0; HandleInbound(rx); }

            // outbound: stream a snapshot + headroom verdict
            FrameSignals s = profiler::Snapshot();
            HeadroomReport h = headroom::Assess(s);
            char tx[1024];
            int len = _snprintf_s(tx, sizeof(tx), _TRUNCATE,
                "{\"type\":\"signals\",\"displayFps\":%.1f,\"presentRate\":%.1f,\"ppf\":%d,"
                "\"frametimeMs\":%.2f,\"frametimeP99\":%.2f,\"gpuBusyPct\":%.1f,\"cpuMainPct\":%.1f,\"cpuTotalPct\":%.1f,"
                "\"bottleneck\":\"%s\",\"utilizationIndex\":%.0f,\"headroomIndex\":%.0f,\"moreFpsLikely\":%s,"
                "\"verdict\":\"%s\",\"suggestion\":\"%s\"}\n",
                s.displayFps, s.presentRate, s.ppf, s.frametimeMs, s.frametimeP99,
                s.gpuBusyPct, s.cpuMainPct, s.cpuTotalPct,
                BottleneckStr(h.bottleneck), h.utilizationIndex, h.headroomIndex,
                h.moreFpsLikely ? "true" : "false", h.verdict.c_str(), h.suggestion.c_str());
            DWORD w = 0;
            if (len <= 0 || !WriteFile(pipe, tx, (DWORD)len, &w, nullptr)) {
                if (GetLastError() == ERROR_NO_DATA || GetLastError() == ERROR_BROKEN_PIPE) break;
            }
            Sleep(250);
        }
        DisconnectNamedPipe(pipe);
        CloseHandle(pipe);
        flcd::Log("ipc: companion disconnected");
    }
    return 0;
}

} // namespace

void StartServer()
{
    if (g_run) return;
    g_run = true;
    g_thread = CreateThread(nullptr, 0, ServerThread, nullptr, 0, nullptr);
}

void StopServer()
{
    g_run = false;
    if (g_thread) { WaitForSingleObject(g_thread, 1000); CloseHandle(g_thread); g_thread = nullptr; }
}

} // namespace flcd::ipc
