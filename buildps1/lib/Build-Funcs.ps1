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

function Invoke-ManifestSign {
    $key = Join-Path $script:BuildRoot '.signing/gc-signing.pem'
    if (-not (Test-Path $key)) {
        Write-Host '!! 未找到签名私钥 .signing/gc-signing.pem，version.json 未签名（客户端会拒绝更新）'
        return
    }
    # 与 buildsh/lib/build_funcs.sh 保持一致：当前生效的密钥是 gc-2026-12。
    # gc-2026-11 的私钥没有留存，用它签出来的清单客户端永远验不过。
    $keyId = if ($env:GC_SIGNING_KEY_ID) { $env:GC_SIGNING_KEY_ID } else { 'gc-2026-12' }
    Write-Host "==> 签名 version.json ($keyId)"
    Invoke-LoggedDotNet -Name 'sign' -Arguments @(
        'run', '--project', (Join-Path $script:BuildRoot 'GC-SAVEDOG/GC-SAVEDOG.csproj'),
        '-c', 'Release', '--', 'sign',
        '--manifest', (Join-Path $script:BuildRoot 'version.json'),
        '--key', $key, '--key-id', $keyId
    )
    Copy-Item (Join-Path $script:BuildRoot 'version.json') (Join-Path $script:BuildRoot 'model/version.json') -Force
}

function Invoke-VersionStamp {
    param([switch]$LauncherOnly, [switch]$NoSign)
    if ($LauncherOnly) {
        Write-Host '==> 刷新启动器包信息（sha256 / 体积 / dist 路径）'
        Invoke-LoggedDotNet -Name 'stamp' -Arguments @(
            'run', '--project', $script:StampProject, '-c', 'Release', '--',
            $script:BuildRoot, $script:BuildVersion, '--launcher-only'
        )
    }
    else {
        Write-Host '==> 自动计算模型版本并生成 version.json'
        Invoke-LoggedDotNet -Name 'stamp' -Arguments @(
            'run', '--project', $script:StampProject, '-c', 'Release', '--',
            $script:BuildRoot, $script:BuildVersion
        )
    }
    # 完整 build 末尾 Pack-Launcher 会统一签一次（末次签名）；中间这次是浪费的。
    # 单独的 stamp / models 工具没有后续打包，仍走默认签名。
    if ($NoSign) {
        Write-Host '    （跳过签名，打包结束后统一签一次）'
    }
    else {
        Invoke-ManifestSign
    }
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

    # 与 buildsh/lib/build_funcs.sh 一致：发布目录只放程序本体，不预置 .GC。
    # .GC（config / model / cache / update / safedog / logs）一律在首次启动时
    # 由 AppPaths.EnsureCreated + RuntimeLayout 创建，压缩包里只有 GC 本体。
    Remove-Item (Join-Path $destination 'model'), (Join-Path $destination 'examples'), (Join-Path $destination '.GC') -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "    运行目录 -> $RelativePath（首次启动后生成 .GC/：config model cache update safedog logs）"
}

