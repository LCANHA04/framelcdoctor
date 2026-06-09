// flcd_nvdrs.exe - NVIDIA driver-profile optimizer (tasklist item #2, NVIDIA path).
//
// Creates a PER-GAME NVIDIA driver profile and forces performance settings the game itself
// doesn't expose, then can delete the profile to revert. Talks to nvapi64.dll directly via
// nvapi_QueryInterface (no SDK / .lib needed - the DLL ships with every NVIDIA driver).
// Minimal NVDRS structs are declared here matching NVIDIA's layout; in C++ the struct
// version stamp = sizeof | (ver<<16) is computed by the compiler, so it can't drift.
//
//   flcd_nvdrs.exe --apply  --app <exe> [--maxperf] [--ogl-threaded] [--lowlatency] [--fpscap N]
//   flcd_nvdrs.exe --revert --app <exe>
//
// Prints "step=<name> status=<int>" per call and "RESULT=OK|FAIL". Exit 0 on success.

#include <windows.h>
#include <cstdio>
#include <cstring>
#include <string>

typedef unsigned int   NvU32;
typedef unsigned short NvU16;
typedef unsigned char  NvU8;
typedef int            NvAPI_Status;          // NVAPI_OK = 0
typedef void* NvDRSSessionHandle;
typedef void* NvDRSProfileHandle;

#define NVAPI_OK 0
#define NVAPI_UNICODE_STRING_MAX 2048
#define NVAPI_BINARY_DATA_MAX    4096
typedef NvU16 NvAPI_UnicodeString[NVAPI_UNICODE_STRING_MAX];

enum NVDRS_SETTING_TYPE { NVDRS_DWORD_TYPE, NVDRS_BINARY_TYPE, NVDRS_STRING_TYPE, NVDRS_WSTRING_TYPE };
enum NVDRS_SETTING_LOCATION { NVDRS_CURRENT_PROFILE_LOCATION, NVDRS_GLOBAL_PROFILE_LOCATION,
                              NVDRS_BASE_PROFILE_LOCATION, NVDRS_DEFAULT_PROFILE_LOCATION };

typedef struct { NvU32 valueLength; NvU8 valueData[NVAPI_BINARY_DATA_MAX]; } NVDRS_BINARY_SETTING;

typedef struct {
    NvU32 version;
    NvAPI_UnicodeString settingName;
    NvU32 settingId;
    NVDRS_SETTING_TYPE settingType;
    NVDRS_SETTING_LOCATION settingLocation;
    NvU32 isCurrentPredefined;
    NvU32 isPredefinedValid;
    union { NvU32 u32PredefinedValue; NVDRS_BINARY_SETTING binaryPredefinedValue; NvAPI_UnicodeString wszPredefinedValue; };
    union { NvU32 u32CurrentValue;    NVDRS_BINARY_SETTING binaryCurrentValue;    NvAPI_UnicodeString wszCurrentValue; };
} NVDRS_SETTING;

typedef struct {
    NvU32 version;
    NvAPI_UnicodeString profileName;
    NvU32 gpuSupport;          // NVDRS_GPU_SUPPORT bitfield struct (4 bytes); bit0=geforce
    NvU32 isPredefined;
    NvU32 numOfApps;
    NvU32 numOfSettings;
} NVDRS_PROFILE;

typedef struct {
    NvU32 version;
    NvU32 isPredefined;
    NvAPI_UnicodeString appName;
    NvAPI_UnicodeString userFriendlyName;
    NvAPI_UnicodeString launcher;
} NVDRS_APPLICATION;

#define MAKE_NVAPI_VERSION(t, ver) (NvU32)(sizeof(t) | ((ver) << 16))
#define NVDRS_SETTING_VER     MAKE_NVAPI_VERSION(NVDRS_SETTING, 1)
#define NVDRS_PROFILE_VER     MAKE_NVAPI_VERSION(NVDRS_PROFILE, 1)
#define NVDRS_APPLICATION_VER MAKE_NVAPI_VERSION(NVDRS_APPLICATION, 1)

// --- setting ids + values (from NvApiDriverSettings.h) ---
#define PREFERRED_PSTATE_ID         0x1057EB71
#define PREFERRED_PSTATE_PREFER_MAX 0x00000001
#define OGL_THREAD_CONTROL_ID       0x20C1221E
#define OGL_THREAD_CONTROL_ENABLE   0x00000001
#define PRERENDERLIMIT_ID           0x007BA09E
#define FRL_FPS_ID                  0x10835002

