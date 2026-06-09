// flcd_amddrs.exe - AMD driver optimizer (tasklist item #2, AMD path) via ADLX.
//
// AMD's modern 3D features (Anti-Lag, Frame Rate Target Control) live in ADLX and are set
// PER-GPU (global), not per-application like NVIDIA's NvAPI profiles. So this affects every
// game on the GPU; revert turns the features back off (driver default).
//
//   flcd_amddrs.exe --apply  [--fpscap N]    (enables Anti-Lag; FRTC cap if N>0)
//   flcd_amddrs.exe --revert                 (disables Anti-Lag + FRTC)
//
// Built only when the ADLX SDK is present (third_party/adlx). ADLXHelper loads amdadlx64.dll
// (ships with the AMD driver) at runtime, so there is nothing to link. Prints
// "step=<name> status=<int>" per call and "RESULT=OK|FAIL"; exit 0 on success.
// NOTE: not yet validated on AMD hardware - to test on the RX 6600 PC.

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "SDK/ADLXHelper/Windows/C/ADLXHelper.h"
#include "SDK/Include/I3DSettings.h"

static int report(const char* step, ADLX_RESULT r)
{
    printf("step=%s status=%d\n", step, (int)r);
    return (int)r;
}

int main(int argc, char** argv)
{
    int apply = 0, revert = 0, fpscap = 0;
    for (int i = 1; i < argc; ++i) {
        if (!strcmp(argv[i], "--apply")) apply = 1;
        else if (!strcmp(argv[i], "--revert")) revert = 1;
        else if (!strcmp(argv[i], "--fpscap") && i + 1 < argc) fpscap = atoi(argv[++i]);
    }
    if (!apply && !revert) {
        printf("RESULT=FAIL step=args status=0\nuso: flcd_amddrs --apply [--fpscap N] | --revert\n");
        return 2;
    }

    ADLX_RESULT res = ADLXHelper_Initialize();
    if (!ADLX_SUCCEEDED(res)) {
        report("Initialize", res);
        printf("RESULT=FAIL\n(ADLX no inicializo: no hay GPU/driver AMD?)\n");
        return 1;
    }

    IADLXSystem* sys = ADLXHelper_GetSystemServices();
    IADLXGPUList* gpus = NULL;
    IADLX3DSettingsServices* d3d = NULL;
    IADLXGPU* gpu = NULL;
    int failed = 0;

    if (report("GetGPUs", res = sys->pVtbl->GetGPUs(sys, &gpus)) != ADLX_OK) failed = 1;
    if (!failed && report("Get3DSettingsServices", res = sys->pVtbl->Get3DSettingsServices(sys, &d3d)) != ADLX_OK) failed = 1;
    if (!failed && report("At_GPUList", res = gpus->pVtbl->At_GPUList(gpus, 0, &gpu)) != ADLX_OK) failed = 1;

    if (!failed) {
        // --- Anti-Lag (low latency) ---
        IADLX3DAntiLag* al = NULL;
        if (report("GetAntiLag", res = d3d->pVtbl->GetAntiLag(d3d, gpu, &al)) == ADLX_OK && al) {
            report("set:antilag", al->pVtbl->SetEnabled(al, apply ? 1 : 0));
            al->pVtbl->Release(al);
        }

        // --- Frame Rate Target Control (driver fps cap) ---
        // apply only touches FRTC if a cap was asked; revert always clears it.
        if ((apply && fpscap > 0) || revert) {
            IADLX3DFrameRateTargetControl* frtc = NULL;
            if (report("GetFRTC", res = d3d->pVtbl->GetFrameRateTargetControl(d3d, gpu, &frtc)) == ADLX_OK && frtc) {
                if (apply) {
                    ADLX_IntRange range = { 0 };
                    frtc->pVtbl->GetFPSRange(frtc, &range);
                    int f = fpscap;
                    if (range.maxValue > 0) { if (f < range.minValue) f = range.minValue; if (f > range.maxValue) f = range.maxValue; }
                    report("set:fpscap", frtc->pVtbl->SetFPS(frtc, f));
                    report("set:frtc-on", frtc->pVtbl->SetEnabled(frtc, 1));
                } else {
                    report("set:frtc-off", frtc->pVtbl->SetEnabled(frtc, 0));
                }
                frtc->pVtbl->Release(frtc);
            }
        }
    }

    if (gpu) gpu->pVtbl->Release(gpu);
    if (d3d) d3d->pVtbl->Release(d3d);
    if (gpus) gpus->pVtbl->Release(gpus);
    ADLXHelper_Terminate();

    printf("RESULT=%s\n", failed ? "FAIL" : "OK");
    return failed ? 1 : 0;
}
