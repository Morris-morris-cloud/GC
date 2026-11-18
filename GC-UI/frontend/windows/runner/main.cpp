#include <flutter/dart_project.h>
#include <flutter/flutter_view_controller.h>
#include <windows.h>

#include "flutter_window.h"
#include "utils.h"

namespace {

// Enable DPI awareness (Per-Monitor V2 when available).
//
// The runner.exe.manifest normally declares this, but the mingw/windres cross
// build does not embed the manifest the way the Visual Studio generator does.
// A DPI-unaware process gets bitmap-stretched by Windows on a 125% / 150%
// scaled display, which makes the whole UI (especially small CJK text) look
// blurry. Declaring it again before any window exists costs nothing and does
// not depend on the manifest being present.
//
// Every API is resolved through GetProcAddress: it keeps the binary working on
// older systems and does not require the current SDK / mingw headers to declare
// DPI_AWARENESS_CONTEXT.
#if defined(_MSC_VER)
#pragma warning(push)
#pragma warning(disable : 4191)  // FARPROC to typed function pointer, on purpose
#endif

BOOL TryPerMonitorV2() {
  // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 == (HANDLE)-4
  using Fn = BOOL(WINAPI*)(HANDLE);
  HMODULE user32 = ::GetModuleHandleW(L"user32.dll");
  if (user32 == nullptr) return FALSE;
  auto set_context = reinterpret_cast<Fn>(
      ::GetProcAddress(user32, "SetProcessDpiAwarenessContext"));
  if (set_context == nullptr) return FALSE;
  // Cast through INT_PTR: plain -4 to pointer is C4312 at /W4.
  return set_context(reinterpret_cast<HANDLE>(static_cast<INT_PTR>(-4)));
}

void TryPerMonitor() {
  // Windows 8.1+: PROCESS_PER_MONITOR_DPI_AWARE == 2
  using Fn = HRESULT(WINAPI*)(int);
  HMODULE shcore = ::LoadLibraryW(L"shcore.dll");
  if (shcore == nullptr) return;
  auto set_awareness =
      reinterpret_cast<Fn>(::GetProcAddress(shcore, "SetProcessDpiAwareness"));
  if (set_awareness != nullptr) {
    // E_ACCESSDENIED means the manifest already set awareness: still fine.
    set_awareness(2);
  }
  ::FreeLibrary(shcore);
}

void TrySystemDpiAware() {
  // Windows Vista+: system DPI awareness, better than nothing.
  using Fn = BOOL(WINAPI*)();
  HMODULE user32 = ::GetModuleHandleW(L"user32.dll");
  if (user32 == nullptr) return;
  auto set_aware =
      reinterpret_cast<Fn>(::GetProcAddress(user32, "SetProcessDPIAware"));
  if (set_aware != nullptr) set_aware();
}

#if defined(_MSC_VER)
#pragma warning(pop)
#endif

void EnableDpiAwareness() {
  // When the manifest already declared awareness, SetProcessDpiAwarenessContext
  // returns FALSE; fall back to the 8.1 and Vista entry points. All three
  // failing is harmless: the app still starts, it just scales blurrily.
  if (TryPerMonitorV2()) return;
  TryPerMonitor();
  TrySystemDpiAware();
}

}  // namespace

int APIENTRY wWinMain(_In_ HINSTANCE instance, _In_opt_ HINSTANCE prev,
                      _In_ wchar_t *command_line, _In_ int show_command) {
  // Must run before the first window is created to take effect.
  EnableDpiAwareness();

  // Attach to console when present (e.g., 'flutter run') or create a
  // new console when running with a debugger.
  if (!::AttachConsole(ATTACH_PARENT_PROCESS) && ::IsDebuggerPresent()) {
    CreateAndAttachConsole();
  }

  // Initialize COM, so that it is available for use in the library and/or
  // plugins.
  ::CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);

  flutter::DartProject project(L"data");

  std::vector<std::string> command_line_arguments =
      GetCommandLineArguments();

  project.set_dart_entrypoint_arguments(std::move(command_line_arguments));

  FlutterWindow window(project);
  Win32Window::Point origin(10, 10);
  Win32Window::Size size(1280, 720);
  if (!window.Create(L"Morris GC", origin, size)) {
    return EXIT_FAILURE;
  }
  window.SetQuitOnClose(true);

  ::MSG msg;
  while (::GetMessage(&msg, nullptr, 0, 0)) {
    ::TranslateMessage(&msg);
    ::DispatchMessage(&msg);
  }

  ::CoUninitialize();
  return EXIT_SUCCESS;
}
