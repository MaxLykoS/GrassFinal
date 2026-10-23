"""Reproduce low-budget bank timing in the isolated project, without runtime overrides."""
from pathlib import Path
import runpy
root=Path(__file__).resolve().parents[2]
runpy.run_path(str(root/'CompactPrototype/SolverOptimization/prepare_solver.py'))
target=Path('D:/CodexTemp/codex-grass-contact-profile/Assets/PBDGrass')
path=target/'ContactProfileTemporary/Editor/ContactGpuBenchmark.cs'
text=path.read_text(encoding='utf-8-sig')
text=text.replace('EditorSceneManager.OpenScene(GrassFinal.PBDGrass.Editor.GrassShowcaseSceneBuilder.ScenePath);',
    'EditorSceneManager.OpenScene("Assets/PBDGrass/Scenes/PBDGrassIterationBudget.unity");')
needle='            demo.PatchSize=name.StartsWith("small")?96:144;'
assert text.count(needle)==1
text=text.replace(needle,'''            var budget=demo.GetComponent<GrassIterationBudgetDemo>();budget.enabled=false;
            budget.Quality=GrassIterationBudgetDemo.Budget.Minimum2x1;budget.TuneMinimumBudget=true;budget.Apply();
            if(name.StartsWith("legacy"))demo.Handoff.BakedResponse=budget.ReferenceResponse;
'''+needle)
path.write_text(text,encoding='utf-8')
print('Prepared 32768 full-pool timing with calibrated2x1 and matching bank.')
