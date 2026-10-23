param([Parameter(Mandatory=$true)][string]$Output,
      [string]$Project='D:\CodexTemp\codex-grass-contact-profile')
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $Output){throw "Use a new output directory: $Output"}
New-Item -ItemType Directory -Path $Output | Out-Null
$env:TEMP=Join-Path $Project 'TempTools'; $env:TMP=$env:TEMP
$env:UPM_CACHE_ROOT=Join-Path $Project 'UpmCache'
$env:GRASS_PROFILE_OUTPUT=$Output; $env:GRASS_PROFILE_CASE='reference_fixedA'
$env:GRASS_PROFILE_SUITE=''; $env:GRASS_REALTIME_CASE=''
$env:GRASS_SOLVER_CASES='reference_fixedA;optimized_fixedA;optimized_fixedB;reference_fixedB;reference_lifecycleA;optimized_lifecycleA;optimized_lifecycleB;reference_lifecycleB'
$p=Start-Process -FilePath 'C:\Program Files\Unity\Hub\Editor\2022.3.62f2c1\Editor\Unity.exe' -ArgumentList @(
    '-batchmode','-force-d3d11','-projectPath',$Project,'-executeMethod','ContactGpuBenchmark.Run',
    '-logFile',(Join-Path $Output 'Unity.log')) -WindowStyle Hidden -PassThru
Write-Output ("Started benchmark pid="+$p.Id)
$timer=[Diagnostics.Stopwatch]::StartNew()
while(-not $p.WaitForExit(1000)){if($timer.Elapsed.TotalSeconds -gt 600){throw "Timeout: pid=$($p.Id). Inspect saved reports and shutdown log."}}
if($p.ExitCode -ne 0){throw "Unity exited with $($p.ExitCode)"}
foreach($name in $env:GRASS_SOLVER_CASES.Split(';')){
    $r=Get-Content -LiteralPath (Join-Path $Output ($name+'.json')) -Raw | ConvertFrom-Json
    if(-not $r.passed -or $r.failed -or $r.dropped){throw "Failed: $name"}
    Write-Output ("PASS "+$name)
}
