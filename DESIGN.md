# FrameLCDoctor — diseño

> **FrameLCDoctor.** Un "Special K más profundo": no una caja de herramientas que el
> usuario configura a mano, sino un **motor de diagnóstico + remedio** que detecta la
> causa raíz de un problema de rendimiento/timing y aplica (o recomienda) el fix
> correcto, por casos.
>
> **Decisiones cerradas (2026-06-08):** companion/UI en **C#/.NET**, core en **C++**;
> inyección por **un solo proxy DLL** (escalar después); **open-source desde el inicio**.

## 0. Origen

Nace de arreglar NieR Replicant ver.1.22 en dos máquinas. Cada síntoma parecido
("va a 30") tenía causa raíz DISTINTA y fix distinto:

| Síntoma | Causa raíz | Remedio |
|---|---|---|
| Cap duro a 60 | limitador software del engine | override del limiter |
| Speedup arriba de 60 | timestep fijo 1/60 (sim atada a fps) | corrección de dt |
| Caída a 30, GPU ociosa (10%), CPU 90% | CPU-bound: overhead driver DX11 en AMD | DXVK (D3D11→Vulkan) |
| Caída a 30 (otro origen) | vsync doble-bloqueo | sync override |

La tesis del producto: **el valor no está en las herramientas, está en saber CUÁL aplicar.**
Eso hoy lo hace un experto a mano (lo que hicimos toda la sesión). FrameDoctor lo automatiza.

## 1. Diferenciador vs Special K / RTSS

- **Special K / RTSS:** potentísimos pero *expert-driven*. Vos tenés que saber qué tocar.
- **FrameDoctor:** *diagnosis-driven*. Mide, clasifica el problema, y guía/aplica el remedio.
  - Auto-detección CPU-bound vs GPU-bound vs cap vs stutter vs timestep.
  - **Asesor DXVK** (ningún tool mainstream te dice "che, esto es overhead DX11 de AMD, usá DXVK" y te lo instala).
  - **Librería de casos por juego** (perfiles) contribuible por la comunidad.
  - Corrección de timestep por perfil (lo que SK no generaliza).

## 1b. Headroom Index — "¿le saco más fps?"

Además de diagnosticar *errores*, FrameLCDoctor responde una pregunta que el usuario
siempre quiere: **¿estoy exprimiendo el hardware al máximo, o hay fps libres por agarrar?**

Mecánica: detectar el **cuello de botella** (el poste más largo) y medir cuánto queda
**ocioso en el recurso que NO es el cuello** — ese ocioso = potencial de fps no aprovechado.

| GPU% | CPU (core pico) | Veredicto | ¿Más fps? |
|---|---|---|---|
| ~99% | bajo | GPU-bound, exprimida al máximo | solo bajando settings/resolución |
| ~10-40% | un core ~100% | CPU-bound (single-thread), GPU ociosa | **sí** — GPU sin usar (DXVK / menos draw-calls) |
| moderados + fps capeado | — | cap-limited | **sí, fácil** — sacar el cap |
| ambos altos y parejos | — | balanceado, cerca del máximo | poco margen |

Salida (`HeadroomReport`, `core/include/flcd/headroom.h`):
- `bottleneck` (GPU / CPU-single / CPU-multi / cap / balanced)
- `utilizationIndex` (cuán exprimido está el recurso limitante, ~100 = al palo)
- `headroomIndex` (capacidad ociosa en el no-cuello = la oportunidad)
- `moreFpsLikely` + `verdict` + `suggestion` (paso concreto atado a un remedio/setting)

Es a la vez **diferenciador** (ningún tool mainstream da un "índice de aprovechamiento"
con veredicto accionable) y la base natural para sugerir el remedio correcto.

## 2. Arquitectura

```
┌─────────────────────────────────────────────────────────────┐
│ COMPANION APP (proceso aparte, UI)                           │
│  - lanza/inyecta, gestiona perfiles, aplica fixes de archivo │
│  - lee GPU%/CPU% del SO (PDH/perf counters)                  │
│  - muestra diagnóstico + decisiones                          │
└───────────────┬─────────────────────────────────────────────┘
                │ named pipe / shared memory
┌───────────────▼─────────────────────────────────────────────┐
│ INJECTED CORE (DLL dentro del juego)                         │
│                                                              │
│  [Inyector]   proxy DLL (d3d11/dxgi/winmm) — base actual     │
│  [Hook layer] present + timing (QPC/Sleep/timeBeginPeriod)   │
│               abstrae API gráfica (D3D11 hoy; D3D12/Vk/9 →)  │
│  [Profiler]   por-frame: frametime, fps, ppf, varianza;      │
│               GPU-time (timestamp queries) vs CPU-time       │
│  [Clasificador] reglas sobre señales → clase de problema     │
│  [Remedios]   limiter / sync / timestep / pacing / advisor   │
│  [Overlay]    ImGui sobre el present hook (diag + toggles)   │
└──────────────────────────────────────────────────────────────┘
                │ usa
┌───────────────▼─────────────────────────────────────────────┐
│ PERFILES DE JUEGO (TOML)  — librería de casos                │
│  engine, fixed-timestep?, ppf, parches dt, remedios recomend.│
└──────────────────────────────────────────────────────────────┘
```

