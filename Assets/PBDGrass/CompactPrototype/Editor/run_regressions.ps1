param(
    [string]$ValidationProject=(Join-Path $env:TEMP 'codex-xpbd-near-unity-validation'),
    [string]$OutputRoot=(Join-Path $env:TEMP 'codex-compact-regressions'),
    [string]$UnityExe='C:\Program Files\Unity\Hub\Editor\2022.3.62f2c1\Editor\Unity.exe'
)
$ErrorActionPreference='Stop'
# Use only an independent validation checkout prepared from Assets/PBDGrass.
# Do not copy or inspect the original project's Library folder.
$jobs=@(
    @{name='play';method='GrassFinal.PBDGrass.Editor.GrassCompactPlayValidation.Run';env='GRASS_COMPACT_OUTPUT';report='PlayMode.json'},
    @{name='combined';method='GrassFinal.PBDGrass.Editor.GrassCombinedBaselineValidation.Run';env='GRASS_COMBINED_OUTPUT';report='Results.json'},
    @{name='wind';method='GrassFinal.PBDGrass.Editor.GrassResponseValidation.Run';env='GRASS_RESPONSE_OUTPUT';report='Results.json'},
    @{name='plastic';method='GrassFinal.PBDGrass.Editor.BakedGrassHandoffValidation.Run';env='BAKED_HANDOFF_OUTPUT';report='Results.json'},
    @{name='original-middle';method='GrassFinal.PBDGrass.Editor.GrassMidrangeValidation.Run';env='GRASS_MIDDLE_OUTPUT';report='Results.json'}
)
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
foreach($job in $jobs) {
    $folder=Join-Path $OutputRoot $job.name
    [Environment]::SetEnvironmentVariable($job.env,$folder,'Process')
    $env:PBD_TIERED_HANDOFF_TEST=if($job.name -eq 'plastic'){'1'}else{''}
    $log=Join-Path $OutputRoot ($job.name+'.log')
    $process=Start-Process -FilePath $UnityExe -ArgumentList @('-batchmode','-force-d3d11','-projectPath',$ValidationProject,'-executeMethod',$job.method,'-logFile',$log) -WindowStyle Hidden -PassThru
    Write-Output ("Started "+$job.name+" PID "+$process.Id)
    if(-not $process.WaitForExit(600000)) {throw ("Owned validation timed out; inspect "+$job.name+" PID "+$process.Id)}
    $report=Get-Content -LiteralPath (Join-Path $folder $job.report) -Raw | ConvertFrom-Json
    if($process.ExitCode -ne 0 -or -not $report.passed) {throw ("Validation failed: "+$job.name+" "+$report.error)}
    Write-Output ("PASS "+$job.name)
}
