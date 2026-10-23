param([string]$Method='GrassFinal.PBDGrass.Editor.GrassLowBudgetBaker.Bake',[string]$Name='Bake',
    [string]$Cases='legacy_fixedA;matched_fixedA;matched_fixedB;legacy_fixedB')
$ErrorActionPreference='Stop'
$project='D:\CodexTemp\codex-grass-contact-profile'
$output=Join-Path $project ('LowBudgetResults\'+$Name)
if(Test-Path -LiteralPath $output){throw "Use new output name: $output"}
New-Item -ItemType Directory -Path $output -Force | Out-Null
$env:TEMP=Join-Path $project 'TempTools';$env:TMP=$env:TEMP;$env:UPM_CACHE_ROOT=Join-Path $project 'UpmCache'
foreach($key in @('GRASS_LOW_BUDGET_OUTPUT','GRASS_RESPONSE_OUTPUT','GRASS_COMBINED_OUTPUT','GRASS_LIFECYCLE_OUTPUT','BAKED_HANDOFF_OUTPUT','GRASS_PROFILE_OUTPUT')){
    [Environment]::SetEnvironmentVariable($key,$output,'Process')
}
$env:GRASS_PROFILE_CASE=$Cases.Split(';')[0];$env:GRASS_SOLVER_CASES=$Cases
$unity='C:\Program Files\Unity\Hub\Editor\2022.3.62f2c1\Editor\Unity.exe'
$p=Start-Process -FilePath $unity -ArgumentList @('-batchmode','-force-d3d11','-projectPath',$project,'-executeMethod',$Method,'-logFile',(Join-Path $output 'Unity.log')) -WindowStyle Hidden -PassThru
Write-Output ("START "+$Name+" pid="+$p.Id)
$timer=[Diagnostics.Stopwatch]::StartNew()
while(-not $p.WaitForExit(1000)){if($timer.Elapsed.TotalSeconds -gt 900){throw "Timeout: $Name pid=$($p.Id)"}}
Write-Output ("EXIT "+$Name+" code="+$p.ExitCode)
Get-ChildItem -LiteralPath $output -Filter '*results.json' -File | ForEach-Object {$r=Get-Content -LiteralPath $_.FullName -Raw|ConvertFrom-Json;$r|Select-Object passed,error|ConvertTo-Json}
if($p.ExitCode -ne 0){exit $p.ExitCode}
