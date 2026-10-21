param([string]$Stage='optimized',[string]$Project='D:\CodexTemp\codex-grass-contact-profile')
$ErrorActionPreference='Stop'
$unity='C:\Program Files\Unity\Hub\Editor\2022.3.62f2c1\Editor\Unity.exe'
$python='C:\Users\pc\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
$env:TEMP=Join-Path $Project 'TempTools';$env:TMP=$env:TEMP
$env:UPM_CACHE_ROOT=Join-Path $Project 'UpmCache'
$output=Join-Path $Project 'CpuResults'
$env:GRASS_PROFILE_OUTPUT=$output
$env:GRASS_PROFILE_PROJECT=$Project
function Invoke-Check([string]$Method,[string]$Name) {
    $log=Join-Path $output ($Name+'.log')
    $process=Start-Process -FilePath $unity -ArgumentList @('-batchmode','-force-d3d11','-projectPath',$Project,'-executeMethod',$Method,'-logFile',$log) -WindowStyle Hidden -PassThru
    Write-Output "$Name PID $($process.Id)"
    while(-not $process.WaitForExit(30000)) { Write-Output "$Name running" }
    if($process.ExitCode -ne 0) {throw "$Name exit $($process.ExitCode); inspect $log"}
    Write-Output "$Name finished"
}
if($Stage -eq 'parity') {
    & $python (Join-Path $PSScriptRoot 'prepare_cpu.py') parity
    $env:GRASS_CPU_OUTPUT=Join-Path $output 'ParityFinal'
    Invoke-Check 'CpuParity.Run' 'ParityFinal'
} elseif($Stage -eq 'optimized') {
    & $python (Join-Path $PSScriptRoot 'prepare_cpu.py') optimized
    foreach($caseName in @('wide_elevated_repeat_after','small_elevated_after','wide_moving_after','wide_overview_after','wide_no_contact_after')) {
        $env:GRASS_PROFILE_CASE=$caseName
        Invoke-Check 'ContactGpuBenchmark.Run' $caseName
    }
    $env:GRASS_REALTIME_CASE='wide_editor_after'
    Invoke-Check 'ContactRealtimeEditor.Run' 'wide_editor_after'
    $env:GRASS_SHOWCASE_OUTPUT=Join-Path $output 'Showcase'
    Invoke-Check 'GrassFinal.PBDGrass.Editor.GrassShowcaseValidation.Run' 'Showcase'
} elseif($Stage -eq 'final') {
    & $python (Join-Path $PSScriptRoot 'prepare_cpu.py') parity
    $env:GRASS_CPU_OUTPUT=Join-Path $output 'ParityAccepted'
    Invoke-Check 'CpuParity.Run' 'ParityAccepted'
    foreach($caseName in @('wide_elevated_final','wide_moving_final','small_elevated_final')) {
        $env:GRASS_PROFILE_CASE=$caseName
        Invoke-Check 'ContactGpuBenchmark.Run' $caseName
    }
} elseif($Stage -eq 'reference') {
    try {
        & $python (Join-Path $PSScriptRoot 'prepare_cpu.py') reference
        foreach($caseName in @('wide_elevated_before','wide_moving_before')) {
            $env:GRASS_PROFILE_CASE=$caseName
            Invoke-Check 'ContactGpuBenchmark.Run' $caseName
        }
    } finally { & $python (Join-Path $PSScriptRoot 'prepare_cpu.py') optimized }
} elseif($Stage -eq 'combined') {
    $env:GRASS_COMBINED_OUTPUT=Join-Path $output 'Combined'
    Invoke-Check 'GrassFinal.PBDGrass.Editor.GrassCombinedBaselineValidation.Run' 'Combined'
} elseif($Stage -eq 'player') {
    $env:GRASS_PROFILE_PLAYER=Join-Path $Project 'CpuPlayer/ContactProfile.exe'
    Invoke-Check 'BuildContactPlayer.Run' 'PlayerBuild'
    $env:GRASS_REALTIME_CASE='wide_player_after'
    $process=Start-Process -FilePath $env:GRASS_PROFILE_PLAYER -ArgumentList @('-batchmode','-force-d3d11','-screen-width','1600','-screen-height','900','-logFile',(Join-Path $output 'wide_player_after.log')) -WindowStyle Hidden -PassThru
    while(-not $process.WaitForExit(30000)) { Write-Output 'Player running' }
    if($process.ExitCode -ne 0) {throw 'Player failed'}
} else {throw 'Unknown stage'}
