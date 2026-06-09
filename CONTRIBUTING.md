# Contributing

The easiest and most valuable contribution is a **game profile** — a small text file that
teaches FrameLCDoctor about a game. No coding required.

## Add a game profile

Create `profiles/<your-game>.toml`. FrameLCDoctor matches it to a running game by the
**exe name**. Minimal profile:

```toml
[game]
name   = "My Game"
exe    = "MyGame-Win64-Shipping.exe"   # the .exe that creates the D3D device
engine = "Unreal Engine 4"
```

### Optional sections

```toml
[graphics]
api                = "d3d11"   # d3d11 | dxgi (d3d12)
presents_per_frame = 1         # most games = 1; some present twice per frame

[timing]
fixed_timestep = true          # true if the engine speeds up when uncapped (NieR, etc.)
                               # -> the app then warns NOT to remove the fps cap
```

### A config preset (one-click fps settings)

Point at the game's settings file and list the values to apply. The app shows the user
the exact changes + the file path, backs it up, and lets them restore.

```toml
[config]
base   = "documents"   # documents | userprofile | appdata | localappdata | "" (absolute)
path   = "My Games/My Game/Config/*/settings.ini"   # '*' = one wildcard directory segment
format = "ini-flat"    # flat "key=value" lines (also works for UE4 GameUserSettings.ini)

[preset.fps]
# the keys/values to set for max fps. Exactly as they appear in the file.
shadowQuality = 0
sg.ShadowQuality = 0       # UE4 scalability keys work too (globally unique in the file)
```

Real examples: [`profiles/nier-replicant-122.toml`](profiles/nier-replicant-122.toml)
(flat ini, fixed-timestep) and [`profiles/vampyr.toml`](profiles/vampyr.toml)
(UE4 `GameUserSettings.ini`).

### Finding the values

- **exe**: the big binary that renders (for UE4, `<Project>/Binaries/Win64/<Project>-Win64-Shipping.exe`).
- **config path**: where the game stores graphics settings (often `Documents\My Games\…`
  or `%LOCALAPPDATA%\<Project>\Saved\Config\WindowsNoEditor\GameUserSettings.ini` for UE4).
- **preset values**: the lowest-quality values for the keys that matter to *fps* — for a
  CPU-bound game, the CPU-side ones (view distance, density, foliage, dynamic shadows).

### Rules

- **Single-player only.** Do not add games with online anti-cheat — the launcher refuses
  them anyway, and injecting can get accounts banned.
- Keep presets **reversible and honest** (the app always backs up and shows the diff).

## Code

C++ core builds with CMake (+ ImGui via `tools/fetch_deps.ps1`); C# companion with the
.NET 8 SDK. See [README.md](README.md#build). Keep the present hook safe — never break a
game's rendering; the overlay stays display-only and off by default.
