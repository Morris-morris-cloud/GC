# ---------------------------------------------------------------------------
# Earth Model Studio 构建脚本（Windows PowerShell）
#
#   .\build.ps1                 编译
#   .\build.ps1 run             运行
#   .\build.ps1 test            离屏渲染校验
#   .\build.ps1 models          编译模型 dll + 生成封面 + 自动打版本
#   .\build.ps1 stamp           只重新生成 version.json 与模型版本
#   .\build.ps1 publish         编译三个平台启动器（不内置模型）+ 建立 .GC 运行目录
#   .\build.ps1 pack-models     单独打包模型数据（含 version.json）
#   .\build.ps1 pack-launcher   生成三个平台的启动器更新包 zip
#   .\build.ps1 update-test     离线验证自动更新链路
#   .\build.ps1 clean           清理
#
# 分层：src\ 只是启动器；模型全部在 model\；**发布包不内置模型**。
#
# 运行目录（发布包里就长这样）：
#   <安装目录>\
#     EarthModelStudio.exe
#     .GC\settings.json
#     .GC\model\       启动时从 gitcode 下载的模型
#     .GC\cache\       下载暂存
#     .GC\update\      待下次启动应用的启动器更新
# ---------------------------------------------------------------------------
param(
    [Parameter(Position = 0)]
    [ValidateSet('build', 'run', 'test', 'publish', 'models', 'stamp', 'pack-models',
                 'pack-launcher', 'update-test', 'bundle', 'clean', 'doctor')]
    [string]$Task = 'build'
)

$ErrorActionPreference = 'Stop'
$Root = $PSScriptRoot
Set-Location $Root

# Avalonia 遥测任务在只读用户目录/CI/沙箱会因写日志失败而中断编译（MSB4018）。
# 官方 opt-out；Directory.Build.targets 里还有一层项目内的兜底。
$env:AVALONIA_TELEMETRY_OPTOUT = '1'

$AppProj = 'src/main/dotnet/EarthModelStudio/EarthModelStudio.csproj'
$TestProj = 'src/test/dotnet/EarthModelStudio.Tests/EarthModelStudio.Tests.csproj'
$PreviewProj = 'model/_engine/PreviewGen/PreviewGen.csproj'
$StampProj = 'model/_engine/VersionStamp/VersionStamp.csproj'
$Sln = 'EarthModelStudio.sln'
$Version = '1.0.0'

function Stamp-Versions {
    Write-Host '==> 自动计算模型版本并生成 version.json'
    dotnet run --project $StampProj -c Release -- $Root $Version
}

function Build-Models {
    Write-Host '==> 编译模型功能程序集（源码就在各模型目录的 src\ 里）'
    Get-ChildItem "$Root/model" -Directory | ForEach-Object {
        $id = $_.Name
        $src = Join-Path $_.FullName 'src'
        if (-not (Test-Path $src)) { return }
        $proj = Get-ChildItem $src -Filter *.csproj | Select-Object -First 1
        if ($null -eq $proj) { return }
        dotnet build $proj.FullName -c Release
        $data = Join-Path $_.FullName "$id.model"
        if (Test-Path $data) {
            Get-ChildItem (Join-Path $src 'bin/Release/net8.0') -Filter *.dll | ForEach-Object {
                if ($_.Name -eq 'EarthModelStudio.ModelSdk.dll') { return }
                Copy-Item -Force $_.FullName $data
                Write-Host "    -> model/$id/$id.model/$($_.Name)"
            }
        }
    }
    Write-Host '==> 用模型自己的渲染器生成封面'
    dotnet run --project $PreviewProj -c Release -- $Root
    Stamp-Versions
}

# 发布目录只放启动器 + 空的 .GC 运行骨架，绝不放模型
function Prepare-RuntimeLayout([string]$RelDir) {
    $dest = "$Root/$RelDir"
    if (-not (Test-Path $dest)) { return }
    foreach ($old in @("$dest/model", "$dest/examples")) {
        if (Test-Path $old) { Remove-Item -Recurse -Force $old }
    }
    foreach ($dir in @("$dest/.GC/model", "$dest/.GC/cache", "$dest/.GC/update")) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    }
    $readme = @'
模型库目录（.GC/model/）
启动时程序会读取 gitcode 仓库的 version.json，把最新版模型下载到这里。
离线使用也可以手动放入 <模型id>/<模型id>.json + <模型id>/<模型id>.model/。

运行目录：
  <可执行文件>
  .GC/settings.json
  .GC/model/      本目录（下载的模型）
  .GC/cache/      下载暂存
  .GC/update/     待下次启动应用的启动器更新
'@
    Set-Content -Path "$dest/.GC/model/README.txt" -Value $readme -Encoding UTF8

    $settingsPath = "$dest/.GC/settings.json"
    if (-not (Test-Path $settingsPath)) {
        $json = @'
{
  "language": "auto",
  "theme": "light",
  "renderFps": 30,
  "memoryMb": 2048,
  "autoUpdateOnStartup": true,
  "updateRepository": "https://gitcode.com/2509_92447106/GC",
  "updateManifestUrl": "",
  "updateLocalRoot": "",
  "installedModelVersions": {},
  "launcherVersion": "",
  "pendingLauncherVersion": "",
  "lastUpdateCheck": "",
  "sources": [],
  "recentModels": []
}
'@
        Set-Content -Path $settingsPath -Value $json -Encoding UTF8
    }
    Write-Host "    运行目录 -> $RelDir/.GC （settings.json + model/ + cache/ + update/）"
}

