# FrameLCDoctor

[![build](https://github.com/LCANHA04/framelcdoctor/actions/workflows/build.yml/badge.svg)](https://github.com/LCANHA04/framelcdoctor/actions/workflows/build.yml)
[![license: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

**A diagnosis-driven game performance tool — a "deeper Special K", focused on actually
gaining FPS, especially on low-end PCs.**

Most fps tools are a manual toolbox: *you* have to know what to tweak. FrameLCDoctor
flips that — it **profiles a running game, finds the root cause** of low/uneven fps, and
then **tells you (and applies) the right fix**. Same symptom ("game runs at 30") can have
completely different causes; diagnosing that is the hard, valuable part, and it's what
this automates.

> Status: working MVP, validated on two engines (NieR Replicant — Cavia/Toylogic, and
> Vampyr — Unreal Engine 4). Single-player only.

---

## What it does

**Diagnose**
- **Bottleneck**: GPU-bound vs CPU-bound (single/multi-thread) vs frame-cap vs balanced.
- **Headroom Index**: are you maxing the hardware, and is there *untapped fps* to get?
- **Smoothness**: average fps, **1% / 0.1% low**, and a live **frametime graph** (spikes = stutter).
- **Per-game profile**: detects the game (by exe) and shows its engine + quirks
  (e.g. a fixed-timestep engine that speeds up if uncapped).

**Gain fps**
- **"How to gain fps"**: concrete in-game settings to lower for *your* bottleneck
  (CPU → draw distance / density / dynamic shadows; GPU → resolution / AA / upscaling).
- **System optimizer** (great for low-end): one-click **High Performance** power plan,
  **High priority** for the game process, and a list of **background apps using CPU**
  (so you close the real fps thieves — idle RAM users are ignored).
- **Per-game presets (auto)**: applies an optimal low/fps preset to the game's own config
  file — for **Unreal Engine 4/5 games it auto-generates the preset** (finds
  `GameUserSettings.ini`, lowers the `sg.*` scalability groups), no profile needed; other
  games use a hand-authored profile. Always shows **exactly what changes and which file**,
  with a backup. Reversible.
- **DXVK advisor**: vendor + bottleneck aware — tells you honestly whether DXVK would
  help (big on AMD CPU-bound, rarely on NVIDIA) instead of recommending it blindly.
- **Frame limiter**: a clean QPC cap you can change live (60 = correct speed on
  fixed-timestep engines).

**Beyond the game** (what you *can't* toggle from in-game — the real differentiator)
- **CPU affinity**: pins the game to the performance cores (Intel P-cores / a single Ryzen
  CCD), off the cores running background junk. Helps CPU-bound games. Reversible.
- **Upscaling** (external, Magpie/Lossless-Scaling style): run the game windowed at a low
  resolution and `flcd_upscaler.exe` captures + upscales it to fullscreen — the GPU draws
  fewer pixels = more fps. Recommended **only when GPU-bound** (the diagnosis decides).
- **Frame generation**: motion-compensated interpolation (block-matching optical flow in a
  compute shader) inserts a frame between real ones for smoother motion. Recommended **only
  when the GPU has headroom** (the inverse of upscaling). Toggle with PAGE UP.
- **Driver optimizer**: a per-game NVIDIA profile (NvAPI: prefer-max-performance, low
  latency, OpenGL threaded optimization, driver fps cap) or AMD global settings (ADLX:
  Anti-Lag, frame-rate target). Forces what the game doesn't expose. Reversible.

**In-game overlay**
- Toggle an ImGui HUD (Insert) showing fps / frametime / lows / GPU-CPU / bottleneck,
  rendered into the game's swap chain. Hidden by default.

**Launcher**
- Pick a Steam game (or browse to an exe), see its graphics API + **anti-cheat status**,
  install/uninstall, and launch. **Anti-cheat games are hard-blocked** (ban protection).
- **Minecraft**: auto-detected when running (it isn't on Steam). Java Edition is OpenGL —
  the launcher injects the core (no file drop) and hooks `gdi32!SwapBuffers`, so you get
  live fps/bottleneck in MC too. The external tools (driver optimizer, frame-gen) also apply.

---

## Architecture

- **Core** (C++ injected DLL): hooks the present (D3D11/DXGI swap chain, or `gdi32!SwapBuffers`
  for OpenGL via MinHook), profiles frames, runs the headroom classifier, paces the limiter,
  and draws the overlay. Streams data to the companion over a named pipe. Loaded as a
  beside-the-exe proxy (D3D) or by `flcd_inject.exe` (CreateRemoteThread, for OpenGL / no
  file drop). Standalone helper exes: `flcd_upscaler` (upscale + frame-gen), `flcd_nvdrs` /
  `flcd_amddrs` (driver optimizer), `flcd_inject` (injector).
- **Companion** (C#/.NET WPF): the GUI, OS metrics (PDH GPU%/CPU%), launcher, profiles,
  and the system optimizer / presets.
- **Profiles** (TOML): per-game knowledge, community-contributable.

See [DESIGN.md](DESIGN.md) for the full design and rationale.

## Safety

- **Single-player only.** Never targets games with online anti-cheat (EAC/BattlEye/VAC…) —
  the launcher detects and refuses them. Injecting there can get your account banned.
- **Never modifies the game executable** (proxy DLL beside it; the original is preserved).
- Every change (proxy, config preset, power plan, priority) is **reversible**.

## Build

Requirements: Windows x64, **Visual Studio 2022** (C++), **CMake**, **.NET 8 SDK**.

```powershell
# 1. native deps (ImGui)
./tools/fetch_deps.ps1

# 2. core DLL
cmake -B build -S . -A x64
cmake --build build --config Release

# 3. prebuilt proxies the launcher deploys
./tools/make_proxies.ps1

# 4. companion (GUI)
dotnet build companion/FrameLCDoctor.sln -c Release
```

Run `companion/.../bin/x64/Release/net8.0-windows/FrameLCDoctor.exe`.

## Use

1. Open the GUI → **Juegos…** → pick a single-player game → **Instalar**.
2. Launch the game (the proxy loads automatically) and open the panel.
3. Read the diagnosis, apply remedies / presets / system optimizations.
4. Toggle the in-game overlay with **Insert**.

## Game profiles

A profile is a small TOML in `profiles/` matched by the game's exe name. It can carry the
engine, the presents-per-frame, a fixed-timestep flag, and a config preset. See
[CONTRIBUTING.md](CONTRIBUTING.md) to add your game — it's just a text file.

## Roadmap

- Validate on real hardware: frame-gen (visual quality), AMD driver optimizer (RX 6600 + ADLX
  SDK), OpenGL diagnosis (Minecraft Java injection)
- FSR upscaling shader (sharper than the current bilinear), per-app AMD profiles
- DXVK one-click auto-install (needs co-existence with our proxy → winmm vehicle)
- VSync / SyncInterval override remedy
- D3D9 / Vulkan support (currently D3D11 + D3D12/DXGI + OpenGL)
- More game profiles & presets (community)
- Code signing (reduce AV false positives)

## License

MIT — see [LICENSE](LICENSE). Born from reverse-engineering NieR Replicant's 60fps lock.