### Componentes

1. **Inyector.** Empezamos con **proxy DLL** (probado, sin launcher, sin flags de AV).
   El companion elige qué nombre usar por juego (d3d11/dxgi/winmm) según imports.
   Más adelante: inyector global opcional (estilo SK) para juegos que cargan la API dinámico.

2. **Hook layer.** Abstracción sobre la API gráfica + timing. Hoy: D3D11/DXGI present
   (factory→swapchain→Present, lo que ya tenemos). Inline hooks con **MinHook/Detours**
   donde vtable no alcance. Roadmap: D3D12, Vulkan, OpenGL, D3D9.

3. **Profiler.** Señales por frame:
   - frametime real, fps de display, **ppf** (presents por frame), varianza/stutter.
   - **GPU-bound vs CPU-bound**: la señal clave. Dos fuentes:
     a) GPU% del SO vía PDH "GPU Engine" counters (companion, sin tocar render).
     b) GPU-time por frame vía D3D timestamp/disjoint queries (más preciso, requiere
        device — se obtiene wrappeando el device del juego).
     CPU: ocupación del hilo de render, saturación de un solo core (firma DX11-AMD).

4. **Clasificador.** Motor de reglas sobre las señales → diagnóstico con confianza.
   Ej: `GPU% < 40 && cpuMainThread > 85 && api==D3D11 && vendor==AMD → CPU-bound por driver`.

5. **Remedios.** Cada remedio = predicado-detector + acción + revert + log. Ver §3.

6. **Overlay.** ImGui dibujado en el present hook: diagnóstico en vivo + toggles + gráfico
   de frametime. Como el overlay de SK pero centrado en "qué te pasa y qué hago".

7. **Perfiles de juego.** TOML por juego con lo aprendido (engine, ppf, fixed-timestep,
   offsets/patrones de dt, remedios recomendados). **El NieR ya es el primer perfil.**
   Contribuible por la comunidad → así escala a "muchos casos".

## 3. Librería de remedios (casos)

| Remedio | Detector (señal) | Acción | Reversible |
|---|---|---|---|
| **FrameLimiter** | fps inestable / quiere cap | limitador QPC en present (ya hecho) | sí (config) |
| **SyncOverride** | vsync doble-bloqueo / tearing | forzar SyncInterval / flag tearing | sí |
| **DXVK Advisor** | CPU-bound + DX11 + AMD + GPU ociosa | bajar DXVK, copiar d3d11/dxgi, dxvk.conf | sí (borrar dlls) |
| **TimestepFix** | speedup ∝ fps (sim atada a frames) | corregir dt por frametime real (por perfil) | sí |
| **FramePacing** | varianza alta / microstutter | pacing + latencia (estilo SK Latent Sync) | sí |
| **CapOverride** | limitador interno del engine | localizar y neutralizar el limiter | sí |

Cada uno: opt-in o auto según preferencia del usuario. **Nunca** se toca el .exe (firma intacta);
todo por archivos agregados/inyección, con backup y revert.

## 4. Stack técnico

- **Core DLL:** C++17/20. MinHook o Microsoft Detours. ImGui (overlay). D3D11 primero.
- **Companion app:** a definir — C++/Qt o C#/.NET (UI más rápida de iterar). IPC named pipe.
- **Build:** CMake + vcpkg (imgui, minhook). Hoy compilamos con MSVC 14.44 / Win SDK 26100.
- **Reversing/estático:** capstone + pefile (ya instalados) para generar perfiles.
- **Perfiles:** TOML.
- **Telemetría SO:** PDH (GPU Engine / Processor counters).

## 5. Roadmap por fases

- **P0 — Fundación.** Proxy inyector genérico + present/timing hooks + overlay ImGui +
  telemetría básica (frametime/fps/ppf). Reusa ~todo lo de la sesión NieR.
- **P1 — Diagnóstico.** GPU%/CPU% + clasificador: CPU-bound vs GPU-bound vs cap vs stutter.
  Reporte en overlay. (MVP que ya da valor: "tu problema es X".)
- **P2 — Remedios core.** FrameLimiter + SyncOverride (auto + manual, reversibles).
- **P3 — Perfiles + DXVK Advisor.** Sistema de perfiles TOML + el caso AMD de punta a punta
  (detectar → recomendar/instalar DXVK). Primer perfil real: NieR Replicant.
- **P4 — TimestepFix framework.** Generalizar la corrección de dt vía perfil (Opción B del NieR
  como primer caso). Incluye el toolkit de RE (HWBP, scanner) para crear perfiles.
- **P5 — Cobertura.** D3D12/Vulkan/D3D9/OpenGL, pulido, compartir perfiles, firma de código.

## 6. Riesgos y límites

