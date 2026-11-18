cmake_minimum_required(VERSION 3.14)

# 交叉编译 Flutter Windows 桌面外壳。
#
# 只编译 C++ 层：runner（Win32 窗口）+ cpp_client_wrapper（Dart 互操作）。
# Dart 侧的 flutter_assets 由 flutter assemble 单独产出，最后与
# flutter_windows.dll 一起摆进同一个目录就是完整的可运行包。
#
# 与 windows/CMakeLists.txt 的区别：不调用 tool_backend.bat（它在非 Windows
# 上不可执行），也不依赖 Visual Studio 生成器。

project(gc_ui_cross LANGUAGES CXX RC)

get_filename_component(APP_DIR "${CMAKE_CURRENT_LIST_DIR}/../../GC-UI/frontend" ABSOLUTE)

set(WIN_DIR     "${APP_DIR}/windows")
set(RUN_DIR     "${WIN_DIR}/runner")
set(EPHEMERAL   "${WIN_DIR}/flutter/ephemeral")
set(WRAPPER     "${EPHEMERAL}/cpp_client_wrapper")
set(DLL_LIB     "${EPHEMERAL}/flutter_windows.dll.lib")

foreach(required
    "${EPHEMERAL}/flutter_windows.dll"
    "${DLL_LIB}"
    "${WRAPPER}/core_implementations.cc")
  if(NOT EXISTS "${required}")
    message(FATAL_ERROR
      "缺少 Flutter Windows 引擎产物：${required}\n"
      "先运行 buildsh/cross/build-ui-win-cross.sh 里的 fetch_engine 步骤。")
  endif()
endforeach()

set(FLUTTER_VERSION "1.0.0" CACHE STRING "写入 exe 版本资源")

# windres 在非 Windows 主机上不按 rc 文件所在目录解析图标路径，
# 这里把 resources\\app_icon.ico 换成绝对路径再交给 windres。
# 注意只替换这一处：rc 里其它 "\0" 是字符串转义，不能动。
file(READ "${RUN_DIR}/Runner.rc" _rc_text)
string(REPLACE "resources\\\\app_icon.ico" "${RUN_DIR}/resources/app_icon.ico" _rc_text "${_rc_text}")
# windres 收到 -DFLUTTER_VERSION=\"1.0.0\" 时会把反斜杠留在宏值里，展开成非法 token。
# 直接把宏定义成字面量，绕开命令行转义。
string(REPLACE "#define VERSION_AS_STRING FLUTTER_VERSION"
               "#define VERSION_AS_STRING \"${FLUTTER_VERSION}\""
               _rc_text "${_rc_text}")
file(WRITE "${CMAKE_CURRENT_BINARY_DIR}/Runner.cross.rc" "${_rc_text}")

# ★ DPI 清单必须自己嵌进去。
#
# windows/runner/CMakeLists.txt 把 runner.exe.manifest 当源文件列进 target，
# Visual Studio 生成器会把它交给链接器（/MANIFESTINPUT）自动嵌入；
# 但这里的交叉编译只用 windres 编 Runner.rc，CMake 不会做这一步，
# 编出来的 GC-UI.exe 就完全没有清单 —— Windows 当它「DPI 不感知」，
# 在 125% / 150% 缩放的屏幕上整体位图拉伸，界面全是糊的。
#
# 因此把清单直接写成资源条目：id 1 = CREATEPROCESS_MANIFEST_RESOURCE_ID，
# type 24 = RT_MANIFEST。windres 不按相对路径解析，这里给绝对路径。
file(APPEND "${CMAKE_CURRENT_BINARY_DIR}/Runner.cross.rc"
     "\n1 24 \"${RUN_DIR}/runner.exe.manifest\"\n")

add_executable(${BINARY_NAME} WIN32
  "${RUN_DIR}/main.cpp"
  "${RUN_DIR}/flutter_window.cpp"
  "${RUN_DIR}/utils.cpp"
  "${RUN_DIR}/win32_window.cpp"
  "${WIN_DIR}/flutter/generated_plugin_registrant.cc"
  "${WRAPPER}/core_implementations.cc"
  "${WRAPPER}/standard_codec.cc"
  "${WRAPPER}/plugin_registrar.cc"
  "${WRAPPER}/flutter_engine.cc"
  "${WRAPPER}/flutter_view_controller.cc"
  "${CMAKE_CURRENT_BINARY_DIR}/Runner.cross.rc"
)

target_include_directories(${BINARY_NAME} PRIVATE
  "${RUN_DIR}"
  "${WIN_DIR}"
  "${EPHEMERAL}"
  "${EPHEMERAL}/include"
  "${WRAPPER}/include"
)

target_compile_features(${BINARY_NAME} PRIVATE cxx_std_17)

# Flutter 的 C++ wrapper 需要 RTTI 与异常；线程安全相关的锁依赖 posix 线程模型。
target_compile_options(${BINARY_NAME} PRIVATE
  -fexceptions
  -frtti
  -Wall
  -Wno-unused-parameter
  -Wno-missing-field-initializers
)

# Runner.rc 里的 #include "resource.h" / runner.exe.manifest 都是相对路径，
# windres 不按源文件目录解析，必须显式给 -I。
set_source_files_properties("${RUN_DIR}/Runner.rc" PROPERTIES
  COMPILE_FLAGS "-I\"${RUN_DIR}\"")

target_compile_definitions(${BINARY_NAME} PRIVATE
  "FLUTTER_VERSION=\"${FLUTTER_VERSION}\""
  "FLUTTER_VERSION_MAJOR=${FLUTTER_VERSION_MAJOR}"
  "FLUTTER_VERSION_MINOR=${FLUTTER_VERSION_MINOR}"
  "FLUTTER_VERSION_PATCH=${FLUTTER_VERSION_PATCH}"
  "FLUTTER_VERSION_BUILD=${FLUTTER_VERSION_BUILD}"
  "NOMINMAX"
)

target_link_libraries(${BINARY_NAME} PRIVATE
  "${DLL_LIB}"
  dwmapi
  ole32
  uuid
)

# runner 的入口是 wWinMain（宽字符版）；MSVC 自动处理，mingw 需要 -municode
# 才会链接 wWinMainCRTStartup，否则报 undefined reference to `WinMain'。
target_link_options(${BINARY_NAME} PRIVATE -municode)

set_target_properties(${BINARY_NAME} PROPERTIES
  OUTPUT_NAME "GC-UI"
  SUFFIX ".exe"
)
