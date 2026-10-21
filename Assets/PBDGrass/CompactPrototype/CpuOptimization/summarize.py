from pathlib import Path
import json, math, statistics, shutil, hashlib, uuid, zipfile
source=Path(__file__).resolve().parent
results=Path('D:/CodexTemp/codex-grass-contact-profile/CpuResults')
def metric(rows,key):
    v=sorted(r[key] for r in rows)
    return {'mean':statistics.mean(v),'p95':v[math.ceil(.95*len(v))-1],'max':v[-1]}
summary={'fixed':{},'realtime':{}}
archive=zipfile.ZipFile(source/'Evidence.zip','w',zipfile.ZIP_DEFLATED)
def evidence(path,folder):archive.write(path,folder+'/'+path.name)
for name in ['wide_elevated_after','wide_elevated_repeat_after','small_elevated_after','wide_moving_after','wide_overview_after','wide_no_contact_after','wide_elevated_before','wide_moving_before','wide_elevated_final','wide_moving_final','small_elevated_final']:
    r=json.loads((results/(name+'.json')).read_text(encoding='utf-8-sig'))
    assert r['passed'] and all(x['active']==32768 and x['ticks']==1 for x in r['rows']),name
    entry={k:v for k,v in r.items() if k!='rows'}
    entry['samples']=len(r['rows']);entry['metrics']={k:metric(r['rows'],k) for k in ['poolCpu','handoffCpu','advanceCpu','streamCpu','totalCpu','solveGpu','windGpu','displayGpu','coreCullGpu','outerGpu','renderGpu']}
    pages=[x for x in r['rows'] if x['shifted']]
    if pages:entry['pages']={'count':len(pages),'streamCpu':metric(pages,'streamCpu'),'totalCpu':metric(pages,'totalCpu'),'rows':[{'frame':x['frame'],'loadedCells':x['loadedCells'],'streamCpu':x['streamCpu'],'totalCpu':x['totalCpu']} for x in pages]}
    summary['fixed'][name]=entry
    evidence(results/(name+'.json'),'Results')
    evidence(results/(name+'.log'),'Logs')
for name in ['wide_editor_after','wide_player_after']:
    r=json.loads((results/(name+'_realtime.json')).read_text(encoding='utf-8-sig'));assert r['passed']
    rows=r['rows'];seconds=sum(x['frameIntervalMs'] for x in rows)/1000;ticks=sum(x['ticks'] for x in rows)
    summary['realtime'][name]={'frames':len(rows),'ticks':ticks,'seconds':seconds,'ticksPerSecond':ticks/seconds,
        'droppedSeconds':sum(x['droppedSeconds'] for x in rows),'maxTicksPerFrame':max(x['ticks'] for x in rows),
        'tickHistogram':{str(i):sum(x['ticks']==i for x in rows) for i in range(5)},
        'advanceCpuPerTick':sum(x['advanceCpuMs'] for x in rows)/ticks,
        'poolCpuPerTick':sum(x['poolCpuMs'] for x in rows)/ticks,'handoffCpuPerTick':sum(x['handoffCpuMs'] for x in rows)/ticks,
        'cpuPerFrame':metric(rows,'cpuMs')}
    evidence(results/(name+'_realtime.json'),'Results')
    shutil.copy2(results/(name+'_realtime.png'),source/(name+'.png'))
    evidence(results/(name+'.log'),'Logs')
for folder,file in [('ParityAccepted','Parity.json'),('Showcase','Results.json'),('Combined','Results.json')]:
    r=json.loads((results/folder/file).read_text(encoding='utf-8-sig'));assert r['passed'],folder
    summary[folder]=r
    archive.write(results/folder/file,'Results/'+folder+'.json')
    evidence(results/(folder+'.log'),'Logs')
archive.close()
(source/'Summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
root=source.parents[1]
old=json.loads((root/'CompactPrototype/Showcase/Performance/Sources.json').read_text(encoding='utf-8-sig'))
assert hashlib.sha256((root/'Scenes/PBDGrassContactLab.unity').read_bytes()).hexdigest()==old['sceneSha256']
unchanged=json.loads((source/'UnchangedAssets.json').read_text(encoding='utf-8-sig'))
assert all(hashlib.sha256((root/p).read_bytes()).hexdigest()==sha for p,sha in unchanged.items())
manifest={'sceneSha256':old['sceneSha256'],'unchangedAssets':len(unchanged),'gpuShadersAndBakeFormatChanged':False,
    'changedRuntime':{str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in [root/'Runtime'/f'{n}.cs' for n in ['GrassGridQuery','GrassSimulationPool','GrassTieredHandoff','GrassLodHandoff','PBDGrassPatchRenderer']]}}
(source/'Sources.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
print(json.dumps({k: {n:v.get('metrics',{}).get('advanceCpu',{}) for n,v in x.items()} for k,x in summary.items() if k=='fixed'},indent=2))