// --- nvapi function ids (resolved via nvapi_QueryInterface) ---
typedef void* (*QueryInterface_t)(NvU32);
typedef NvAPI_Status(*Initialize_t)();
typedef NvAPI_Status(*DRS_CreateSession_t)(NvDRSSessionHandle*);
typedef NvAPI_Status(*DRS_DestroySession_t)(NvDRSSessionHandle);
typedef NvAPI_Status(*DRS_LoadSettings_t)(NvDRSSessionHandle);
typedef NvAPI_Status(*DRS_SaveSettings_t)(NvDRSSessionHandle);
typedef NvAPI_Status(*DRS_CreateProfile_t)(NvDRSSessionHandle, NVDRS_PROFILE*, NvDRSProfileHandle*);
typedef NvAPI_Status(*DRS_DeleteProfile_t)(NvDRSSessionHandle, NvDRSProfileHandle);
typedef NvAPI_Status(*DRS_FindProfileByName_t)(NvDRSSessionHandle, NvU16*, NvDRSProfileHandle*);
typedef NvAPI_Status(*DRS_CreateApplication_t)(NvDRSSessionHandle, NvDRSProfileHandle, NVDRS_APPLICATION*);
typedef NvAPI_Status(*DRS_SetSetting_t)(NvDRSSessionHandle, NvDRSProfileHandle, NVDRS_SETTING*);

static QueryInterface_t QI = nullptr;
static Initialize_t           pInit;
static DRS_CreateSession_t    pCreateSession;
static DRS_DestroySession_t   pDestroySession;
static DRS_LoadSettings_t     pLoadSettings;
static DRS_SaveSettings_t     pSaveSettings;
static DRS_CreateProfile_t    pCreateProfile;
static DRS_DeleteProfile_t    pDeleteProfile;
static DRS_FindProfileByName_t pFindProfileByName;
static DRS_CreateApplication_t pCreateApplication;
static DRS_SetSetting_t       pSetSetting;

template <class T> static T resolve(NvU32 id) { return reinterpret_cast<T>(QI(id)); }

static void toU16(NvU16* dst, const wchar_t* s)   // wchar_t is 16-bit on Windows
{
    size_t i = 0; for (; s[i] && i < NVAPI_UNICODE_STRING_MAX - 1; ++i) dst[i] = (NvU16)s[i];
    dst[i] = 0;
}

static int report(const char* step, NvAPI_Status st)
{
    printf("step=%s status=%d\n", step, st);
    return st;
}

static void setDword(NvDRSSessionHandle ses, NvDRSProfileHandle prof, NvU32 id, NvU32 val, const char* label)
{
    NVDRS_SETTING s = {};
    s.version = NVDRS_SETTING_VER;
    s.settingId = id;
    s.settingType = NVDRS_DWORD_TYPE;
    s.u32CurrentValue = val;
    report(label, pSetSetting(ses, prof, &s));
}

