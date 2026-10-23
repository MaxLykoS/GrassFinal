"""Preserve raw measurements and summarize final paired GPU timestamps by phase."""
from pathlib import Path
import hashlib
import json
import statistics
import subprocess
import zipfile

here = Path(__file__).resolve().parent
workspace = here.parents[3]
results = Path('D:/CodexTemp/codex-grass-contact-profile/SolverOptimizationResults')
summary = {'baseline': '6197058', 'cases': [], 'validation': {}}
capture = Path('D:/CodexTemp/grass-nsight-capture')
smoke_names = ['SmokeDX12Render', 'SmokeDX12NoContact', 'SmokeDX11Render']
summary['captureSmoke'] = {}
for name in smoke_names:
    path = capture / name / 'Results.json'
    if path.exists():
        summary['captureSmoke'][name] = json.loads(path.read_text(encoding='utf-8-sig'))
for folder in ['Performance', 'Groups', 'FinalPerformance']:
    for path in sorted((results / folder).glob('*.json')):
        report = json.loads(path.read_text(encoding='utf-8-sig'))
        if 'rows' not in report:
            continue
        case = {key: report[key] for key in ['caseName', 'passed', 'gpu', 'api', 'substeps',
                'iterations', 'gpuBufferBytes', 'dropped', 'failed']}
        case['experiment'] = folder
        case['phases'] = {}
        for phase in dict.fromkeys(row['phase'] for row in report['rows']):
            rows = [row for row in report['rows'] if row['phase'] == phase]
            p = {'frames': len(rows), 'activeMin': min(row['active'] for row in rows),
                 'activeMax': max(row['active'] for row in rows),
                 'ticksMin': min(row['ticks'] for row in rows), 'ticksMax': max(row['ticks'] for row in rows)}
            for key in ['active', 'solveGpu', 'displayGpu', 'coreCullGpu', 'outerGpu', 'renderGpu', 'totalGpu',
                        'poolCpu', 'handoffCpu', 'advanceCpu', 'totalCpu', 'cpuAllocatedBytes']:
                values = sorted(row[key] for row in rows)
                p[key] = {'mean': statistics.mean(values), 'median': statistics.median(values),
                          'p95': values[min(len(values)-1, int(len(values)*.95))]}
            case['phases'][phase] = p
        summary['cases'].append(case)
for name in ['Parity', 'FinalParity', 'Compact', 'Lifecycle', 'Combined', 'Showcase']:
    report = json.loads((results / name / 'Results.json').read_text(encoding='utf-8-sig'))
    summary['validation'][name] = {'passed': report['passed'], 'error': report.get('error', '')}
summary['sourceSha256'] = {}
for relative in ['Assets/PBDGrass/Runtime/PBDGrassPatchRenderer.cs',
                 'Assets/PBDGrass/CompactPrototype/Shaders/CompactGrassSolver.compute',
                 'Assets/PBDGrass/CompactPrototype/Editor/generate_compact_shaders.py']:
    data = (workspace / relative).read_bytes()
    summary['sourceSha256'][relative] = hashlib.sha256(data).hexdigest()
    if not relative.endswith('PBDGrassPatchRenderer.cs'):
        original = subprocess.check_output(['git', 'show', '6197058:'+relative], cwd=workspace)
        assert data.replace(b'\r\n', b'\n') == original.replace(b'\r\n', b'\n'), relative
(here / 'Summary.json').write_text(json.dumps(summary, indent=2), encoding='utf-8')
with zipfile.ZipFile(here / 'Evidence.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
    for folder in ['Performance', 'Groups', 'FinalPerformance', 'Parity', 'FinalParity', 'Compact', 'Lifecycle', 'Combined', 'Showcase']:
        for path in sorted((results / folder).glob('*.json')):
            archive.write(path, path.relative_to(results))
    for name in ['GroupExperimentSources.zip', 'GroupsShutdown.json', 'FinalPerformanceShutdown.json']:
        path=results / name
        if path.exists():archive.write(path, name)
    for name in smoke_names:
        for filename in ['Results.json', 'Capture.png']:
            path = capture / name / filename
            if path.exists():archive.write(path, 'capture/'+name+'/'+filename)
    for path in sorted(here.iterdir()):
        if path.suffix in ['.py', '.txt', '.ps1']:
            archive.write(path, 'reproduction/'+path.name)
    for relative in summary['sourceSha256']:
        archive.write(workspace / relative, 'production/'+relative)
for case in summary['cases']:
    if case['experiment']=='FinalPerformance':
        print(case['caseName'], {p: round(v['solveGpu']['mean'],4) for p,v in case['phases'].items()})
print('Saved Summary.json and Evidence.zip; shader and generator match committed baseline.')
