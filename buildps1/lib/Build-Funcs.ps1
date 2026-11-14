function Invoke-LoggedDotNet {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string[]]$Arguments
    )
    $logPath = Join-Path $script:BuildRoot "build/logs/$Name.log"
    & dotnet @Arguments *> $logPath
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        Get-Content $logPath | Write-Host
        throw "dotnet $($Arguments -join ' ') 失败，退出码 $exitCode；日志：$logPath"
    }
}

function Invoke-VersionStamp {
    Write-Host '==> 自动计算模型版本并生成 version.json'
    Invoke-LoggedDotNet -Name 'stamp' -Arguments @(
        'run', '--project', $script:StampProject, '-c', 'Release', '--',
        $script:BuildRoot, $script:BuildVersion
    )
}

function Build-Models {
    Write-Host '==> 编译模型功能程序集（源码位于各模型目录的 src/）'
    Get-ChildItem (Join-Path $script:BuildRoot 'model') -Directory | ForEach-Object {
        $modelId = $_.Name
        $sourceDir = Join-Path $_.FullName 'src'
        if (-not (Test-Path $sourceDir)) { return }
        $project = Get-ChildItem $sourceDir -Filter '*.csproj' | Select-Object -First 1
        if ($null -eq $project) { return }

        Invoke-LoggedDotNet -Name "model-$modelId" -Arguments @('build', $project.FullName, '-c', 'Release')
        $packageDir = Join-Path $_.FullName "$modelId.model"
        $outputDir = Join-Path $sourceDir 'bin/Release/net8.0'
        if (Test-Path $packageDir) {
            Get-ChildItem $outputDir -Filter '*.dll' -ErrorAction SilentlyContinue | Where-Object Name -ne 'GC.ModelSdk.dll' | ForEach-Object {
                Copy-Item $_.FullName (Join-Path $packageDir $_.Name) -Force
                Write-Host "    -> model/$modelId/$modelId.model/$($_.Name)"
            }
        }
    }

    Write-Host '==> 用模型自己的渲染器生成封面'
    Invoke-LoggedDotNet -Name 'preview' -Arguments @('run', '--project', $script:PreviewProject, '-c', 'Release', '--', $script:BuildRoot)
    Invoke-VersionStamp
}

