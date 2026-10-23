"""Archive this experiment's raw evidence without Unity logs or source-cache access."""
from pathlib import Path
import hashlib
import json
import statistics
import subprocess
import uuid
import zipfile

here=Path(__file__).resolve().parent
workspace=here.parents[4]
results=Path('D:/CodexTemp/codex-grass-contact-profile/RegisterResults')
summary={'date':'2026-10-22','reference':'6197058 shader, plus current disabled-collider filtering',
         'performance':[], 'checks':{}, 'productionSha256':{}}
for folder in sorted(results.iterdir()):
    if not folder.is_dir():continue
    paths=list(folder.glob('*.json'))+list(folder.glob('*/Results.json'))
    for path in sorted(paths):
        r=json.loads(path.read_text(encoding='utf-8-sig'))
        if 'rows' not in r:
            summary['checks'][str(path.relative_to(results)).replace('\\','/')]=r
            continue
        out={k:r.get(k) for k in ['caseName','passed','error','api','gpu','substeps','iterations','gpuBufferBytes','dropped','failed']}
        out['run']=folder.name
        out['scope']='Includes all solve, rebuild and optional stage/unstage GPU work'
        if folder.name=='PerformanceSoa':out['scope']='PRELIMINARY: solveGpu excludes UnstageSolver; do not use as total solver cost'
        out['frames']=len(r['rows'])
        out['activeMin']=min(x['active'] for x in r['rows']);out['activeMax']=max(x['active'] for x in r['rows'])
        out['tickMin']=min(x['ticks'] for x in r['rows']);out['tickMax']=max(x['ticks'] for x in r['rows'])
        out['metrics']={}
        for key in ['solveGpu','displayGpu','coreCullGpu','outerGpu','renderGpu','totalGpu','advanceCpu','poolCpu','handoffCpu','totalCpu']:
            values=sorted(x[key] for x in r['rows'])
            out['metrics'][key]={'mean':statistics.mean(values),'median':statistics.median(values),
                                 'p95':values[min(len(values)-1,int(.95*len(values)))]}
        summary['performance'].append(out)
for relative in ['Assets/PBDGrass/Runtime/PBDGrassPatchRenderer.cs',
                 'Assets/PBDGrass/CompactPrototype/Shaders/CompactGrassSolver.compute',
                 'Assets/PBDGrass/CompactPrototype/Editor/generate_compact_shaders.py']:
    data=(workspace/relative).read_bytes()
    summary['productionSha256'][relative]=hashlib.sha256(data).hexdigest()
    if not relative.endswith('PBDGrassPatchRenderer.cs'):
        original=subprocess.check_output(['git','show','6197058:'+relative],cwd=workspace)
        assert data.replace(b'\r\n',b'\n')==original.replace(b'\r\n',b'\n'),relative
(here/'Summary.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
with zipfile.ZipFile(here/'Evidence.zip','w',zipfile.ZIP_DEFLATED) as z:
    for folder in sorted(results.iterdir()):
        if not folder.is_dir():continue
        for path in sorted(list(folder.glob('*.json'))+list(folder.glob('*/Results.json'))):
            z.write(path,'results/'+str(path.relative_to(results)))
    for path in sorted(here.iterdir()):
        if path.suffix in ['.py','.txt','.md','.json']:
            z.write(path,'reproduction/'+path.name)
for path in [here,*here.iterdir()]:
    if path.suffix=='.meta' or path.name=='__pycache__':continue
    meta=Path(str(path)+'.meta')
    if not meta.exists():meta.write_text('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\n'+
        ('folderAsset: yes\n' if path.is_dir() else '')+'DefaultImporter:\n  externalObjects: {}\n',encoding='utf-8')
print('Archived',len(summary['performance']),'measurement cases and',len(summary['checks']),'checks.')
for c in summary['performance']:
    if c['run']=='PerformanceTransposeFull':print(c['caseName'],round(c['metrics']['solveGpu']['mean'],4),c['gpuBufferBytes'])
