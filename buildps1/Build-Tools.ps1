function Invoke-BuildTool {
    param([Parameter(Mandatory)][string]$Name)
    switch ($Name) {
        'models'       { Build-Models }
        'stamp'        { Invoke-VersionStamp }
        'pack-models'  { Build-Models; Pack-Models }
        'pack-launcher'{ Pack-Launcher }
        'run'          { & dotnet run --project $script:AppProject -c Debug; if ($LASTEXITCODE -ne 0) { throw "dotnet run 失败，退出码 $LASTEXITCODE" } }
        'test'         {
            & dotnet run --project $script:TestProject -c Debug -- (Join-Path $script:BuildRoot 'build/screenshots')
            if ($LASTEXITCODE -ne 0) { throw "测试失败，退出码 $LASTEXITCODE" }
            # 与 pr-check.yml 一致：应用器测试单独跑（它与启动器有同名类型，不能并入 GC.Tests）
            Invoke-SavedogTests
        }
        'update-test'  { & dotnet run --project $script:TestProject -c Debug -- --update-test $script:BuildRoot; if ($LASTEXITCODE -ne 0) { throw "更新测试失败，退出码 $LASTEXITCODE" } }
        'clean' {
            Remove-Item (Join-Path $script:BuildRoot 'build'), (Join-Path $script:BuildRoot 'dist') -Recurse -Force -ErrorAction SilentlyContinue
            foreach ($projectRoot in @('GC', 'GC-BOOT', 'GC-DRAW', 'model')) {
                $path = Join-Path $script:BuildRoot $projectRoot
                if (Test-Path $path) {
                    Get-ChildItem $path -Directory -Recurse -Force | Where-Object Name -in @('bin', 'obj') | Sort-Object FullName -Descending | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
                }
            }
            Write-Host '==> 已清理'
        }
        'doctor' { & dotnet --info | Select-Object -First 20; if ($LASTEXITCODE -ne 0) { throw "dotnet --info 失败，退出码 $LASTEXITCODE" } }
        'dist' {
            Write-Host '==> dist 产物：'
            Get-ChildItem (Join-Path $script:BuildRoot 'dist') -ErrorAction SilentlyContinue | Select-Object Name, Length
        }
        default { throw "未知任务：$Name" }
    }
}