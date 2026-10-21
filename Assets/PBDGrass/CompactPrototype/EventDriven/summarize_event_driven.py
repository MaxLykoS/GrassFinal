"""Package the accepted contact lifecycle run; never read the Unity Library."""
from pathlib import Path
import hashlib
import json
import math
import shutil
import statistics
import sys
import zipfile

output = Path(__file__).resolve().parent
source = output.parents[1]
root = source.parents[1]
results = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(
    "D:/CodexTemp/codex-grass-contact-profile/EventDrivenResults")


def read(relative):
    value = json.loads((results / relative).read_text(encoding="utf-8-sig"))
    assert value["passed"], (relative, value.get("error"))
    return value


def summarize(rows):
    metrics = ["solveGpu", "displayGpu", "selectionGpu", "windGpu", "coreCullGpu",
               "outerGpu", "renderGpu", "totalGpu", "advanceCpu", "poolCpu",
               "handoffCpu", "selectionCpu", "totalCpu", "cpuAllocatedBytes"]
    stats = {}
    for key in metrics:
        values = sorted(row[key] for row in rows)
        stats[key] = {"mean": statistics.mean(values),
                      "p95": values[math.ceil(len(values) * .95) - 1], "max": max(values)}
    return {"samples": len(rows), "activeMin": min(r["active"] for r in rows),
            "activeMax": max(r["active"] for r in rows),
            "activeMean": statistics.mean(r["active"] for r in rows),
            "contactMean": statistics.mean(r["contact"] for r in rows),
            "settlingMean": statistics.mean(r["settling"] for r in rows),
            "fadingMean": statistics.mean(r["fading"] for r in rows),
            "gcCollections": sum(r["gcCollections"] for r in rows), "metrics": stats}


reports = {"lifecycle": read("AcceptedLifecycle/Results.json"),
           "showcase": read("Showcase/Results.json"), "combined": read("Combined/Results.json")}
performance = {}
for case in ("wide_lifecycle", "wide_fixed"):
    report = read(f"Performance/{case}.json")
    rows = report.pop("rows")
    assert len(rows) == report["sampleTarget"] and all(r["ticks"] == 1 for r in rows)
    phases = {}
    for phase in dict.fromkeys(row["phase"] for row in rows):
        phases[phase] = summarize([row for row in rows if row["phase"] == phase])
    performance[case] = {"configuration": report, "phases": phases}
summary = {"date": "2026-10-21", "validation": reports, "performance": performance,
           "extraCpuTimerBytes": 144 * 144 * 4, "gpuBufferBytes": 224852832,
           "notes": [
               "GPU times are D3D11 timestamps; CPU times are submission scopes, not GPU durations.",
               "solveGpu includes the fused near solver and its geometry rebuild; displayGpu is separate handoff/middle work.",
               "totalGpu includes the test scene render; it is not a standalone player FPS measurement.",
               "Contact and settling phases may also contain earlier local tails; actual counts are recorded per row.",
               "The no-contact solve marker brackets no solver dispatch; the residual is timestamp overhead.",
               "32k fixed control remains over the 1 ms near-physics target; fewer active blades are not a solver optimization.",
               "The artificial deep-contact instantaneous removal has a large physical rebound before fading; see Results.md.",
               "The isolated editor stalled during shutdown after both performance reports completed; recorded separately."]}
(output / "Summary.json").write_text(json.dumps(summary, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

paths = [
    "Runtime/XPBDGrassSettings.cs", "Runtime/GrassSimulationPool.cs", "Runtime/PBDGrassPatchRenderer.cs",
    "Runtime/GrassLodHandoff.cs", "Runtime/GrassTieredHandoff.cs", "Runtime/GrassOuterCoverage.cs",
    "Shaders/GrassInteractionBlend.cginc", "Shaders/GrassOuterCoverage.compute",
    "CompactPrototype/Shaders/CompactGrassHandoff.compute", "CompactPrototype/Shaders/CompactGrass.shader",
    "CompactPrototype/Shaders/CompactGrassSolver.compute",
    "CompactPrototype/Editor/generate_compact_shaders.py",
    "CompactPrototype/Editor/GrassContactLifecycleValidation.cs",
    "CompactPrototype/Editor/GrassShowcaseValidation.cs", "CompactPrototype/Editor/GrassShowcaseSceneBuilder.cs",
    "CompactPrototype/Showcase/GrassShowcaseController.cs",
    "CompactPrototype/Showcase/Performance/ContactGpuBenchmark.cs.txt",
    "CompactPrototype/Showcase/Performance/prepare_profile.py",
    "CompactPrototype/CpuOptimization/prepare_cpu.py",
    "Scenes/PBDGrassContactLab.unity", "ResponsePrototype/Editor/GrassCombinedBaselineValidation.cs",
    "CompactPrototype/EventDriven/summarize_event_driven.py", "CompactPrototype/EventDriven/Reference.zip"]
manifest = {str((source / p).relative_to(root)).replace("\\", "/"):
            hashlib.sha256((source / p).read_bytes()).hexdigest() for p in paths}
(output / "Sources.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
for name in ["LiveFadeClose.png", "ReleasedClose.png"]:
    shutil.copy2(results / "AcceptedLifecycle" / name, output / name)
with zipfile.ZipFile(output / "Evidence.zip", "w", zipfile.ZIP_DEFLATED) as archive:
    for relative in ["AcceptedLifecycle/Results.json", "Showcase/Results.json", "Combined/Results.json",
                     "Performance/wide_lifecycle.json", "Performance/wide_fixed.json", "PerformanceShutdown.json"]:
        archive.write(results / relative, relative)
    for p in paths:
        if not p.endswith("Reference.zip"):
            archive.write(source / p, "Sources/" + p)
    for p in ["Summary.json", "Sources.json", "Reproduce.md"]:
        archive.write(output / p, p)
print("Packaged three passed regressions and 900 timestamp samples.")
for case, report in performance.items():
    for phase, values in report["phases"].items():
        print(case, phase, values["samples"], "active", round(values["activeMean"]),
              "solve", round(values["metrics"]["solveGpu"]["mean"], 3))