int main(int argc, char** argv)
{
    bool apply = false, revert = false, maxperf = false, ogl = false, lowlat = false;
    int fpscap = 0; std::wstring app;
    for (int i = 1; i < argc; ++i) {
        std::string a = argv[i];
        if (a == "--apply") apply = true;
        else if (a == "--revert") revert = true;
        else if (a == "--maxperf") maxperf = true;
        else if (a == "--ogl-threaded") ogl = true;
        else if (a == "--lowlatency") lowlat = true;
        else if (a == "--fpscap" && i + 1 < argc) fpscap = atoi(argv[++i]);
        else if (a == "--app" && i + 1 < argc) {
            std::string s = argv[++i]; app.assign(s.begin(), s.end());   // ascii exe name
        }
    }
    if ((!apply && !revert) || app.empty()) {
        printf("RESULT=FAIL step=args status=0\n");
        printf("uso: flcd_nvdrs --apply|--revert --app <exe> [--maxperf --ogl-threaded --lowlatency --fpscap N]\n");
        return 2;
    }

    HMODULE nv = LoadLibraryW(L"nvapi64.dll");
    if (!nv) { printf("RESULT=FAIL step=LoadLibrary status=0\n(nvapi64.dll no encontrada: no hay driver NVIDIA?)\n"); return 1; }
    QI = (QueryInterface_t)GetProcAddress(nv, "nvapi_QueryInterface");
    if (!QI) { printf("RESULT=FAIL step=QueryInterface status=0\n"); return 1; }

    pInit              = resolve<Initialize_t>(0x0150E828);
    pCreateSession     = resolve<DRS_CreateSession_t>(0x0694D52E);
    pDestroySession    = resolve<DRS_DestroySession_t>(0xDAD9CFF8);
    pLoadSettings      = resolve<DRS_LoadSettings_t>(0x375DBD6B);
    pSaveSettings      = resolve<DRS_SaveSettings_t>(0xFCBC7E14);
    pCreateProfile     = resolve<DRS_CreateProfile_t>(0xCC176068);
    pDeleteProfile     = resolve<DRS_DeleteProfile_t>(0x17093206);
    pFindProfileByName = resolve<DRS_FindProfileByName_t>(0x7E4A9A0B);
    pCreateApplication = resolve<DRS_CreateApplication_t>(0x4347A9DE);
    pSetSetting        = resolve<DRS_SetSetting_t>(0x577DD202);
    if (!pInit || !pCreateSession || !pLoadSettings || !pSaveSettings || !pCreateProfile
        || !pDeleteProfile || !pFindProfileByName || !pCreateApplication || !pSetSetting) {
        printf("RESULT=FAIL step=resolve status=0\n"); return 1;
    }

    std::wstring profName = L"FrameLCDoctor: " + app;
    NvU16 profU16[NVAPI_UNICODE_STRING_MAX]; toU16(profU16, profName.c_str());

    if (report("Initialize", pInit()) != NVAPI_OK) { printf("RESULT=FAIL\n"); return 1; }
    NvDRSSessionHandle ses = nullptr;
    if (report("CreateSession", pCreateSession(&ses)) != NVAPI_OK) { printf("RESULT=FAIL\n"); return 1; }
    if (report("LoadSettings", pLoadSettings(ses)) != NVAPI_OK) { pDestroySession(ses); printf("RESULT=FAIL\n"); return 1; }

    // start clean: drop any existing FrameLCDoctor profile for this exe (makes apply idempotent
    // and is exactly what revert does).
    NvDRSProfileHandle existing = nullptr;
    if (pFindProfileByName(ses, profU16, &existing) == NVAPI_OK && existing)
        report("DeleteProfile(old)", pDeleteProfile(ses, existing));

    if (revert) {
        NvAPI_Status sv = pSaveSettings(ses);
        report("SaveSettings", sv);
        pDestroySession(ses);
        printf("RESULT=%s\n", sv == NVAPI_OK ? "OK" : "FAIL");
        return sv == NVAPI_OK ? 0 : 1;
    }

    // apply: create the profile + attach the app + push settings
    NVDRS_PROFILE p = {};
    p.version = NVDRS_PROFILE_VER;
    p.gpuSupport = 0x1;                 // geforce
    toU16(p.profileName, profName.c_str());
    NvDRSProfileHandle prof = nullptr;
    if (report("CreateProfile", pCreateProfile(ses, &p, &prof)) != NVAPI_OK) { pDestroySession(ses); printf("RESULT=FAIL\n"); return 1; }

    NVDRS_APPLICATION ap = {};
    ap.version = NVDRS_APPLICATION_VER;
    toU16(ap.appName, app.c_str());
    toU16(ap.userFriendlyName, app.c_str());
    report("CreateApplication", pCreateApplication(ses, prof, &ap));

    if (maxperf) setDword(ses, prof, PREFERRED_PSTATE_ID, PREFERRED_PSTATE_PREFER_MAX, "set:maxperf");
    if (ogl)     setDword(ses, prof, OGL_THREAD_CONTROL_ID, OGL_THREAD_CONTROL_ENABLE, "set:ogl-threaded");
    if (lowlat)  setDword(ses, prof, PRERENDERLIMIT_ID, 1, "set:lowlatency");           // 1 pre-rendered frame
    if (fpscap > 0) setDword(ses, prof, FRL_FPS_ID, (NvU32)fpscap, "set:fpscap");

    NvAPI_Status sv = pSaveSettings(ses);
    report("SaveSettings", sv);
    pDestroySession(ses);
    printf("RESULT=%s\n", sv == NVAPI_OK ? "OK" : "FAIL");
    return sv == NVAPI_OK ? 0 : 1;
}