function Prune-Package {
    param(
        [Parameter(Mandatory)][string]$Destination,
        [Parameter(Mandatory)][ValidateSet('win', 'linux')][string]$Platform
    )
    if (-not (Test-Path $Destination)) { return }
    $junk = @(
        'mscordaccore.dll', 'mscordbi.dll', 'web.config',
        'aspnetcorev2_inprocess.dll',
        'Microsoft.AspNetCore.Server.IIS.dll',
        'Microsoft.AspNetCore.Server.IISIntegration.dll',
        'Microsoft.AspNetCore.Server.HttpSys.dll',
        'Microsoft.DiaSymReader.Native.dll'
    )
    if ($Platform -eq 'win') {
        # 后端已编入启动器，ProjectReference 残留的独立 exe / runtimeconfig 没有用
        $junk += @('GC.UI.Server.exe', 'GC.UI.Server.dll', 'GC.UI.Server.deps.json', 'GC.UI.Server.runtimeconfig.json')
        $junk += @('Avalonia.X11.dll', 'Avalonia.Native.dll', 'Avalonia.FreeDesktop.dll', 'Tmds.DBus.Protocol.dll')
    }
    else {
        $junk += @('Avalonia.Win32.dll', 'Avalonia.Win32.Automation.dll')
    }
    foreach ($name in $junk) {
        Get-ChildItem $Destination -Filter $name -File -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
    }
    # mscordaccore*.dll / Microsoft.DiaSymReader.Native*.dll 是带版本号通配的
    Get-ChildItem $Destination -Filter 'mscordaccore*.dll' -File -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
    Get-ChildItem $Destination -Filter 'Microsoft.DiaSymReader.Native*.dll' -File -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
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
        "-p:EnableCompressionInSingleFile=$singleValue",
        '-p:PublishTrimmed=false', '-o', $outputPath
    )

    # 更新守护进程：与主程序同目录（裁剪 + 压缩单文件，70MB -> 14MB）
    # 裁剪的前提是 JSON 全部走源生成上下文（GC-SAVEDOG/SavedogJson.cs）。
    # EnableCompressionInSingleFile：守护进程不常驻，只在更新时跑一次，
    # 解压一次换来更小的包，对「只在更新时下载」的场景很划算。
    $savedogProj = Join-Path $script:BuildRoot 'GC-SAVEDOG/GC-SAVEDOG.csproj'
    if (Test-Path $savedogProj) {
        Write-Host "==> 编译 GC-SAVEDOG ($Rid)（裁剪 + 压缩单文件）"
        Invoke-LoggedDotNet -Name "savedog-$($Rid -replace '-', '')" -Arguments @(
            'publish', $savedogProj, '-c', 'Release', '-r', $Rid,
            '--self-contained', 'true', "-p:Version=$script:BuildVersion", '-p:DebugType=none',
            '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
            '-p:EnableCompressionInSingleFile=true',
            '-p:PublishTrimmed=true', '-p:TrimMode=full',
            '-p:JsonSerializerIsReflectionEnabledByDefault=false', '-o', $outputPath
        )
    }

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
    Write-Host "==> 生成启动器产物（GCL-$script:BuildVersion-<系统>-<架构>）"

    # 与 buildsh/lib/build_funcs.sh 的 pack_launcher 对齐：
    #   1) 包名统一 GCL- 前缀；
    #   2) zip 内只有一个顶层目录（GCL-<版本>-<平台>），不散落一屏 dll；
    #   3) 非本体文件（守护进程）收进 .GC/bin，安装目录根只留 GC 本体；
    #   4) 剔除调试 / 无关平台的组件（prune）。

    # --- Windows：启动器 + 守护进程 + 前端，整目录打进单顶层目录 ---
    foreach ($platform in @(@{ Rid = 'win-x64'; Label = 'win-amd64' }, @{ Rid = 'win-arm64'; Label = 'win-arm64' })) {
        $sourceDir = Join-Path $dist $platform.Label
        if (-not (Test-Path $sourceDir)) { $sourceDir = Join-Path $dist $platform.Rid }
        if (-not (Test-Path $sourceDir)) { continue }

        # 启动器固定是 GC.exe；后端与前端是可选的随包产物，不能当成启动器
        $exe = Join-Path $sourceDir 'GC.exe'
        if (-not (Test-Path $exe)) {
            $exe = (Get-ChildItem $sourceDir -Filter '*.exe' | Where-Object {
                $_.Name -notlike 'GC-SAVEDOG*' -and $_.Name -notlike 'GC.UI.Server*' -and $_.Name -notlike 'GC-UI*'
            } | Select-Object -First 1).FullName
        }
        if (-not $exe) { continue }

        $pkgName = "GCL-$script:BuildVersion-$($platform.Label)"
        $temp = Join-Path $dist ".winpkg-$($platform.Label)"
        $pkg = Join-Path $temp $pkgName
        Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Force -Path $pkg | Out-Null
        Get-ChildItem $sourceDir -Force | Where-Object { $_.Extension -ne '.zip' } | Copy-Item -Destination $pkg -Recurse -Force
        Prune-Package -Destination $pkg -Platform 'win'
        Move-Item (Join-Path $pkg (Split-Path $exe -Leaf)) (Join-Path $pkg "$pkgName.exe") -Force
        if (Get-Command chmod -ErrorAction SilentlyContinue) { & chmod +x (Join-Path $pkg "$pkgName.exe") 2>$null }

        $savedogExe = Join-Path $pkg 'GC-SAVEDOG.exe'
        if (Test-Path $savedogExe) {
            $binDir = Join-Path $pkg '.GC/bin'
            New-Item -ItemType Directory -Force -Path $binDir | Out-Null
            Move-Item $savedogExe (Join-Path $binDir 'GC-SAVEDOG.exe') -Force
        }
        else {
            Write-Host "    !! 缺少 GC-SAVEDOG.exe，该平台将退回旧脚本更新"
        }

        $zipPath = Join-Path $dist "$($platform.Label)/$pkgName.zip"
        Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
        Compress-Archive -Path $pkg -DestinationPath $zipPath -CompressionLevel Optimal
        Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "    -> dist/$($platform.Label)/$pkgName.zip"
    }

    # --- macOS：先得到真实 .app 目录（改名 + 同步 Info.plist + 守护进程入 .GC/bin） ---
    $macApp = Join-Path $dist 'mac-arm64/GC.app'
    if (Test-Path $macApp) {
        $finalApp = Join-Path $dist "GCL-$script:BuildVersion-mac-arm64.app"
        Remove-Item $finalApp -Recurse -Force -ErrorAction SilentlyContinue
        Move-Item $macApp $finalApp
        $binary = Join-Path $finalApp 'Contents/MacOS/GC'
        if ((Test-Path $binary) -and (Get-Command chmod -ErrorAction SilentlyContinue)) { & chmod +x $binary }
        $plist = Join-Path $finalApp 'Contents/Info.plist'
        if ((Test-Path $plist) -and (Test-Path '/usr/libexec/PlistBuddy')) {
            & /usr/libexec/PlistBuddy -c 'Set :CFBundleName Morris GC' $plist 2>$null
            & /usr/libexec/PlistBuddy -c 'Set :CFBundleDisplayName Morris GC' $plist 2>$null
        }
        # 非本体文件收进 .GC/bin（与 Windows 布局一致）：.GC 位置不变，仍在 .app/Contents/MacOS/
        $savedog = Join-Path $finalApp 'Contents/MacOS/GC-SAVEDOG'
        if (Test-Path $savedog) {
            $binDir = Join-Path $finalApp 'Contents/MacOS/.GC/bin'
            New-Item -ItemType Directory -Force -Path $binDir | Out-Null
            Move-Item $savedog (Join-Path $binDir 'GC-SAVEDOG') -Force
            if (Get-Command chmod -ErrorAction SilentlyContinue) { & chmod +x (Join-Path $binDir 'GC-SAVEDOG') 2>$null }
        }
        if (Get-Command xattr -ErrorAction SilentlyContinue) { & xattr -cr $finalApp 2>$null }
    }

    # --- 统一压缩：.app -> 同名 .zip，放进平台目录 ---
    Get-ChildItem $dist | Where-Object { $_.Name -like "GCL-$script:BuildVersion-*.app" } | ForEach-Object {
        $label = $_.BaseName -replace "^GCL-$([regex]::Escape($script:BuildVersion))-", ''
        $targetDir = Join-Path $dist $label
        New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
        $zipPath = Join-Path $targetDir ($_.BaseName + '.zip')
        Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
        if (Get-Command ditto -ErrorAction SilentlyContinue) {
            & ditto -c -k --sequesterRsrc --keepParent $_.FullName $zipPath
            if ($LASTEXITCODE -ne 0) { throw "ditto 压缩失败：$($_.Name)" }
        }
        else {
            Compress-Archive -Path $_.FullName -DestinationPath $zipPath -CompressionLevel Optimal
        }
        Write-Host "    -> dist/$label/$($_.BaseName).zip"
    }

    # 原始产物（.exe / .app 目录）不再保留：只发布 zip（移入暂存区，不做递归删除）
    $trash = Join-Path $script:BuildRoot 'build/trash'
    New-Item -ItemType Directory -Force -Path $trash | Out-Null
    Get-ChildItem $dist -Force | Where-Object {
        $_.Name -like "GCL-$script:BuildVersion-*.exe" -or $_.Name -like "GCL-$script:BuildVersion-*.app"
    } | ForEach-Object {
        Move-Item $_.FullName (Join-Path $trash "$($_.Name)-$([DateTime]::Now.Ticks)") -Force -ErrorAction SilentlyContinue
    }
    Remove-IntermediateDirectories
    Invoke-VersionStamp -LauncherOnly
    Write-Host '==> dist 可发布 zip：'
    Get-ChildItem $dist -Recurse -Filter "GCL-$script:BuildVersion-*.zip" | Select-Object FullName, Length
}

