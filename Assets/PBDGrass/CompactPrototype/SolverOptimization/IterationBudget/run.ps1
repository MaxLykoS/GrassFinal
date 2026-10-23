param([string]$Method='ContactGpuBenchmark.Run',[string]$Name='Performance',[int]$Substeps=0,[int]$Iterations=0,
    [string]$Cases='q8x3_fixedA;q8x2_fixedA;q8x1_fixedA;q6x1_fixedA;q4x2_fixedA;q4x1_fixedA;q4x1_fixedB;q4x2_fixedB;q6x1_fixedB;q8x1_fixedB;q8x2_fixedB;q8x3_fixedB')
$ErrorActionPreference='Stop'
$project='D:\CodexTemp\codex-grass-contact-profile'
$output=Join-Path $project ('IterationResults\'+$Name)
if(Test-Path -LiteralPath $output){throw "Use a new output name: $output"}
New-Item -ItemType Directory -Path $output -Force | Out-Null
$env:TEMP=Join-Path $project 'TempTools';$env:TMP=$env:TEMP;$env:UPM_CACHE_ROOT=Join-Path $project 'UpmCache'
$env:GRASS_SOLVE_SUBSTEPS=if($Substeps -gt 0){[string]$Substeps}else{''}
$env:GRASS_SOLVE_ITERATIONS=if($Iterations -gt 0){[string]$Iterations}else{''}
foreach($key in @('GRASS_PROFILE_OUTPUT','GRASS_ITERATION_OUTPUT','GRASS_COMBINED_OUTPUT','GRASS_LIFECYCLE_OUTPUT','GRASS_SHOWCASE_OUTPUT')) {
    [Environment]::SetEnvironmentVariable($key,$output,'Process')
}
$env:GRASS_SOLVER_CASES=$Cases;$env:GRASS_PROFILE_CASE=$Cases.Split(';')[0]
$unity='C:\Program Files\Unity\Hub\Editor\2022.3.62f2c1\Editor\Unity.exe'
$process=Start-Process -FilePath $unity -ArgumentList @('-batchmode','-force-d3d11','-projectPath',$project,
    '-executeMethod',$Method,'-logFile',(Join-Path $output 'Unity.log')) -WindowStyle Hidden -PassThru
Write-Output ("START "+$Name+" pid="+$process.Id)
$timer=[Diagnostics.Stopwatch]::StartNew()
while(-not $process.WaitForExit(1000)){if($timer.Elapsed.TotalSeconds -gt 900){throw "Timeout: $Name pid=$($process.Id)"}}
Write-Output ("EXIT "+$Name+" code="+$process.ExitCode)
Get-ChildItem -LiteralPath $output -Filter '*.json' -File | ForEach-Object {
    $report=Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
    [pscustomobject]@{File=$_.Name;Passed=$report.passed;Error=$report.error;Samples=$report.rows.Count;
        SolveMean=($report.rows.solveGpu|Measure-Object -Average).Average}
} | ConvertTo-Json -Depth 2
if($process.ExitCode -ne 0){exit $process.ExitCode}
