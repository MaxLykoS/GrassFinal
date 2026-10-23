"""Prepare paired solver measurements in the existing isolated Unity project."""
from pathlib import Path
import os
import runpy
import subprocess
import uuid

here = Path(__file__).resolve().parent
grass = here.parents[1]
workspace = grass.parents[1]
for path in [here, *here.rglob('*')]:
    if path.suffix == '.meta' or '__pycache__' in path.parts:
        continue
    meta = Path(str(path) + '.meta')
    if not meta.exists():
        meta.write_text('fileFormatVersion: 2\nguid: ' + uuid.uuid4().hex + '\n' +
                        ('folderAsset: yes\n' if path.is_dir() else '') +
                        'DefaultImporter:\n  externalObjects: {}\n', encoding='utf-8')
runpy.run_path(str(grass / 'CompactPrototype/Showcase/Performance/prepare_profile.py'))
project = Path(os.environ.get('GRASS_PROFILE_PROJECT', 'D:/CodexTemp/codex-grass-contact-profile'))
target = project / 'Assets/PBDGrass'
temporary = target / 'ContactProfileTemporary'
reference = subprocess.check_output(['git', 'show',
    '6197058:Assets/PBDGrass/CompactPrototype/Shaders/CompactGrassSolver.compute'], cwd=workspace).decode('utf-8-sig')
# Keep relative shared includes pointing to the same unchanged compact helpers.
reference = reference.replace('#include "GrassCompact', '#include "../CompactPrototype/Shaders/GrassCompact')
(temporary / 'CompactReferenceSolver.compute').write_text(reference, encoding='utf-8')
hooks = temporary / 'ContactGpuHooks.cs'
text = hooks.read_text(encoding='utf-8-sig').replace('public static bool Active;',
    'public static bool Active, ReferenceContacts;')
hooks.write_text(text, encoding='utf-8')
renderer = target / 'Runtime/PBDGrassPatchRenderer.cs'
text = renderer.read_text(encoding='utf-8-sig')
needle = '            shader.SetInt("ColliderCount",liveColliderCount);'
assert text.count(needle) == 1
text = text.replace(needle, needle + '''
            // Baseline upload behavior is installed only in the isolated test project.
            if(ContactGpuHooks.ReferenceContacts)
            {if(shapes.Length>0)contacts.SetData(shapes);shader.SetInt("ColliderCount",shapes.Length);}
''')
renderer.write_text(text, encoding='utf-8')
benchmark = temporary / 'Editor/ContactGpuBenchmark.cs'
text = benchmark.read_text(encoding='utf-8-sig')
needle = '            demo.PatchSize=name.StartsWith("small")?96:144;'
text = text.replace(needle, '''            ContactGpuHooks.ReferenceContacts=name.StartsWith("reference");
            if(ContactGpuHooks.ReferenceContacts)demo.Solver=AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/PBDGrass/ContactProfileTemporary/CompactReferenceSolver.compute");
''' + needle)
needle = '        if(report.passed && report.caseName.Contains("lifecycle")'
assert text.count(needle) == 1
text = text.replace(needle, '''        var cases=(Environment.GetEnvironmentVariable("GRASS_SOLVER_CASES")??"").Split(';');
        int index=Array.IndexOf(cases,report.caseName);
        if(report.passed && index>=0 && index+1<cases.Length)
        {
            Environment.SetEnvironmentVariable("GRASS_PROFILE_CASE",cases[index+1]);
            EditorApplication.delayCall+=Run;return;
        }
''' + needle)
benchmark.write_text(text, encoding='utf-8')
(temporary / 'Editor/ContactSolverParity.cs').write_bytes((here / 'ContactSolverParity.cs.txt').read_bytes())
print('Prepared baseline 6197058 and current solver, independent state and identical workload.')
