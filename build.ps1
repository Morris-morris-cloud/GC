# ---------------------------------------------------------------------------
# GC 构建入口（Windows PowerShell，薄封装）
# 实际代码在 buildsh/ 里。Windows 上若无 sh，则走内置的最小实现。
#   .\build.ps1 build | check | models | stamp | run | test | clean | doctor
# ---------------------------------------------------------------------------
param(
    [Parameter(Position = 0)]
    [ValidateSet('build','check','models','stamp','pack-models','pack-launcher',
                 'run','test','update-test','clean','doctor','dist')]
    [string]$Task = 'build'
)

$ErrorActionPreference = 'Stop'
$Root = $PSScriptRoot
Set-Location $Root
$env:AVALONIA_TELEMETRY_OPTOUT = '1'

$AppProj  = 'GC-BOOT/GC.csproj'
$TestProj = 'GC/src/test/dotnet/GC.Tests/GC.Tests.csproj'
$StampProj= 'model/_engine/VersionStamp/VersionStamp.csproj'
$Sln      = 'GC.sln'
$Version  = ([regex]::Match((Get-Content Directory.Build.props -Raw), '<Version>(.*?)</Version>')).Groups[1].Value
if (-not $Version) { $Version = '1.0.0' }
New-Item -ItemType Directory -Force -Path "$Root/build/logs" | Out-Null

# 优先复用 buildsh 的 shell 实现（Git Bash / WSL / sh 可用时）
$sh = Get-Command sh -ErrorAction SilentlyContinue
if ($sh) {
    & $sh.Source "$Root/build.sh" $Task
    exit $LASTEXITCODE
}

function Stamp { dotnet run --project $StampProj -c Release -- $Root $Version }
function Build-Models {
    Get-ChildItem "$Root/model" -Directory | ForEach-Object {
        $id = $_.Name
        $src = Join-Path $_.FullName 'src'
        if (-not (Test-Path $src)) { return }
        $proj = Get-ChildItem $src -Filter *.csproj | Select-Object -First 1
        if (-not $proj) { return }
        dotnet build $proj.FullName -c Release
    }
    Stamp
}

switch ($Task) {
    'build' {
        Write-Host '==> 编译三平台（进度见 buildsh/lib/progress.sh，建议用 Git Bash 运行 ./build.sh build）'
        Remove-Item -Recurse -Force "$Root/dist" -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Force -Path "$Root/dist" | Out-Null
        Stamp
        foreach ($rid in @('osx-arm64','win-x64','win-arm64')) {
            dotnet publish $AppProj -c Release -r $rid --self-contained true `
                -p:Version=$Version -p:DebugType=none -p:PublishSingleFile=true `
                -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false `
                -o "$Root/dist/$rid"
        }
        Write-Host '==> 完成（详细打包请用 ./build.sh build）'
    }
    'check' {
        dotnet build $Sln -c Debug
        $env:EMS_SELFTEST = '1'; dotnet run --project $AppProj -c Debug --no-build; Remove-Item Env:\EMS_SELFTEST
        dotnet run --project $TestProj -c Debug --no-build -- "$Root/build/screenshots"
    }
    'models'      { Build-Models }
    'stamp'       { Stamp }
    'pack-models' { Build-Models }
    'pack-launcher' { Write-Host '请用 ./build.sh pack-launcher' }
    'run'         { dotnet run --project $AppProj -c Debug }
    'test'        { dotnet run --project $TestProj -c Debug -- "$Root/build/screenshots" }
    'update-test' { dotnet run --project $TestProj -c Debug -- '--update-test' $Root }
    'clean'       { Remove-Item -Recurse -Force "$Root/build","$Root/dist" -ErrorAction SilentlyContinue; Write-Host '==> 已清理' }
    'doctor'      { dotnet --info | Select-Object -First 20 }
    'dist'        { Get-ChildItem "$Root/dist" -ErrorAction SilentlyContinue }
}
