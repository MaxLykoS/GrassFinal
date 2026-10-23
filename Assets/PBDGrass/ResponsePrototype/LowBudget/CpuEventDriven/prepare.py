"""Prepare the frozen 1.747 ms baseline or event-driven CPU in the isolated project."""
from pathlib import Path
import runpy
import sys

here=Path(__file__).resolve().parent
grass=here.parents[2]
target=Path('D:/CodexTemp/codex-grass-contact-profile/Assets/PBDGrass')
mode=sys.argv[1] if len(sys.argv)>1 else 'current'
runpy.run_path(str(here.parent/'prepare.py'))
if mode=='before':
    for name in ['GrassSimulationPool','GrassTieredHandoff','PBDGrassPatchRenderer','GrassGridQuery']:
        (target/'Runtime'/(name+'.cs')).write_bytes((here/('Reference'+name+'.cs.txt')).read_bytes())
    # The new shared coverage is unused by the frozen implementation. Its query
    # revision API did not exist in the reference, so remove this temporary copy.
    path=target/'Runtime/GrassInteractionCoverage.cs'
    if path.exists():path.unlink()
    profile=(grass/'CompactPrototype/Showcase/Performance/prepare_profile.py').read_text(encoding='utf-8')
    exec(profile[profile.index('def replace('):],{'target':target})
benchmark=target/'ContactProfileTemporary/Editor/ContactGpuBenchmark.cs'
text=benchmark.read_text(encoding='utf-8')
text=text.replace('            lab.SetView(name.Contains("overview")?4:1);','            // Same serialized spread-scene camera in both implementations.')
text=text.replace('            lab.ApplyActors(t,contact);','            lab.ApplyActors(report.caseName.Contains("hold")?0:t,contact);')
benchmark.write_text(text,encoding='utf-8')
for name in ['GrassEventStateValidation.cs','GrassEventCpuValidation.cs']:
    path=here/(name+'.txt')
    if path.exists():(target/'ContactProfileTemporary/Editor'/name).write_bytes(path.read_bytes())
print('Prepared event CPU',mode,'using identical 2x1/F10, response bank, spread layout and tick rate.')
