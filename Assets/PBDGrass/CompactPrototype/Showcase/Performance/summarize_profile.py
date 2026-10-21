"""Copy raw measurements and produce explicit CPU/GPU-scope summaries."""
from pathlib import Path
import csv, hashlib, json, math, shutil, statistics

here=Path(__file__).resolve().parent
source=Path("D:/CodexTemp/codex-grass-contact-profile/Results")
raw=here/"Raw"
raw.mkdir(exist_ok=True)
summary={
    "gpuTimestampMethod":"D3D11 timestamp/disjoint queries, nonblocking DONOTFLUSH polling; no simulation-buffer readback",
    "fixedTickScope":"Isolated Unity Editor batch process, explicit 1600x900 HDR 4x MSAA Camera.Render; one complete 1/60s tick per sampled frame",
    "realtimeScope":"Windows Mono Development Player and isolated Editor Play Mode (editor field). VSync off, uncapped, same offscreen target; wall-clock delta into original 60Hz accumulator, max 4 catch-up ticks",
    "caveats":[
        "CPU and GPU overlap; do not add CPU and GPU times or invert fixed-tick GPU samples to claim Editor FPS.",
        "GPU selection/stream/whole-frame elapsed timestamp intervals can include GPU idle waiting for CPU submission.",
        "Camera render interval includes scene geometry, shadow/depth work and target resolve; opaque is a subset, not an extra sum.",
        "CPU allocation counter values in raw fixed-tick rows were not validated on this Unity Mono runtime and are not used.",
        "Other user Unity processes were left running; absolute GPU samples are subject to contention and clock variability.",
        "C: had about 200 MB free and the old C: validation import crashed. All valid runs used a new D: project; no files were deleted."
    ], "fixedTick":{}, "realtime":{}
}
def stats(values):
    ordered=sorted(values)
    return {"count":len(ordered),"mean":statistics.mean(ordered),"median":statistics.median(ordered),
            "p95":ordered[math.ceil(len(ordered)*.95)-1],"p99":ordered[math.ceil(len(ordered)*.99)-1],"max":ordered[-1]}
for p in sorted(source.glob("*.json")):
    r=json.loads(p.read_text())
    assert r["passed"], p
    shutil.copy2(p,raw/p.name)
    rows=r["rows"]
    with (raw/(p.stem+".csv")).open("w",newline="") as f:
        w=csv.DictWriter(f,fieldnames=list(rows[0]));w.writeheader();w.writerows(rows)
    item={k:v for k,v in r.items() if k!="rows"}
    if p.stem.endswith("_realtime"):
        item["fps"]=1000/statistics.mean(x["frameIntervalMs"] for x in rows)
        item["actualSimTicksPerSecond"]=sum(x["ticks"] for x in rows)/(sum(x["frameIntervalMs"] for x in rows)/1000)
        item["ticksPerFrame"]=stats([x["ticks"] for x in rows])
        item["tickHistogram"]={str(t):sum(x["ticks"]==t for x in rows) for t in range(5)}
        item["droppedSeconds"]=sum(x["droppedSeconds"] for x in rows)
        tick_rows=[x for x in rows if x["ticks"]>0]
        item["advanceCpuMsPerExecutedTick"]=sum(x["advanceCpuMs"] for x in tick_rows)/sum(x["ticks"] for x in tick_rows)
        item["stats"]={k:stats([x[k] for x in rows]) for k in ["frameIntervalMs","cpuMs","advanceCpuMs","poolCpuMs","handoffCpuMs"]}
        summary["realtime"][p.stem]=item
    else:
        item["stats"]={k:stats([x[k] for x in rows]) for k in rows[0] if k.endswith("Cpu") or k.endswith("Gpu")}
        item["activeRange"]=[min(x["active"] for x in rows),max(x["active"] for x in rows)]
        item["tickRange"]=[min(x["ticks"] for x in rows),max(x["ticks"] for x in rows)]
        shifted=[x for x in rows if x["shifted"]]
        item["shiftedSamples"]=len(shifted)
        if shifted:item["shiftedStats"]={k:stats([x[k] for x in shifted]) for k in ["streamCpu","streamGpu","totalCpu","selectionCpu","loadedCells"]}
        summary["fixedTick"][p.stem]=item
combined=[]
for name in ["wide_elevated","wide_elevated_repeat"]:
    file=source/(name+".json")
    if file.exists():combined+=json.loads(file.read_text())["rows"]
if combined:
    summary["combinedWideElevated"]={k:stats([x[k] for x in combined]) for k in combined[0] if k.endswith("Cpu") or k.endswith("Gpu")}
if (source/"Telemetry.csv").exists():shutil.copy2(source/"Telemetry.csv",here/"Telemetry.csv")
(here/"Summary.json").write_text(json.dumps(summary,indent=2)+"\n",encoding="utf-8")
print(json.dumps({"fixed":{k:{m:round(v["stats"][m]["mean"],3) for m in ["poolCpu","handoffCpu","totalCpu","solveGpu","displayGpu","renderGpu"]} for k,v in summary["fixedTick"].items()},
    "realtime":{k:{"fps":round(v["fps"],2),"ticksPerFrame":round(v["ticksPerFrame"]["mean"],2),"droppedSeconds":round(v["droppedSeconds"],3)} for k,v in summary["realtime"].items()}}))
