// QPC frame limiter implementation (ported from the validated NieR limiter).
#include "flcd/frame_limiter.h"

namespace flcd {

void FrameLimiter::Init()
{
    QueryPerformanceFrequency(&freq_);
    timeBeginPeriod(1);   // tighten Sleep granularity
}

void FrameLimiter::SetTarget(int displayFps, int ppf)
{
    displayFps_ = displayFps;
    ppf_ = ppf < 1 ? 1 : ppf;
    next_ = 0;            // resync
}

void FrameLimiter::Tick()
{
    const int limit = displayFps_ * ppf_;   // pace present-call rate
    if (limit <= 0) return;                  // uncapped

    LARGE_INTEGER now; QueryPerformanceCounter(&now);
    const LONGLONG ft = freq_.QuadPart / limit;
    if (next_ == 0) { next_ = now.QuadPart + ft; return; }

    if (now.QuadPart < next_) {
        double ms = (double)(next_ - now.QuadPart) * 1000.0 / freq_.QuadPart;
        if (ms > 1.5) Sleep((DWORD)(ms - 1.0));           // coarse sleep
        do { QueryPerformanceCounter(&now); } while (now.QuadPart < next_); // spin tail
    }
    next_ += ft;
    if (next_ < now.QuadPart) next_ = now.QuadPart + ft;  // resync if we fell behind
}

} // namespace flcd