- **Anti-cheat:** SOLO single-player. Nunca inyectar en juegos con AC online. Lista negra.
- **Falsos positivos de AV:** la inyección flaggea. Mitigar con firma de código (P5) y open-source.
- **Amplitud de APIs:** enorme. Mitigación: D3D11 primero, todo lo demás *profile-driven* e incremental.
- **TimestepFix no escala solo:** cada juego es RE propio → depende de perfiles de comunidad,
  no de magia automática. Honestidad: el tool *facilita* crear el fix, no lo adivina.
- **Scope creep:** es el riesgo #1. Disciplina de fases; cada fase entrega algo usable.

## 7. Qué ya tenemos (no arrancamos de cero)

- Inyección segura por proxy DLL (forwarders auto-generados, sin romper render).
- Hook factory→swapchain→Present (sin dummy device).
- Limitador QPC con detección de ppf.
- Toolkit de reversing: `analyze.py` / `disasm.py` / `xref.py` (capstone+pefile), HWBP+VEH.
- Un caso real entendido punta a punta (NieR) = primer perfil + validación del enfoque.

## 8. Decisiones — cerradas

- **Nombre:** FrameLCDoctor.
- **Companion/UI:** C#/.NET. **Core:** C++.
- **Inyección:** un solo proxy DLL en P0; global injector más adelante.
- **Open-source desde el inicio** (licencia a definir: MIT o GPLv3; GPLv3 si se reusa
  código de DXVK/otros; si no, MIT).

### Pendiente menor
- IPC core↔companion: named pipe (default) vs shared memory para telemetría de alta frecuencia.
- Host de perfiles de comunidad (repo git público, carga vía PR).

## 9. Estructura del repo (open-source)

```
framelcdoctor/
├─ LICENSE                (MIT o GPLv3)
├─ README.md
├─ DESIGN.md              (este doc)
├─ CMakeLists.txt         (raíz; build del core C++)
├─ core/                  (C++ — la DLL inyectada)
│  ├─ inject/             proxy DLL + forwarders auto-generados
│  ├─ hooks/              factory/present, timing (MinHook)
│  ├─ profiler/           frametime, ppf, gpu/cpu-time
│  ├─ classify/           motor de reglas
│  ├─ remedies/           limiter, sync, dxvk-advisor, timestep, pacing
│  ├─ overlay/            ImGui
│  └─ ipc/                named pipe server
├─ companion/             (C#/.NET — UI + orquestación)
│  ├─ FrameLCDoctor.sln
│  ├─ ui/                 WPF o Avalonia (multiplataforma futura)
│  ├─ ipc/                cliente named pipe
│  ├─ sysmetrics/         PDH GPU%/CPU%
│  └─ profiles/           carga/gestión de perfiles TOML
├─ profiles/              perfiles por juego (TOML) — NieR el primero
├─ tools/                 reversing: analyze.py, disasm.py, xref.py
└─ third_party/           vcpkg manifest (imgui, minhook, tomlplusplus)
```

- **UI .NET:** evaluar **Avalonia** sobre WPF (Avalonia es cross-platform y open-source,
  alinea con el espíritu del proyecto; WPF es solo-Windows pero más maduro). Decidir en P0.
- **Core↔companion:** named pipe con un protocolo chico JSON/binario; el companion no
  inyecta lógica, orquesta y muestra.

## 10. Estado de implementación (actualizado)

**Hecho y validado** (NieR + Vampyr):
- Inyección por proxy DLL (d3d11/dxgi), hook de present sin dummy device, IPC named pipe.
- Profiler: frametime, fps, **auto-ppf** (detección de frame por gap entre presents),
  **1% / 0.1% low**, serie de frametime.
- Headroom Index + clasificador de cuello (GPU / CPU-single / CPU-multi / cap / balanced).
- Remedios: **frame limiter** en caliente, **DXVK advisor** (vendor+cuello, plain-language).
- **"Cómo ganar fps"** (settings por cuello, consciente de fixed-timestep).
- **Optimizador del sistema**: plan de energía, prioridad del juego, apps de fondo por CPU.
- **Presets por juego** (config del juego, con backup + transparencia total).
- **Perfiles TOML** por exe (NieR, Vampyr).
- **Launcher** con scan de Steam + browse, detección de API, **guard anti-cheat**, instalar/lanzar.
- **Overlay in-game ImGui** (display-only, toggle Insert).
- GUI WPF (Segoe UI Variable), companion C#/.NET 8.

**Decisiones que cambiaron vs el diseño original:**
- UI = **WPF** (no Avalonia): viene con el SDK, sin NuGet, y el tool es Windows-only.
- ppf no se hardcodea: se **auto-detecta** por gap entre presents.

**TODO (diferido, necesita testear lanzando juegos o requiere insumos externos):**
- DXVK auto-install de un click (convivencia con el proxy d3d11 → mover el vehículo a winmm).
- Remedio **sync-override** (forzar/quitar vsync; ojo flip-model sin allow-tearing).
- Soporte **D3D9 / OpenGL / Vulkan** (hoy D3D11 + D3D12/DXGI). D3D9 = proxy d3d9 + hook
  EndScene/Present + imgui_impl_dx9. Vulkan = layer + vkQueuePresentKHR + imgui_impl_vulkan.
- **Firma de código** (requiere certificado).
