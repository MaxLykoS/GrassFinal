"""Install iteration-budget experiments only in the isolated Unity project."""
from pathlib import Path
import runpy
import uuid

here=Path(__file__).resolve().parent
runpy.run_path(str(here.parent/'prepare_solver.py'))
project=Path('D:/CodexTemp/codex-grass-contact-profile')
target=project/'Assets/PBDGrass'
temporary=target/'ContactProfileTemporary'
renderer=target/'Runtime/PBDGrassPatchRenderer.cs'
text=renderer.read_text(encoding='utf-8-sig')
needle='            this.camera = camera; this.settings = settings; this.sourceMaterial = sourceMaterial;'
assert text.count(needle)==1
text=text.replace(needle,'''            // Diagnostic overrides exist only in this isolated copy.
            if(int.TryParse(Environment.GetEnvironmentVariable("GRASS_SOLVE_SUBSTEPS"),out int steps))settings.NearSubsteps=steps;
            if(int.TryParse(Environment.GetEnvironmentVariable("GRASS_SOLVE_ITERATIONS"),out int iterations))settings.ConstraintIterations=iterations;
'''+needle)
renderer.write_text(text,encoding='utf-8')
benchmark=temporary/'Editor/ContactGpuBenchmark.cs'
text=benchmark.read_text(encoding='utf-8-sig')
needle='            demo.PatchSize=name.StartsWith("small")?96:144;'
assert text.count(needle)==1
text=text.replace(needle,'''            var budget=name.Split('_')[0].Substring(1).Split('x');
            demo.Physics.NearSubsteps=int.Parse(budget[0]);demo.Physics.ConstraintIterations=int.Parse(budget[1]);
'''+needle)
benchmark.write_text(text,encoding='utf-8')
for name in ['IterationQuality.cs','IterationWindValidation.cs']:
    path=here/(name+'.txt')
    if path.exists():(temporary/'Editor'/name).write_bytes(path.read_bytes())
# This clone keeps all combined LOD/plastic/bank-integrity assertions. Accuracy
# changes are expected here, so rebake deviations are collected for all 8 types
# instead of aborting at the old bit-parity-era material thresholds.
combined=target/'ResponsePrototype/Editor/GrassCombinedBaselineValidation.cs'
text=combined.read_text(encoding='utf-8-sig')
start=text.index('                        Require(result.maxTerminalSpeed<.003f')
end=text.index('                        Debug.Log("GRASS_COMBINED_REBAKE',start)
text=text[:start]+'                        // Iteration experiment: retain the full eight-type deviation report.\n'+text[end:]
combined.write_text(text,encoding='utf-8')
for path in [here,*here.iterdir()]:
    if path.suffix=='.meta' or path.name=='__pycache__':continue
    meta=Path(str(path)+'.meta')
    if not meta.exists():meta.write_text('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\n'+
        ('folderAsset: yes\n' if path.is_dir() else '')+'DefaultImporter:\n  externalObjects: {}\n',encoding='utf-8')
print('Prepared iteration benchmarks; production solver and accepted bake assets unchanged.')
