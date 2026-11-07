# ---------------------------------------------------------------------------
# Earth Model Studio 构建脚本（Windows PowerShell）
#
#   .\build.ps1                 编译
#   .\build.ps1 run             运行
#   .\build.ps1 test            离屏渲染校验
#   .\build.ps1 publish         一次性编译三个平台 -> dist\
#   .\build.ps1 models          编译模型功能程序集 + 生成封面
#   .\build.ps1 pack-models     编译模型后整体打包 -> dist\models-x.y.z.zip
#   .\build.ps1 clean           清理
#
# 分层约定：src\ 只是启动器，不含任何模型数据；模型全部放在 model\（外置数据）。
# ---------------------------------------------------------------------------
param(
    [Parameter(Position = 0)]
    [ValidateSet('build', 'run', 'test', 'publish', 'models', 'pack-models', 'clean', 'doctor')]
    [string]$Task = 'build'
)

$ErrorActionPreference = 'Stop'
$Root = $PSScriptRoot
Set-Location $Root

$AppProj = 'src/main/dotnet/EarthModelStudio/EarthModelStudio.csproj'
$TestProj = 'src/test/dotnet/EarthModelStudio.Tests/EarthModelStudio.Tests.csproj'
$Sln = 'EarthModelStudio.sln'
$Version = '1.0.0'

function Publish-Rid([string]$Rid, [string]$OutDir) {
    Write-Host "==> 编译 $Rid -> $OutDir"
    dotnet publish $AppProj -c Release -r $Rid --self-contained true `
        -p:Version=$Version -p:DebugType=none -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o "$Root/$OutDir"
    if (Test-Path "$Root/$OutDir/model") { Remove-Item -Recurse -Force "$Root/$OutDir/model" }
    if (Test-Path "$Root/$OutDir/examples") { Remove-Item -Recurse -Force "$Root/$OutDir/examples" }
    # 启动器不带模型：只建空的模型库目录，模型作为独立内容放置 / 下载
    New-Item -ItemType Directory -Force -Path "$Root/$OutDir/model" | Out-Null
    if (Test-Path "$Root/model/README.md") {
        Copy-Item -Force "$Root/model/README.md" "$Root/$OutDir/model/README.md"
    }
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

switch ($Task) {
    'build' { dotnet build $Sln -c Debug }
    'run' { dotnet run --project $AppProj -c Debug }
    'test' { dotnet run --project $TestProj -c Debug -- "$Root/build/screenshots" }
    'publish' {
        New-Item -ItemType Directory -Force -Path "$Root/dist" | Out-Null
        Publish-Rid 'osx-arm64' 'dist/mac-arm64'
        Publish-Rid 'win-x64' 'dist/win-x64'
        Publish-Rid 'win-arm64' 'dist/win-arm64'
        Write-Host '==> 三个平台编译完成（启动器不含模型数据），见 dist\' -ForegroundColor Green
        Write-Host '    模型数据用 .\build.ps1 pack-models 单独打包，或放进 <安装目录>\model 后再启动'
    }
    'models' {
        Build-Models
    }
    'pack-models' {
        Build-Models
        Pack-Models
    }
    'clean' {
        Remove-Item -Recurse -Force "$Root/build", "$Root/dist" -ErrorAction SilentlyContinue
        Get-ChildItem "$Root/src" -Recurse -Directory -Include bin, obj |
            Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    }
    'doctor' { dotnet --info }
}
