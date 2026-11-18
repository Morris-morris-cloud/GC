param(
    [Parameter(Position = 0)]
    [ValidateSet('build','check','models','stamp','pack-models','pack-launcher',
                 'release-verify','run','test','update-test','clean','doctor','dist')]
    [string]$Task = 'build',
    [string]$Version = ''
)

$ErrorActionPreference = 'Stop'
$script:BuildRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:AppProject = 'GC-BOOT/GC-BOOT.csproj'
$script:PreviewProject = 'model/_engine/PreviewGen/PreviewGen.csproj'
$script:StampProject = 'model/_engine/VersionStamp/VersionStamp.csproj'
$script:TestProject = 'GC/src/test/dotnet/GC.Tests/GC.Tests.csproj'
$script:Solution = 'GC.sln'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'

Push-Location $script:BuildRoot
try {
    $props = [xml](Get-Content 'Directory.Build.props' -Raw)
    $versionNode = $props.SelectSingleNode('/Project/PropertyGroup/Version')
    if (-not $Version) { $Version = $versionNode.InnerText }
    if (-not $Version) { $Version = '1.0.0' }
    $script:BuildVersion = $Version

    # release-verify 的 -Version 是「要核对的 tag」，不是改版本号：绝不能改写清单
    if ($PSBoundParameters.ContainsKey('Version') -and $Task -ne 'release-verify') {
        if ($null -eq $versionNode) { throw 'Directory.Build.props 中找不到 Version 节点。' }
        $versionNode.InnerText = $Version
        $props.Save((Join-Path $script:BuildRoot 'Directory.Build.props'))
        Write-Host "==> 版本号设为 $Version"
    }

    New-Item -ItemType Directory -Force -Path 'build/logs' | Out-Null
    . (Join-Path $PSScriptRoot 'lib/Progress.ps1')
    . (Join-Path $PSScriptRoot 'lib/Build-Funcs.ps1')
    . (Join-Path $PSScriptRoot 'Build-Check.ps1')
    . (Join-Path $PSScriptRoot 'Build-Tools.ps1')

    switch ($Task) {
        'build'          { Invoke-FullBuild }
        'check'          { Invoke-BuildCheck }
        'release-verify' { Invoke-ReleaseVerify -Tag $Version }
        default          { Invoke-BuildTool $Task }
    }
}
finally {
    Pop-Location
}