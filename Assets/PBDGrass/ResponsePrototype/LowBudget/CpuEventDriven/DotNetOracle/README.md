# Pool CPU oracle evidence — 2026-10-23

The completed standalone run passed **24 cases / 240,000 ticks**. Every tick compared the new event-driven pool against the frozen pre-event implementation by active world-grid identity, lifecycle counts, and exact exit time. Capacity, unique grid/slot ownership, mapping consistency, and stable slots for retained grids were also checked.

[Results.json](Results.json) records all cases and source hashes. [PassSummary.txt](PassSummary.txt) preserves the complete PASS summary transcribed from the completed run's console output. Archiving did not rerun the tests. These files are correctness evidence, not CPU timing measurements.

Each case runs 10,000 ticks on 33×33 immutable grids. Capacities are 1, 16, 128, and 1024 grids; seeds are 1, 2, 7, 8, 31, and 32. Odd seeds use contact-driven mode, even seeds use fixed mode. Tests include moving/stationary focus, ranking changes, actor coverage changes, random `ForgetGrids`, oversubscription, and changed settle/fade settings.

The tests compile the actual Pool, Coverage, Query, and Settings source snapshots with **.NET SDK 7.0.101 / net7.0**, referencing the installed **Unity 2022.3.62f2c1 UnityEngine.CoreModule.dll**. Unity's managed vector and math implementations execute directly. No Unity Editor process, scene, shader, GPU resource, or GPU readback is involved. The DLL, build output, `bin/`, and `obj/` are not archived here.

`Program.cs.txt` includes a minimal world-layout type for satisfying the query API and for the separate coverage test. The 24 exact pool cases use static bucket queries, so they do not claim to validate Unity world streaming. The separate 1024-step coverage oracle tests static buckets plus a 64×64 ring using the copied CPU coordinate-replacement logic. `EditorStubs.cs.txt` supplies only compile-time Editor exit/JSON adapters for that optional test; the replay invokes its private CPU exercise method, not its Unity Editor entry point.

The exploratory repeated fixed/contact mode-switch comparison is **excluded** from the 24 exact cases. It found an old implementation bug: a previous release time can survive repeated switches and cause immediate expiry. The new implementation begins a fresh release interval when entering contact mode. GPU/world-state comparisons and actual Unity streaming tests are separate validations.

## Replay

Run these PowerShell commands from any directory. They create a new temporary project and leave the Unity project unchanged. The first run may restore the local .NET framework reference package. This can consume CPU; run separately from performance measurements.

```powershell
$oracleArchive = 'D:\UnityProject\GrassFinal\Assets\PBDGrass\ResponsePrototype\LowBudget\CpuEventDriven\DotNetOracle'
$oracleReplay = Join-Path 'D:\CodexTemp' ('grass-pool-oracle-replay-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $oracleReplay -ErrorAction Stop | Out-Null
Get-ChildItem -LiteralPath $oracleArchive -Filter '*.cs.txt' -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $oracleReplay $_.Name.Substring(0, $_.Name.Length - 4))
}
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net7.0</TargetFramework>
    <Nullable>disable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>C:/Program Files/Unity/Hub/Editor/2022.3.62f2c1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll</HintPath>
    </Reference>
  </ItemGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $oracleReplay 'PoolTest.csproj') -Encoding utf8

dotnet run --project (Join-Path $oracleReplay 'PoolTest.csproj') 2>&1 |
    Tee-Object -FilePath (Join-Path $oracleReplay 'PoolPass.log')
if ($LASTEXITCODE -ne 0) { throw 'Pool oracle failed.' }

# Optional independent coverage-union oracle, expected two PASS lines:
dotnet run --project (Join-Path $oracleReplay 'PoolTest.csproj') -- coverage 2>&1 |
    Tee-Object -FilePath (Join-Path $oracleReplay 'CoveragePass.log')
if ($LASTEXITCODE -ne 0) { throw 'Coverage oracle failed.' }
```

Expected pool output is the 24 lines in `PassSummary.txt`. The optional coverage output is `Coverage PASS False` and `Coverage PASS True`.