function Publish-Rid([string]$Rid, [string]$OutDir) {
    Write-Host "==> 编译 $Rid -> $OutDir"
    dotnet publish $AppProj -c Release -r $Rid --self-contained true `
        -p:Version=$Version -p:DebugType=none -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o "$Root/$OutDir"
    foreach ($old in @("$Root/$OutDir/model", "$Root/$OutDir/examples")) {
        if (Test-Path $old) { Remove-Item -Recurse -Force $old }
    }
}

function Pack-Models {
    New-Item -ItemType Directory -Force -Path "$Root/dist" | Out-Null
    $zip = "$Root/dist/models-$Version.zip"
    if (Test-Path $zip) { Remove-Item -Force $zip }
    Write-Host "==> 打包模型数据 -> dist/models-$Version.zip"
    $stage = Join-Path $env:TEMP "ems-models-stage"
    if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    Get-ChildItem "$Root/model" -Recurse -File | Where-Object {
        $_.FullName -notmatch '\\(bin|obj)\\' -and $_.Extension -ne '.user'
    } | ForEach-Object {
        $rel = $_.FullName.Substring("$Root/model".Length + 1)
        $dest = Join-Path $stage $rel
        New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
        Copy-Item -Force $_.FullName $dest
    }
    Compress-Archive -Path "$stage/*" -DestinationPath $zip -Force
    Remove-Item -Recurse -Force $stage
}

# 启动器更新包：只包含程序文件，排除 .GC（否则会覆盖用户模型与设置）
function Pack-Launcher {
    Write-Host '==> 生成启动器更新包（不含 .GC）'
    $targets = @{
        'GC-win-x64.zip'   = "$Root/dist/win-x64"
        'GC-win-arm64.zip' = "$Root/dist/win-arm64"
        'GC-osx-arm64.zip' = "$Root/dist/mac-arm64/EarthModelStudio.app/Contents/MacOS"
    }
    if (-not (Test-Path $targets['GC-osx-arm64.zip'])) {
        $targets['GC-osx-arm64.zip'] = "$Root/dist/mac-arm64"
    }
    foreach ($name in $targets.Keys) {
        $src = $targets[$name]
        if (-not (Test-Path $src)) { continue }
        $stage = Join-Path $env:TEMP ("ems-launcher-" + [IO.Path]::GetFileNameWithoutExtension($name))
        if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
        New-Item -ItemType Directory -Force -Path $stage | Out-Null
        Get-ChildItem $src -Recurse -File | Where-Object {
            $_.FullName -notmatch '\\\.GC\\' -and $_.Name -ne '.DS_Store'
        } | ForEach-Object {
            $rel = $_.FullName.Substring($src.Length).TrimStart('\', '/')
            $dest = Join-Path $stage $rel
            New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
            Copy-Item -Force $_.FullName $dest
        }
        $zip = "$Root/dist/$name"
        if (Test-Path $zip) { Remove-Item -Force $zip }
        Compress-Archive -Path "$stage/*" -DestinationPath $zip -Force
        Remove-Item -Recurse -Force $stage
    }
    Stamp-Versions
    Get-ChildItem "$Root/dist/GC-*.zip" | Select-Object Name, Length
}

switch ($Task) {
    'build' { dotnet build $Sln -c Debug }
    'run' { dotnet run --project $AppProj -c Debug }
    'test' { dotnet run --project $TestProj -c Debug -- "$Root/build/screenshots" }
    'publish' {
        New-Item -ItemType Directory -Force -Path "$Root/dist" | Out-Null
        Stamp-Versions
        Publish-Rid 'osx-arm64' 'dist/mac-arm64'
        Publish-Rid 'win-x64' 'dist/win-x64'
        Publish-Rid 'win-arm64' 'dist/win-arm64'
        Prepare-RuntimeLayout 'dist/win-x64'
        Prepare-RuntimeLayout 'dist/win-arm64'
        Prepare-RuntimeLayout 'dist/mac-arm64'
        Pack-Launcher
        Write-Host '==> 三个平台编译完成（发布包不含模型，首次启动自动下载），见 dist\' -ForegroundColor Green
        Write-Host '    把 version.json / model/ 推送到 gitcode，启动器就会自动检查更新'
    }
    'models' { Build-Models }
    'stamp' { Stamp-Versions }
    'bundle' { Write-Host '已弃用：发布包不再内置模型。请改用 .\build.ps1 stamp 与 gitcode 自动更新。' }
    'pack-models' {
        Build-Models
        Pack-Models
    }
    'pack-launcher' { Pack-Launcher }
    'update-test' { dotnet run --project $TestProj -c Debug -- '--update-test' $Root }
    'clean' {
        Remove-Item -Recurse -Force "$Root/build", "$Root/dist" -ErrorAction SilentlyContinue
        Get-ChildItem "$Root/src" -Recurse -Directory -Include bin, obj |
            Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    }
    'doctor' { dotnet --info }
}
