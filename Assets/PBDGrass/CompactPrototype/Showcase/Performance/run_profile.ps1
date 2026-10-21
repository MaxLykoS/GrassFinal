param(
    [string[]]$Cases=@('small_elevated','wide_overview','small_overview','wide_no_contact','wide_natural','wide_elevated_repeat','wide_moving'),
    [string]$Project='D:\CodexTemp\codex-grass-contact-profile'
)
$ErrorActionPreference='Stop'
$unity='C:\Program Files\Unity\Hub\Editor\2022.3.62f2c1\Editor\Unity.exe'
$env:TEMP=Join-Path $Project 'TempTools'
$env:TMP=$env:TEMP
$env:UPM_CACHE_ROOT=Join-Path $Project 'UpmCache'
$env:GRASS_PROFILE_OUTPUT=Join-Path $Project 'Results'
foreach($caseName in $Cases) {
    $env:GRASS_PROFILE_CASE=$caseName
    $resultPath=Join-Path $env:GRASS_PROFILE_OUTPUT ($caseName+'.json')
    if(Test-Path -LiteralPath $resultPath) { throw "Refusing to overwrite an existing measurement: $resultPath" }
    $log=Join-Path $env:GRASS_PROFILE_OUTPUT ($caseName+'.log')
    $process=Start-Process -FilePath $unity -ArgumentList @('-batchmode','-force-d3d11','-projectPath',$Project,
        '-executeMethod','ContactGpuBenchmark.Run','-logFile',$log) -WindowStyle Hidden -PassThru
    Write-Output "$caseName started: PID $($process.Id)"
    while(-not $process.WaitForExit(30000)) {
        Write-Output "$caseName running"
    }
    if(-not (Test-Path -LiteralPath $resultPath)) { throw "$caseName produced no result; inspect $log" }
    $report=Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    if(-not $report.passed) { throw "$caseName failed: $($report.error)" }
    $cpuMean=($report.rows | Measure-Object totalCpu -Average).Average
    $gpuSolve=($report.rows | Measure-Object solveGpu -Average).Average
    Write-Output ("{0}: {1} samples, CPU {2:F3}ms, GPU solve {3:F3}ms" -f $caseName,$report.rows.Count,$cpuMean,$gpuSolve)
}
