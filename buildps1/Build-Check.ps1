function Invoke-BuildCheck {
    Initialize-BuildProgress @('编译检查', '模型自检', '离屏渲染', '产物检查')
    $failures = [System.Collections.Generic.List[string]]::new()
    try {
        Start-BuildStep '编译检查'
        try { Invoke-LoggedDotNet -Name 'check-build' -Arguments @('build', $script:Solution, '-c', 'Debug', '-v', 'quiet') }
        catch { $failures.Add('编译失败，见 build/logs/check-build.log'); Write-Host "!! $($failures[-1])" }
        Complete-BuildStep

        Start-BuildStep '模型自检'
        $previousSelfTest = $env:EMS_SELFTEST
        try {
            $env:EMS_SELFTEST = '1'
            Invoke-LoggedDotNet -Name 'check-selftest' -Arguments @('run', '--project', $script:AppProject, '-c', 'Debug', '--no-build')
            if (Select-String -Path 'build/logs/check-selftest.log' -Pattern 'FAIL' -Quiet) {
                $failures.Add('模型自检有失败项')
                Write-Host "!! $($failures[-1])"
            }
        }
        catch { $failures.Add('模型自检异常'); Write-Host "!! $($failures[-1])" }
        finally { $env:EMS_SELFTEST = $previousSelfTest }
        Complete-BuildStep

        Start-BuildStep '离屏渲染'
        try {
            New-Item -ItemType Directory -Force -Path 'build/screenshots' | Out-Null
            Invoke-LoggedDotNet -Name 'check-render' -Arguments @('run', '--project', $script:TestProject, '-c', 'Debug', '--no-build', '--', (Join-Path $script:BuildRoot 'build/screenshots'))
        }
        catch { $failures.Add('离屏渲染失败，见 build/logs/check-render.log'); Write-Host "!! $($failures[-1])" }
        Complete-BuildStep

        Start-BuildStep '产物检查'
        $packageCount = @(Get-ChildItem 'dist' -Filter "GC-$script:BuildVersion-*.zip" -ErrorAction SilentlyContinue).Count
        if ($packageCount -lt 3) {
            $failures.Add("产物不足 3 个（当前 $packageCount）")
            Write-Host "!! $($failures[-1])"
        }
        Complete-BuildStep
    }
    finally { Complete-BuildProgress }

    if ($failures.Count -eq 0) { Write-Host '==> 全部通过'; return }
    Write-Host '==> 有失败项'
    throw ($failures -join "`n")
}