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
- **Per-game presets**: applies an optimal low/fps preset to the game's own config file,
  showing **exactly what it changes and which file** it touches, with a backup. Reversible.
- **DXVK advisor**: vendor + bottleneck aware — tells you honestly whether DXVK would
  help (big on AMD CPU-bound, rarely on NVIDIA) instead of recommending it blindly.
- **Frame limiter**: a clean QPC cap you can change live (60 = correct speed on
  fixed-timestep engines).

**In-game overlay**
- Toggle an ImGui HUD (Insert) showing fps / frametime / lows / GPU-CPU / bottleneck,
  rendered into the game's swap chain. Hidden by default.

**Launcher**
- Pick a Steam game (or browse to an exe), see its graphics API + **anti-cheat status**,
  install/uninstall, and launch. **Anti-cheat games are hard-blocked** (ban protection).

---

## Architecture

- **Core** (C++ injected DLL): a proxy DLL (d3d11/dxgi) that hooks the swap chain Present,
  profiles frames, runs the headroom classifier, paces the limiter, and draws the overlay.
  Streams data to the companion over a named pipe.
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

- DXVK one-click auto-install (needs co-existence with our proxy → winmm vehicle)
- VSync / SyncInterval override remedy
- D3D9 / OpenGL / Vulkan support (currently D3D11 + D3D12/DXGI)
- More game profiles & presets (community)
- Code signing (reduce AV false positives)

## License

MIT — see [LICENSE](LICENSE). Born from reverse-engineering NieR Replicant's 60fps lock.
