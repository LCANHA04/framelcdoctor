# FrameLCDoctor

A **diagnosis-driven** game performance/timing tool — a "deeper Special K".

Instead of a manual toolbox, FrameLCDoctor **profiles a running game, classifies the
root cause** of a performance or timing problem, and **applies or recommends the right
remedy** — per case. CPU-bound vs GPU-bound, software fps caps, fixed-timestep speedup,
vsync double-blocking, stutter: each gets detected and treated.

> Status: **early scaffold.** Design is in [DESIGN.md](DESIGN.md). Born from reverse
> engineering NieR Replicant ver.1.22 (see `profiles/nier-replicant-122.toml`).

## Why

The same symptom ("game runs at 30") can have completely different root causes that
need different fixes. Diagnosing that is the hard, valuable part — and what this tool
automates.

| Symptom | Root cause | Remedy |
|---|---|---|
| Hard 60 fps cap | engine software limiter | limiter override |
| Speedup above 60 | fixed 1/60 timestep | timestep correction |
| Drop to 30, GPU idle, CPU maxed | CPU-bound (AMD DX11 driver overhead) | DXVK advisor |
| Drop to 30 (other) | vsync double-block | sync override |

## Architecture

- **Core (C++)** — injected proxy DLL: hooks, profiler, classifier, remedies, ImGui overlay, IPC.
- **Companion (C#/.NET)** — UI, OS metrics (GPU%/CPU% via PDH), profile management, orchestration.
- **Profiles (TOML)** — per-game knowledge (engine, ppf, fixed-timestep, fixes). Community-contributable.

See [DESIGN.md](DESIGN.md) for the full design, roadmap (P0–P5) and rationale.

## Repo layout

```
core/        C++ injected DLL (CMake)
companion/   C#/.NET UI + orchestration
profiles/    per-game TOML profiles
tools/       reversing helpers (capstone/pefile)
third_party/ vcpkg-managed deps
```

## Building (work in progress)

Requirements (not all needed yet during P0):

- **Core:** CMake + a C++17 toolchain (MSVC 2022) + vcpkg (minhook, imgui, tomlplusplus).
- **Companion:** .NET 8 SDK (only the runtime may be installed — install the SDK to build).

```sh
# core
cmake -B build -S . && cmake --build build --config Release
# companion
dotnet build companion/FrameLCDoctor.sln -c Release
```

## License

MIT — see [LICENSE](LICENSE).

## Safety

Single-player only. Never targets games with online anti-cheat. Never modifies the game
executable (proxy DLL + injection; all changes reversible).
