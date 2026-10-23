"""Install temporary instrumentation in the isolated validation project only."""
from pathlib import Path
import json, os, shutil, uuid, zipfile

source = Path(__file__).resolve().parents[3]
project = Path(os.environ.get("GRASS_PROFILE_PROJECT", "D:/CodexTemp/codex-grass-contact-profile"))
assert project.name == "codex-grass-contact-profile"
target = project / "Assets/PBDGrass"
workspace = source.parents[1]
for path in Path(__file__).parent.rglob("*"):
    if path.suffix != ".meta" and not Path(str(path)+".meta").exists():
        Path(str(path)+".meta").write_text("fileFormatVersion: 2\nguid: "+uuid.uuid4().hex+"\n"+
            ("folderAsset: yes\n" if path.is_dir() else "")+"DefaultImporter:\n  externalObjects: {}\n",encoding="utf-8")
for folder in ["ProjectSettings", "Assets/PBDGrass"]:
    for path in (workspace/folder).rglob("*"):
        if not path.is_file() or "__pycache__" in path.parts: continue
        destination=project/path.relative_to(workspace)
        if not destination.exists() or path.read_bytes()!=destination.read_bytes():
            destination.parent.mkdir(parents=True,exist_ok=True)
            shutil.copy2(path,destination)
# Grass has no external package dependency. Keep engine modules from the source
# manifest; avoid downloading unrelated stores, collaboration tools or IDE packages.
manifest=json.loads((workspace/"Packages/manifest.json").read_text(encoding="utf-8-sig"))
(project/"Packages").mkdir(parents=True,exist_ok=True)
(project/"Packages/manifest.json").write_text(json.dumps({"dependencies": {
    k:v for k,v in manifest["dependencies"].items() if k.startswith("com.unity.modules.")
}},indent=2),encoding="utf-8")
instrument = target / "ContactProfileTemporary"
(instrument / "Editor").mkdir(parents=True, exist_ok=True)
(instrument / "Editor/ContactProfile.Editor.asmdef").write_text(json.dumps({
    "name": "ContactProfile.Editor",
    "references": ["GrassFinal.PBDGrass", "GrassFinal.PBDGrass.Compact.Editor"],
    "includePlatforms": ["Editor"], "autoReferenced": False
}), encoding="utf-8")
(instrument / "Plugins/x86_64").mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(source / "ResponsePrototype/CombinedBaselineValidation/GpuReproduction.zip") as archive:
    (instrument / "Plugins/x86_64/GrassGpuTimer.dll").write_bytes(archive.read("native/x86_64/GrassGpuTimer.dll"))
    hooks = archive.read("temporary/Runtime/PlasticGpuHooks.cs").decode("utf-8-sig").replace("PlasticGpuHooks", "ContactGpuHooks")
hooks = hooks.replace("public static IntPtr Function;", """public static long Stamp() => System.Diagnostics.Stopwatch.GetTimestamp();
        public static double Ms(long start) => (Stamp()-start)*1000.0/System.Diagnostics.Stopwatch.Frequency;
        public static double PoolCpu,HandoffCpu,SelectionCpu;
        public static int SelectCalls;
        public static void ResetCpu() {PoolCpu=HandoffCpu=SelectionCpu=0;SelectCalls=0;}
        public static IntPtr Function;""")
(instrument / "ContactGpuHooks.cs").write_text(hooks, encoding="utf-8")
(instrument / "Editor/ContactGpuBenchmark.cs").write_bytes(Path(__file__).with_name("ContactGpuBenchmark.cs.txt").read_bytes())
(instrument / "ContactRealtimeRunner.cs").write_bytes(Path(__file__).with_name("ContactRealtimeRunner.cs.txt").read_bytes())
(instrument / "Editor/BuildContactPlayer.cs").write_bytes(Path(__file__).with_name("BuildContactPlayer.cs.txt").read_bytes())
(instrument / "Editor/ContactRealtimeEditor.cs").write_bytes(Path(__file__).with_name("ContactRealtimeEditor.cs.txt").read_bytes())

def replace(text, before, after):
    assert text.count(before) == 1, (before, text.count(before))
    return text.replace(before, after)

renderer = target / "Runtime/PBDGrassPatchRenderer.cs"
text = renderer.read_text(encoding="utf-8-sig")
text = replace(text, "private void SelectPool()\n        {\n", "private void SelectPool()\n        {\n            long selectionStart=ContactGpuHooks.Stamp();ContactGpuHooks.SelectCalls++;\n")
text = replace(text, "            Pool.Select(SimulationCenter,", "            long poolStart=ContactGpuHooks.Stamp();\n            Pool.Select(SimulationCenter,")
pool_end = "            if(uploadedSelectionVersion!=Pool.SelectionVersion)" if "if(uploadedSelectionVersion!=Pool.SelectionVersion)" in text else "            for (int i=0; i<Pool.Selected.Count; i++)"
text = replace(text, pool_end, "            ContactGpuHooks.PoolCpu+=ContactGpuHooks.Ms(poolStart);\n" + pool_end)
text = replace(text, "if (Handoff != null) Handoff.Select(Pool.Selected,SimulationTime,SimulationCenter,interactionRegions);",
    "if (Handoff != null) {long h=ContactGpuHooks.Stamp(); Handoff.Select(Pool.Selected,SimulationTime,SimulationCenter,interactionRegions);ContactGpuHooks.HandoffCpu+=ContactGpuHooks.Ms(h);}")
text = replace(text, "        }\n        private void ReadShape", "            ContactGpuHooks.SelectionCpu+=ContactGpuHooks.Ms(selectionStart);\n        }\n        private void ReadShape")
after_select = "            int liveColliderCount=0;" if "int liveColliderCount=0;" in text else "            for (int i=0;i<shapes.Length;i++)"
text = replace(text, "            SelectPool();\n" + after_select, "            SelectPool();ContactGpuHooks.Event(2);\n" + after_select)
text = replace(text, "            if (Pool.Selected.Count>0) for(int substep", "            ContactGpuHooks.Event(3);\n            if (Pool.Selected.Count>0) for(int substep")
text = replace(text, "            SimulationTime+=tick; SimulationTicks++;", "            ContactGpuHooks.Event(4);\n            SimulationTime+=tick; SimulationTicks++;")
text = replace(text, "            Handoff?.UpdateDisplay(SimulationTime,tick);", "            Handoff?.UpdateDisplay(SimulationTime,tick);ContactGpuHooks.Event(6);")
renderer.write_text(text, encoding="utf-8")
tiered = target / "Runtime/GrassTieredHandoff.cs"
text = tiered.read_text(encoding="utf-8-sig")
display_count = "data.BladeCount" if "(data.BladeCount+63)" in text else "data.Blades.Length"
text = replace(text, f"            shader.Dispatch(displayKernel,({display_count}+63)/64,1,1); }}",
    f"            ContactGpuHooks.Event(5);\n            shader.Dispatch(displayKernel,({display_count}+63)/64,1,1); }}")
tiered.write_text(text, encoding="utf-8")
print("Installed timestamp and CPU instrumentation only in", target)