function Prepare-RuntimeLayout {
    param([Parameter(Mandatory)][string]$RelativePath)
    $destination = Join-Path $script:BuildRoot $RelativePath
    if (-not (Test-Path $destination)) { return }

    Remove-Item (Join-Path $destination 'model'), (Join-Path $destination 'examples') -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path (Join-Path $destination '.GC/model'), (Join-Path $destination '.GC/cache'), (Join-Path $destination '.GC/update') | Out-Null
    Set-Content -Path (Join-Path $destination '.GC/model/README.txt') -Encoding UTF8 -Value @'
模型库目录（.GC/model/）
启动时程序会读取 GitCode 仓库的 version.json，把最新版模型下载到这里。
离线使用也可以手动放入 <模型id>/<模型id>.json + <模型id>/<模型id>.model/。

运行目录：
  <可执行文件>
  .GC/settings.json
    .GC/model/      本目录（下载的模型）
    .GC/cache/      下载暂存
    .GC/update/     待下次启动应用的启动器更新
'@
    $settingsPath = Join-Path $destination '.GC/settings.json'
    if (-not (Test-Path $settingsPath)) {
        Set-Content -Path $settingsPath -Encoding UTF8 -Value @'
{
  "language": "auto",
  "theme": "light",
  "renderFps": 30,
  "memoryMb": 2048,
  "autoUpdateOnStartup": true,
    "updateRepository": "https://gitcode.com/Morris_1/GC",
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
    }
    Write-Host "    运行目录 -> $RelativePath/.GC"
}

function Publish-Rid {
    param(
        [Parameter(Mandatory)][string]$Rid,
        [Parameter(Mandatory)][string]$Output,
        [bool]$SingleFile = $true
    )
    $outputPath = Join-Path $script:BuildRoot $Output
    Remove-Item $outputPath -Recurse -Force -ErrorAction SilentlyContinue
    $singleValue = $SingleFile.ToString().ToLowerInvariant()
    Write-Host "==> 编译 $Rid (single-file=$singleValue) -> $Output"
    Invoke-LoggedDotNet -Name "publish-$($Rid -replace '-', '')" -Arguments @(
        'publish', $script:AppProject, '-c', 'Release', '-r', $Rid,
        '--self-contained', 'true', "-p:Version=$script:BuildVersion", '-p:DebugType=none',
        "-p:PublishSingleFile=$singleValue", "-p:IncludeNativeLibrariesForSelfExtract=$singleValue",
        '-p:PublishTrimmed=false', '-o', $outputPath
    )
    Remove-Item (Join-Path $outputPath 'model'), (Join-Path $outputPath 'examples') -Recurse -Force -ErrorAction SilentlyContinue
}

function Pack-Models {
    $zipPath = Join-Path $script:BuildRoot "dist/models-$script:BuildVersion.zip"
    Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
    Write-Host "==> 打包模型数据 -> dist/models-$script:BuildVersion.zip"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $modelRoot = Join-Path $script:BuildRoot 'model'
    $archive = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        Get-ChildItem $modelRoot -File -Recurse | Where-Object {
            $relativePath = $_.FullName.Substring($modelRoot.Length).TrimStart([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar).Replace('\', '/')
            $_.Name -ne '.DS_Store' -and
                $_.Extension -ne '.user' -and
                $relativePath -notmatch '(^|/)(bin|obj)/'
        } | ForEach-Object {
            $relativePath = $_.FullName.Substring($modelRoot.Length).TrimStart([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar).Replace('\', '/')
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive, $_.FullName, "model/$relativePath", [System.IO.Compression.CompressionLevel]::Optimal
            ) | Out-Null
        }
    }
    finally { $archive.Dispose() }
    Get-Item $zipPath | Select-Object Name, Length
}

function Pack-Launcher {
    $dist = Join-Path $script:BuildRoot 'dist'
    Write-Host "==> 生成启动器产物（GC-$script:BuildVersion-<平台>）"

    foreach ($platform in @(@{ Rid = 'win-x64'; Label = 'win-amd64' }, @{ Rid = 'win-arm64'; Label = 'win-arm64' })) {
        $sourceDir = Join-Path $dist $platform.Rid
        if (-not (Test-Path $sourceDir)) { continue }
        $executable = Get-ChildItem $sourceDir -Filter '*.exe' | Select-Object -First 1
        if ($null -eq $executable) { continue }
        Copy-Item $executable.FullName (Join-Path $dist "GC-$script:BuildVersion-$($platform.Label).exe") -Force
    }

    $macApp = Join-Path $dist 'mac-arm64/GC.app'
    if (Test-Path $macApp) {
        $finalApp = Join-Path $dist "GC-$script:BuildVersion-mac-arm64.app"
        Remove-Item $finalApp -Recurse -Force -ErrorAction SilentlyContinue
        Move-Item $macApp $finalApp
        $binary = Join-Path $finalApp 'Contents/MacOS/GC'
        if ((Test-Path $binary) -and (Get-Command chmod -ErrorAction SilentlyContinue)) { & chmod +x $binary }
        $plist = Join-Path $finalApp 'Contents/Info.plist'
        if ((Test-Path $plist) -and (Test-Path '/usr/libexec/PlistBuddy')) {
            & /usr/libexec/PlistBuddy -c 'Set :CFBundleName Morris GC' $plist 2>$null
            & /usr/libexec/PlistBuddy -c 'Set :CFBundleDisplayName Morris GC' $plist 2>$null
        }
        if (Get-Command xattr -ErrorAction SilentlyContinue) { & xattr -cr $finalApp 2>$null }
    }

    Get-ChildItem $dist | Where-Object { $_.Name -like "GC-$script:BuildVersion-*.exe" -or $_.Name -like "GC-$script:BuildVersion-*.app" } | ForEach-Object {
        $zipPath = Join-Path $dist ($_.BaseName + '.zip')
        Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
        if ($_.PSIsContainer) {
            Remove-Item (Join-Path $_.FullName 'Contents/MacOS/.GC') -Recurse -Force -ErrorAction SilentlyContinue
        }
        if ($_.PSIsContainer -and (Get-Command ditto -ErrorAction SilentlyContinue)) {
            & ditto -c -k --sequesterRsrc --keepParent $_.FullName $zipPath
            if ($LASTEXITCODE -ne 0) { throw "ditto 压缩失败：$($_.Name)" }
        }
        else {
            Compress-Archive -Path $_.FullName -DestinationPath $zipPath -CompressionLevel Optimal
        }
    }

    Remove-Item (Join-Path $dist "GC-$script:BuildVersion-*.exe") -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $dist "GC-$script:BuildVersion-*.app") -Recurse -Force -ErrorAction SilentlyContinue
    Remove-IntermediateDirectories
    Invoke-VersionStamp
    Write-Host '==> dist 可发布 zip：'
    Get-ChildItem $dist -Filter "GC-$script:BuildVersion-*.zip" | Select-Object Name, Length
}

function Remove-IntermediateDirectories {
    foreach ($name in @('win-x64', 'win-arm64', 'mac-arm64')) {
        Remove-Item (Join-Path $script:BuildRoot "dist/$name") -Recurse -Force -ErrorAction SilentlyContinue
    }
    Get-ChildItem (Join-Path $script:BuildRoot 'dist') -Filter '.DS_Store' -Recurse -Force -ErrorAction SilentlyContinue | Remove-Item -Force
}

function New-MacAppBundle {
    $dist = Join-Path $script:BuildRoot 'dist/mac-arm64'
    $app = Join-Path $dist 'GC.app'
    Remove-Item $app -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path (Join-Path $app 'Contents/MacOS'), (Join-Path $app 'Contents/Resources') | Out-Null
    $plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>GC</string>
<key>CFBundleDisplayName</key><string>GC</string>
<key>CFBundleIdentifier</key><string>cn.neorf.GC</string>
<key>CFBundleVersion</key><string>$script:BuildVersion</string>
<key>CFBundleShortVersionString</key><string>$script:BuildVersion</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleExecutable</key><string>GC</string>
<key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
<key>LSMinimumSystemVersion</key><string>11.0</string>
<key>NSHighResolutionCapable</key><true/>
</dict></plist>
"@
    Set-Content -Path (Join-Path $app 'Contents/Info.plist') -Encoding UTF8 -Value $plist
    Get-ChildItem $dist -Force | Where-Object Name -notin @('GC.app', '.DS_Store') | Move-Item -Destination (Join-Path $app 'Contents/MacOS')
    Remove-Item (Join-Path $app 'Contents/MacOS/.GC') -Recurse -Force -ErrorAction SilentlyContinue
    $binary = Join-Path $app 'Contents/MacOS/GC'
    if ((Test-Path $binary) -and (Get-Command chmod -ErrorAction SilentlyContinue)) { & chmod +x $binary }

    $iconSource = Join-Path $script:BuildRoot 'GC/src/Assets/icon-1024.png'
    if ((Test-Path $iconSource) -and (Get-Command iconutil -ErrorAction SilentlyContinue) -and (Get-Command sips -ErrorAction SilentlyContinue)) {
        $iconset = Join-Path $dist 'AppIcon.iconset'
        Remove-Item $iconset -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Force -Path $iconset | Out-Null
        foreach ($size in @(16, 32, 64, 128, 256, 512)) {
            & sips -z $size $size $iconSource --out (Join-Path $iconset "icon_${size}x${size}.png") *> $null
            $doubleSize = $size * 2
            & sips -z $doubleSize $doubleSize $iconSource --out (Join-Path $iconset "icon_${size}x${size}@2x.png") *> $null
        }
        $iconFile = Join-Path $app 'Contents/Resources/AppIcon.icns'
        & iconutil -c icns $iconset -o $iconFile
        if ($LASTEXITCODE -eq 0) {
            $infoPlist = Join-Path $app 'Contents/Info.plist'
            if (Test-Path '/usr/libexec/PlistBuddy') {
                & /usr/libexec/PlistBuddy -c 'Add :CFBundleIconFile string AppIcon' $infoPlist 2>$null
            }
        }
        Remove-Item $iconset -Recurse -Force -ErrorAction SilentlyContinue
    }
    Write-Host "==> 已打包 $app"
}

function Invoke-FullBuild {
    Initialize-BuildProgress @('清理旧产物', '版本戳', '编译 Mac', '编译 Win-x64', '编译 Win-arm64', '运行骨架', '打包 zip', '收尾')
    try {
        Start-BuildStep '清理旧产物'
        Remove-Item (Join-Path $script:BuildRoot 'dist') -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Force -Path (Join-Path $script:BuildRoot 'dist') | Out-Null
        Complete-BuildStep

        Start-BuildStep '版本戳'; Invoke-VersionStamp; Complete-BuildStep
        Start-BuildStep '编译 Mac'; Publish-Rid 'osx-arm64' 'dist/mac-arm64' $false; Complete-BuildStep
        Start-BuildStep '编译 Win-x64'; Publish-Rid 'win-x64' 'dist/win-x64' $true; Complete-BuildStep
        Start-BuildStep '编译 Win-arm64'; Publish-Rid 'win-arm64' 'dist/win-arm64' $true; Complete-BuildStep

        Start-BuildStep '运行骨架'
        New-MacAppBundle
        Prepare-RuntimeLayout 'dist/win-x64'
        Prepare-RuntimeLayout 'dist/win-arm64'
        Prepare-RuntimeLayout 'dist/mac-arm64/GC.app/Contents/MacOS'
        Complete-BuildStep

        Start-BuildStep '打包 zip'; Pack-Launcher; Complete-BuildStep
        Start-BuildStep '收尾'; Complete-BuildStep
        Write-Host '==> 完成，产物：'
        Get-ChildItem (Join-Path $script:BuildRoot 'dist') -Filter "GC-$script:BuildVersion-*.zip" | Select-Object Name, Length
    }
    finally { Complete-BuildProgress }
}