@echo off
REM ---------------------------------------------------------------------------
REM  git.bat —— GC 仓库的 git 导航入口（Windows）
REM
REM    git.bat                 显示帮助（列出 gitshell\ 里所有可用命令）
REM    git.bat <命令> [参数]   调用 gitshell\<命令>.bat
REM
REM  例：
REM    git.bat autogit          提交并推送到所有已配置的仓库
REM    git.bat autogit -m "修复" 指定提交信息
REM
REM  实际脚本都在 gitshell\ 里，本文件只负责查找与分发（与 build.ps1 同样的设计）。
REM ---------------------------------------------------------------------------
setlocal
chcp 65001 >nul 2>&1

set "ROOT_DIR=%~dp0"
if "%ROOT_DIR:~-1%"=="\" set "ROOT_DIR=%ROOT_DIR:~0,-1%"
set "SHELL_DIR=%ROOT_DIR%\gitshell"
cd /d "%ROOT_DIR%"

if "%~1"=="" goto help
if /i "%~1"=="-h" goto help
if /i "%~1"=="--help" goto help
if /i "%~1"=="help" goto help

set "CMD=%~1"
shift
set "SCRIPT=%SHELL_DIR%\%CMD%.bat"

if not exist "%SCRIPT%" (
  echo !! 没有这个命令：%CMD%
  echo.
  goto help
  exit /b 1
)

call "%SCRIPT%" "%ROOT_DIR%" %*
exit /b %errorlevel%

:help
echo GC git 工具（gitshell\）
echo.
if exist "%SHELL_DIR%" (
  for %%f in ("%SHELL_DIR%\*.bat") do (
    echo   %%~nf
  )
) else (
  echo   （gitshell\ 目录不存在）
)
echo.
echo 用法：git.bat ^<命令^> [参数]
exit /b 0
