@echo off
REM 构建 GC-UI 前端（Windows 上运行；macOS 请用 build-ui.sh）。
REM
REM   GC-UI\build-ui.cmd

setlocal
set ROOT=%~dp0..
set FRONTEND=%ROOT%\GC-UI\frontend

where flutter >nul 2>nul
if errorlevel 1 (
  echo 找不到 flutter，请先安装 Flutter SDK 并加入 PATH。
  exit /b 1
)

echo ==^> 构建后端（GC.UI.Server）
dotnet build "%ROOT%\GC-UI\backend\GC.UI.Server\GC.UI.Server.csproj" -c Release
if errorlevel 1 exit /b 1

echo ==^> 构建前端（windows）
cd /d "%FRONTEND%"
call flutter pub get
if errorlevel 1 exit /b 1
call flutter build windows --release
if errorlevel 1 exit /b 1

echo ==^> 完成
echo 产物: %FRONTEND%\build\windows\x64\runner\Release\
endlocal
