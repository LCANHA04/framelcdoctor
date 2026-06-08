// QPC frame limiter (ported from the NieR limiter). Paces the present-call rate to
// target_display_fps * presents_per_frame, so the displayed rate hits the target even
// when the engine issues multiple Present calls per frame.
#pragma once
#include <windows.h>

namespace flcd {

class FrameLimiter {
public:
    void Init();                                  // call once (queries QPC freq, raises timer res)
    void SetTarget(int displayFps, int ppf);      // 0 fps = uncapped
    void Tick();                                  // call once per present

private:
    LARGE_INTEGER freq_{};
    LONGLONG next_ = 0;
    int      displayFps_ = 0;
    int      ppf_ = 1;
};

} // namespace flcd
