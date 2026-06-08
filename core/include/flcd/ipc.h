// IPC between the injected core and the C#/.NET companion. Named pipe.
// Core streams FrameSignals (JSON lines) out; companion sends OS metrics + commands in.
#pragma once

namespace flcd::ipc {

constexpr wchar_t kPipeName[] = L"\\\\.\\pipe\\framelcdoctor";

// Starts the pipe server on a background thread. Streams profiler snapshots to the
// connected companion and applies inbound metrics/commands. Idempotent.
void StartServer();
void StopServer();

} // namespace flcd::ipc
