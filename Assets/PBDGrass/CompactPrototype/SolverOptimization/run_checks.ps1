param([string]$Project='D:\CodexTemp\codex-grass-contact-profile',[string]$Output='')
$ErrorActionPreference='Stop'
$unity='C:\Program Files\Unity\Hub\Editor\2022.3.62f2c1\Editor\Unity.exe'
if([string]::IsNullOrEmpty($Output)){$Output=Join-Path $Project 'SolverOptimizationResults'}
$env:TEMP=Join-Path $Project 'TempTools'; $env:TMP=$env:TEMP
$env:UPM_CACHE_ROOT=Join-Path $Project 'UpmCache'
$jobs=@(
    @{name='FinalParity';method='ContactSolverParity.Run';env='GRASS_SOLVER_PARITY_OUTPUT'},
    @{name='Compact';method='GrassFinal.PBDGrass.Editor.GrassCompactValidation.Run';env='GRASS_COMPACT_OUTPUT'},
    @{name='Lifecycle';method='GrassFinal.PBDGrass.Editor.GrassContactLifecycleValidation.Run';env='GRASS_LIFECYCLE_OUTPUT'},
    @{name='Combined';method='GrassFinal.PBDGrass.Editor.GrassCombinedBaselineValidation.Run';env='GRASS_COMBINED_OUTPUT'},
    @{name='Showcase';method='GrassFinal.PBDGrass.Editor.GrassShowcaseValidation.Run';env='GRASS_SHOWCASE_OUTPUT'}
)
foreach($job in $jobs) {
    $folder=Join-Path $output $job.name
    if(Test-Path -LiteralPath (Join-Path $folder 'Results.json')) {throw "Result already exists: $folder"}
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    [Environment]::SetEnvironmentVariable($job.env,$folder,'Process')
    $process=Start-Process -FilePath $unity -ArgumentList @('-batchmode','-force-d3d11','-projectPath',$Project,
        '-executeMethod',$job.method,'-logFile',(Join-Path $output ($job.name+'.log'))) -WindowStyle Hidden -PassThru
    Write-Output ("START "+$job.name+" pid="+$process.Id)
    $timer=[Diagnostics.Stopwatch]::StartNew()
    while(-not $process.WaitForExit(1000)) {if($timer.Elapsed.TotalSeconds -gt 600) {throw "Timeout: $($job.name) pid=$($process.Id)"}}
    $report=Get-Content -LiteralPath (Join-Path $folder 'Results.json') -Raw | ConvertFrom-Json
    if(-not $report.passed -or $process.ExitCode -ne 0) {throw "Failed: $($job.name) $($report.error)"}
    Write-Output ("PASS "+$job.name)
}
