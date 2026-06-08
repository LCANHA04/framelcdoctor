// In-game ImGui overlay (display-only). Rendered into the game's swap chain from the
// Present hook, just before the real present. Toggled by a hotkey. No input capture
// (read-only HUD) to keep it safe and simple.
#pragma once
struct IDXGISwapChain;

namespace flcd::overlay {

void Init();                          // read config (enabled, hotkey)
void OnPresent(IDXGISwapChain* sc);   // render the HUD into sc's back buffer
void Shutdown();

} // namespace flcd::overlay
