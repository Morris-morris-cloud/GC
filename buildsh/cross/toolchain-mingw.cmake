# 在 Linux / macOS 上交叉编译 Flutter Windows 桌面应用的 CMake 工具链。
#
# 原理：Flutter 的 Windows 嵌入层（windows/runner/*.cpp + cpp_client_wrapper）
# 是纯 Win32 + 标准 C++，只通过 C ABI 调用 flutter_windows.dll。因此用
# mingw-w64（GCC，posix 线程模型）即可产出与官方 toolchain 等价的 PE 产物，
# 不需要 Windows 机器，也不需要 MSVC。
#
# 用法：
#   cmake -B build -G Ninja -DCMAKE_TOOLCHAIN_FILE=.../toolchain-mingw.cmake ...

set(CMAKE_SYSTEM_NAME Windows)
set(CMAKE_SYSTEM_VERSION 10.0)
set(CMAKE_SYSTEM_PROCESSOR x86_64)

set(MINGW_PREFIX "x86_64-w64-mingw32" CACHE STRING "目标三元组前缀")

# 必须用 posix 线程模型：Flutter 的 C++ wrapper 依赖 std::mutex / std::thread，
# win32 线程模型下的 libstdc++ 不提供这些类型。
find_program(MINGW_C   NAMES ${MINGW_PREFIX}-gcc-posix   ${MINGW_PREFIX}-gcc   REQUIRED)
find_program(MINGW_CXX NAMES ${MINGW_PREFIX}-g++-posix   ${MINGW_PREFIX}-g++   REQUIRED)
find_program(MINGW_RC  NAMES ${MINGW_PREFIX}-windres                          REQUIRED)

set(CMAKE_C_COMPILER   "${MINGW_C}")
set(CMAKE_CXX_COMPILER "${MINGW_CXX}")
set(CMAKE_RC_COMPILER  "${MINGW_RC}")

set(CMAKE_FIND_ROOT_PATH "/usr/${MINGW_PREFIX}")
set(CMAKE_FIND_ROOT_PATH_MODE_PROGRAM NEVER)
set(CMAKE_FIND_ROOT_PATH_MODE_LIBRARY ONLY)
set(CMAKE_FIND_ROOT_PATH_MODE_INCLUDE ONLY)
set(CMAKE_FIND_ROOT_PATH_MODE_PACKAGE ONLY)

# 静态链接 libgcc / libstdc++ / winpthread，产物只依赖 flutter_windows.dll
# 和系统 DLL，拷到干净机器上也能跑。
set(CMAKE_CXX_FLAGS_INIT "-DUNICODE -D_UNICODE -DNOMINMAX -D_HAS_EXCEPTIONS=1")
set(CMAKE_EXE_LINKER_FLAGS_INIT "-static -static-libgcc -static-libstdc++")

# GCC 16 兼容性
set(CMAKE_CXX_FLAGS "${CMAKE_CXX_FLAGS} -Wno-compound-token-split-by-macro -Wno-deprecated-attributes")

# 强制 C++17，避免 GCC 16 的 C++20 三路比较问题
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# 兼容 mingw-w64 14 + GCC 16 的问题
set(CMAKE_CXX_FLAGS "${CMAKE_CXX_FLAGS} -std=c++17 -Wno-error -D__STDC_CONSTANT_MACROS")
