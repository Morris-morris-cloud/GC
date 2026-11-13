@echo off
REM autogit.bat —— 提交并推送到所有已配置的仓库（Windows 版一键多仓同步）
REM
REM 用法：
REM   git.bat autogit                     自动生成提交信息并推送所有仓库
REM   git.bat autogit -m "提交信息"        指定提交信息
REM   git.bat autogit -m "..." -n          只提交，不推送
REM   git.bat autogit --dry-run           只看会提交什么，不动仓库
REM
REM 多仓库：遍历 git remote 里的每一个远程，依次 push。
REM   加仓库： git remote add gitee git@github.com:user/repo.git
REM
REM 自动跳过（永不提交）：dist\ build\ bin\ obj\ .GC\ node_modules\ 等

setlocal enabledelayedexpansion
chcp 65001 >nul 2>&1

set "ROOT_DIR=%~1"
if "%ROOT_DIR%"=="" set "ROOT_DIR=%CD%"
cd /d "%ROOT_DIR%" 2>nul || (echo !! 目录不存在：%ROOT_DIR% & exit /b 1)
shift

set "MSG="
set "PUSH=1"
set "DRY=0"
:parse
if "%~1"=="" goto parsed
if /i "%~1"=="-m" ( set "MSG=%~2" & shift & shift & goto parse )
if /i "%~1"=="--message" ( set "MSG=%~2" & shift & shift & goto parse )
if /i "%~1"=="-n" ( set "PUSH=0" & shift & goto parse )
if /i "%~1"=="--no-push" ( set "PUSH=0" & shift & goto parse )
if /i "%~1"=="--dry-run" ( set "DRY=1" & shift & goto parse )
echo 未知参数：%~1
exit /b 1
:parsed

REM ---------- 0. 前置检查 ----------
git rev-parse --git-dir >nul 2>&1
if errorlevel 1 ( echo !! 这里不是 git 仓库：%CD% & exit /b 1 )

for /f "delims=" %%b in ('git rev-parse --abbrev-ref HEAD 2^>nul') do set "BRANCH=%%b"
if "%BRANCH%"=="" ( echo !! 当前不在任何分支上 & exit /b 1 )

REM ---------- 1. 确保忽略规则 ----------
if exist ".gitignore" (
  for %%p in ("dist/" "build/" "bin/" "obj/" ".GC/") do (
    findstr /x /c:%%p ".gitignore" >nul 2>&1
    if errorlevel 1 (
      echo %%p>>".gitignore"
      echo     + .gitignore 追加 %%~p
    )
  )
)

REM ---------- 2. 收集改动 ----------
echo ==^> 检查改动（分支 %BRANCH%）
git add -A 2>nul

for %%d in (dist build bin obj .GC) do (
  for /f "delims=" %%f in ('git ls-files "%%d" 2^>nul') do (
    git rm -r --cached --quiet "%%d" 2>nul
    echo     - 从索引移除 %%d\（本地文件保留）
    goto nextdir_%%d
  )
  :nextdir_%%d
)

for /f %%n in ('git diff --cached --name-only ^| find /c /v ""') do set "STAGED=%%n"
if "%STAGED%"=="0" (
  echo ==^> 没有需要提交的改动
) else (
  echo ==^> 待提交 %STAGED% 个文件
)

if "%DRY%"=="1" (
  echo ==^> --dry-run：到此为止
  exit /b 0
)

REM ---------- 3. 提交 ----------
if not "%STAGED%"=="0" (
  if "%MSG%"=="" (
    for /f "tokens=1-3 delims=/ " %%a in ('date /t') do set "D=%%a-%%b-%%c"
    set "MSG=更新项目（!D!）"
  )
  git commit -q -m "%MSG%"
  echo ==^> 已提交：%MSG%
) else (
  echo ==^> 跳过提交（无改动）
)

REM ---------- 4. 推送到所有远程 ----------
if "%PUSH%"=="0" ( echo ==^> -n 指定不推送 & exit /b 0 )

for /f "delims=" %%r in ('git remote') do (
  echo ==^> 推送到 %%r
  git push -u %%r "%BRANCH%" 2>&1
  if errorlevel 1 (
    echo     !! 推送失败。若确认以本地为准：
    echo        git push --force-with-lease %%r %BRANCH%
    set "FAILED=1"
  ) else (
    echo     OK
  )
)

echo.
if "%FAILED%"=="1" (
  echo ==^> 完成，但有仓库推送失败
  exit /b 1
)
echo ==^> 全部仓库已同步
exit /b 0
