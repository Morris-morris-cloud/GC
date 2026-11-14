$script:ProgressSteps = @()
$script:ProgressIndex = 0
$script:ProgressActivity = 'GC 构建'

function Initialize-BuildProgress {
    param([string[]]$Steps)
    $script:ProgressSteps = $Steps
    $script:ProgressIndex = 0
    Write-Progress -Activity $script:ProgressActivity -Status '准备' -PercentComplete 0
}

function Start-BuildStep {
    param([string]$Name)
    $script:ProgressIndex++
    $percent = [math]::Min(99, [int](($script:ProgressIndex - 1) * 100 / [math]::Max(1, $script:ProgressSteps.Count)))
    Write-Progress -Activity $script:ProgressActivity -Status $Name -PercentComplete $percent
    Write-Host "==> $Name"
}

function Complete-BuildStep {
    $percent = [int]($script:ProgressIndex * 100 / [math]::Max(1, $script:ProgressSteps.Count))
    Write-Progress -Activity $script:ProgressActivity -Status '进行中' -PercentComplete $percent
}

function Complete-BuildProgress {
    Write-Progress -Activity $script:ProgressActivity -Completed
}