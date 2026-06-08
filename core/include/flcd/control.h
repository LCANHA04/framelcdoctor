// Runtime control surface: remedies the companion can drive over IPC. (P2)
#pragma once

namespace flcd::control {

void Init();
void Tick();                              // called once per present (paces the limiter)
void SetLimiter(int displayFps, int ppf); // 0 fps = uncapped
void GetLimiter(int* displayFps, int* ppf);

} // namespace flcd::control