function Remove-IntermediateDirectories {
    # 平台目录里只留最终 zip（其它都是 publish 中间产物）
    foreach ($name in @('win-amd64', 'win-x64', 'win-arm64', 'mac-arm64', 'linux-arm64', 'linux-x64')) {
        $dir = Join-Path $script:BuildRoot "dist/$name"
        if (-not (Test-Path $dir)) { continue }
        Get-ChildItem $dir -Force | Where-Object { $_.Name -notlike "GCL-$script:BuildVersion-*.zip" } |
            Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
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
<key>CFBundleIdentifier</key><string>com.xhmcl.GC</string>
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

function Invoke-SavedogTests {
    $proj = Join-Path $script:BuildRoot 'GC/src/test/dotnet/GC.Savedog.Tests/GC.Savedog.Tests.csproj'
    Write-Host '==> 运行应用器测试（整目录替换 / .GC/bin 合并 / 回滚）'
    & dotnet run --project $proj -c Debug
    if ($LASTEXITCODE -ne 0) { throw "应用器测试失败，退出码 $LASTEXITCODE" }
}

function Invoke-ReleaseVerify {
    param([string]$Tag = '')
    $root = $script:BuildRoot
    $verifyTag = if ($Tag) { $Tag } else { $script:BuildVersion }
    $work = Join-Path $root 'build/release-verify'
    New-Item -ItemType Directory -Force -Path $work | Out-Null

    Write-Host "==> 核对 Release $verifyTag"
    $dump = Join-Path $work "$verifyTag.json"
    & python3 (Join-Path $root 'buildsh/lib/verify_release.py') `
        --tag $verifyTag `
        --manifest (Join-Path $root 'version.json') `
        --dump-manifest $dump
    if ($LASTEXITCODE -ne 0) { throw "发布一致性核对未通过（tag $verifyTag）" }

    Write-Host ''
    Write-Host '==> 校验 tag 清单签名'
    & dotnet run --project (Join-Path $root 'GC-SAVEDOG/GC-SAVEDOG.csproj') -c Release -- `
        verify --manifest $dump
    if ($LASTEXITCODE -ne 0) { throw '清单签名校验未通过，客户端会拒绝这次更新' }
    Write-Host '结论: 签名可信，可以发布 / 已经发布正确'
}

function Invoke-FullBuild {
    Initialize-BuildProgress @('清理旧产物', '版本戳', '编译 Mac', '编译 Win-x64', '编译 Win-arm64', '运行骨架', '打包 zip', '收尾')
    try {
        Start-BuildStep '清理旧产物'
        Remove-Item (Join-Path $script:BuildRoot 'dist') -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Force -Path (Join-Path $script:BuildRoot 'dist') | Out-Null
        Complete-BuildStep

        # 末次签名：这里只生成清单不签名，Pack-Launcher 打完包后统一签一次
        Start-BuildStep '版本戳'; Invoke-VersionStamp -NoSign; Complete-BuildStep
        # 与 buildsh 一致：启动器与守护进程都发布为压缩单文件，非本体文件在打包时收进 .GC/bin
        Start-BuildStep '编译 Mac'; Publish-Rid 'osx-arm64' 'dist/mac-arm64' $true; Complete-BuildStep
        Start-BuildStep '编译 Win-x64'; Publish-Rid 'win-x64' 'dist/win-amd64' $true; Complete-BuildStep
        Start-BuildStep '编译 Win-arm64'; Publish-Rid 'win-arm64' 'dist/win-arm64' $true; Complete-BuildStep

        Start-BuildStep '运行骨架'
        New-MacAppBundle
        Prepare-RuntimeLayout 'dist/win-amd64'
        Prepare-RuntimeLayout 'dist/win-arm64'
        Prepare-RuntimeLayout 'dist/mac-arm64/GC.app/Contents/MacOS'
        Complete-BuildStep

        Start-BuildStep '打包 zip'; Pack-Launcher; Complete-BuildStep
        Start-BuildStep '收尾'; Complete-BuildStep
        Write-Host '==> 完成，产物：'
        Get-ChildItem (Join-Path $script:BuildRoot 'dist') -Recurse -Filter "GCL-$script:BuildVersion-*.zip" | Select-Object FullName, Length
    }
    finally { Complete-BuildProgress }
}