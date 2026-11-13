@echo off
REM autogit.bat —— 交互式提交（Windows）
REM
REM 用法：
REM   git.bat autogit              交互模式（推荐）
REM   git.bat autogit -m "信息"     跳过信息输入，其余仍交互
REM   git.bat autogit --dry-run    只看会提交什么，不动仓库
REM   git.bat autogit -y           全部确认跳过（配合 -m 可全自动）
REM
REM 交互流程：显示改动 → 逐个选仓库 → 输入提交信息 → 确认 → 执行
REM 自动跳过：dist\ build\ bin\ obj\ .GC\（任意层级）

setlocal enabledelayedexpansion
chcp 65001 >nul 2>&1

set "ROOT_DIR=%~1"
if "%ROOT_DIR%"=="" set "ROOT_DIR=%CD%"
cd /d "%ROOT_DIR%" 2>nul || (echo !! 目录不存在 & exit /b 1)
shift

set "MSG="
set "DRY=0"
set "ASSUME_YES=0"
:parse
if "%~1"=="" goto parsed
if /i "%~1"=="-m" ( set "MSG=%~2" & shift & shift & goto parse )
if /i "%~1"=="--message" ( set "MSG=%~2" & shift & shift & goto parse )
if /i "%~1"=="--dry-run" ( set "DRY=1" & shift & goto parse )
if /i "%~1"=="-y" ( set "ASSUME_YES=1" & shift & goto parse )
if /i "%~1"=="--yes" ( set "ASSUME_YES=1" & shift & goto parse )
echo 未知参数：%~1
exit /b 1
:parsed

REM ---------- 0. 前置检查 ----------
git rev-parse --git-dir >nul 2>&1
if errorlevel 1 ( echo !! 这里不是 git 仓库：%CD% & exit /b 1 )

for /f "delims=" %%b in ('git rev-parse --abbrev-ref HEAD 2^>nul') do set "BRANCH=%%b"
if "%BRANCH%"=="" ( echo !! 当前不在任何分支上 & exit /b 1 )

set "REMOTES="
for /f "delims=" %%r in ('git remote') do set "REMOTES=!REMOTES! %%r"
if "!REMOTES!"=="" ( echo !! 没有配置任何远程仓库 & exit /b 1 )

echo ==============================================
echo  仓库：%CD%
echo  分支：%BRANCH%
echo ==============================================
echo.

echo ==^> 会被排除的构建产物：
for %%d in (dist build bin obj .GC) do (
  for /f %%c in ('git ls-files -- "*%%d/*" 2^>nul ^| find /c /v ""') do (
    if not "%%c"=="0" echo     %%d\  %%c 个文件
  )
)
echo.

echo ==^> 待提交的改动：
git status --short | findstr /v /r "\\dist\\ \\build\\ \\bin\\ \\obj\\ \\.GC\\" | more +0
echo.

if "%DRY%"=="1" (
  echo ==^> --dry-run：未做任何修改
  exit /b 0
)

REM ---------- 2. 选择仓库 ----------
echo ==^> 选择要推送的仓库（y=推送 / n=跳过 / a=全部 / q=退出）
echo.
for %%r in (!REMOTES!) do (
  for /f "delims=" %%u in ('git remote get-url %%r 2^>nul') do echo   %%r  %%u
)
echo.

set "SELECTED="
for %%r in (!REMOTES!) do (
  if "!ASSUME_YES!"=="1" (
    set "SELECTED=!SELECTED! %%r"
    echo   [自动] %%r
  ) else (
    set "ANS="
    set /p "ANS=  推送到 %%r (y/n/a/q) "
    if /i "!ANS!"=="a" (
      set "SELECTED=!REMOTES!"
      goto selected_done
    ) else if /i "!ANS!"=="q" (
      echo 已取消
      exit /b 0
    ) else if /i "!ANS!"=="y" (
      set "SELECTED=!SELECTED! %%r"
      echo        -^> 推送
    ) else (
      echo        -^> 跳过
    )
  )
)
:selected_done

set "PUSH=1"
if "!SELECTED!"=="" (
  echo.
  echo !! 没有选择任何仓库，只提交到本地不推送
  set "PUSH=0"
)

REM ---------- 3. 提交信息 ----------
echo.
if "%MSG%"=="" (
  echo ==^> 请输入提交信息（留空自动生成）
  set "MSG="
  set /p "MSG=  ^> "
)
if "%MSG%"=="" (
  for /f "tokens=1-3 delims=/ " %%a in ('date /t') do set "D=%%a-%%b-%%c"
  set "MSG=更新项目（!D!）"
  echo   （自动生成：!MSG!）
)

REM ---------- 4. 确认 ----------
echo.
echo ==============================================
echo  提交信息：%MSG%
if "%PUSH%"=="1" (
  echo  推送到：  !SELECTED!
) else (
  echo  推送到：  （仅本地提交）
)
echo ==============================================
if not "%ASSUME_YES%"=="1" (
  set "OK="
  set /p "OK=确认执行？(y/N) "
  if /i not "!OK!"=="y" ( echo 已取消 & exit /b 0 )
)

REM ---------- 5. 执行 ----------
echo.
echo ==^> 整理索引
for %%d in (dist build bin obj .GC) do (
  for /f "delims=" %%f in ('git ls-files -- "*%%d/*" 2^>nul') do (
    git rm -r --cached --quiet -- "*%%d/*" 2>nul
    echo     - 移出 %%d\ 内的文件（本地保留）
    goto nextdir_%%d
  )
  :nextdir_%%d
)

git add -A 2>nul

for /f %%n in ('git diff --cached --name-only ^| find /c /v ""') do set "STAGED=%%n"
if "%STAGED%"=="0" (
  echo ==^> 没有可提交的改动
) else (
  git commit -q -m "%MSG%"
  echo ==^> 已提交 %STAGED% 个文件
  git log --oneline -1
)

REM ---------- 6. 推送 ----------
if "%PUSH%"=="0" (
  echo.
  echo ==^> 仅本地提交，未推送
  exit /b 0
)

set "FAILED=0"
for %%r in (!SELECTED!) do (
  echo.
  echo ==^> 推送到 %%r
  git push -u %%r "%BRANCH%" 2>&1
  if errorlevel 1 (
    echo     !! 失败。若确认以本地为准：
    echo        git push --force-with-lease %%r %BRANCH%
    set "FAILED=1"
  ) else (
    echo     OK
  )
)

echo.
if "%FAILED%"=="1" (
  echo ==^> 完成，有仓库推送失败
  exit /b 1
)
echo ==^> 全部完成
exit /b 0
