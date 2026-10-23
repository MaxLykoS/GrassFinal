"""Summarize measured CPU versions, checking identical workloads and offline regressions."""
from pathlib import Path
import hashlib
import json
import statistics
import subprocess
import uuid
import zipfile

here=Path(__file__).resolve().parent
root=Path('D:/CodexTemp/codex-grass-contact-profile/LowBudgetResults')
keys=['advanceCpu','poolCpu','handoffCpu','selectionCpu','streamCpu','totalCpu','solveGpu','displayGpu','totalGpu','cpuAllocatedBytes',
      'PoolQueryDetail','PoolRankDetail','PoolAssignDetail','ConfigDetail','BuildQueryDetail','ClassifyDetail','BoundaryDetail','AllocateDetail','UploadDetail']

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def metrics(rows):
    result={}
    for key in keys:
        values=sorted(row[key] for row in rows)
        result[key]={'mean':statistics.mean(values),'median':statistics.median(values),'p95':values[int(len(values)*.95)],'max':values[-1]}
    result['active']={'min':min(row['active'] for row in rows),'max':max(row['active'] for row in rows),'mean':statistics.mean(row['active'] for row in rows)}
    result['gcCollections']=sum(row['gcCollections'] for row in rows)
    return result

report={'date':'2026-10-23','scope':'CPU scheduling only; same spread scene,2x1,F10,matching response bank','cases':{},'validation':{}}
all_fixed={}
for version in ['before','after']:
    fixed=[]
    for suffix in ['fixedA','fixedB','moving']:
        name=version+'_'+suffix
        raw=read(root/('CpuBeforePerf' if version=='before' else 'CpuAfterPerfFinal')/(name+'.json'))
        assert raw['passed'] and raw['failed']==0 and raw['dropped']==0,name
        rows=sorted(raw['rows'],key=lambda row:row['frame']);assert len(rows)==(1440 if suffix=='moving' else 300),name
        assert [row['frame'] for row in rows]==list(range(121,121+len(rows))),('missing/duplicate frame',name)
        assert all(row['ticks']==1 and (row['active']<=32768 if suffix=='moving' else row['active']==32768) for row in rows),name
        report['cases'][name]={k:raw[k] for k in ['caseName','gpu','cpu','api','unity','patchSize','blades','colliders','substeps','iterations','width','height','msaa','gpuBufferBytes','middleFull','middleOuter']}
        report['cases'][name]['metrics']=metrics(rows)
        if suffix!='moving':fixed+=rows
        if version=='after':
            old_raw=read(root/'CpuBeforePerf'/('before_'+suffix+'.json'))
            assert all(old_raw[k]==raw[k] for k in ['patchSize','blades','colliders','substeps','iterations','width','height','msaa','gpuBufferBytes','middleFull','middleOuter']),('configuration differs',suffix)
            old=sorted(old_raw['rows'],key=lambda row:row['frame'])
            for a,b in zip(old,rows):
                assert all(a[k]==b[k] for k in ['frame','active','ticks','contact','settling','fading','loadedCells','shifted','selectCalls']),('workload differs',suffix,a['frame'])
    all_fixed[version]=metrics(fixed)
report['fixed']=all_fixed
for name in ['CpuBeforeParityComplete','CpuAfterParityFinal','CpuPoolParityFinal','CpuSpreadScene','CpuLifecycleFinal','CpuCombinedFinal','CpuSeedsFinal','CpuSwitchingFinal']:
    data=read(root/name/'Results.json');assert data['passed'],(name,data.get('error'))
    report['validation'][name]=data
report['sourceHashes']={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (here.parents[2]/'Runtime').glob('*.cs')}
report['bankHashes']={}
workspace=here.parents[4]
with zipfile.ZipFile(here.parent/'Evidence.zip') as archive:
    for path in (here.parent/'Baked').glob('GrassResponse*.asset'):
        previous=archive.read('production/'+path.relative_to(workspace).as_posix())
        assert previous==path.read_bytes(),('matching bank changed',path)
        report['bankHashes'][str(path.relative_to(workspace))]=hashlib.sha256(previous).hexdigest()
for path in (here.parents[1]/'Baked').glob('GrassResponse*.asset'):
    previous=subprocess.check_output(['git','show','HEAD:'+path.relative_to(workspace).as_posix()],cwd=workspace)
    assert previous==path.read_bytes(),('accepted bank changed',path)
    report['bankHashes'][str(path.relative_to(workspace))]=hashlib.sha256(previous).hexdigest()
(here/'Summary.json').write_text(json.dumps(report,indent=2,ensure_ascii=False),encoding='utf-8')
b=all_fixed['before'];a=all_fixed['after'];bm=report['cases']['before_moving']['metrics'];am=report['cases']['after_moving']['metrics']
table='\n'.join(f"| {label} | {b[key]['mean']:.3f} | {a[key]['mean']:.3f} | {100*(1-a[key]['mean']/b[key]['mean']):.1f}% |" for label,key in [('CPU Advance','advanceCpu'),('模拟池选择（包含在Advance）','poolCpu'),('交接管理（包含在Advance）','handoffCpu')])
detail='\n'.join(f"| {label} | {b[key]['mean']:.3f} | {a[key]['mean']:.3f} |" for label,key in [('模拟池局部查询和评分','PoolQueryDetail'),('模拟池优先级/距离排序','PoolRankDetail'),('模拟池槽位管理','PoolAssignDetail'),('交接查询/列表构造','BuildQueryDetail'),('交接工作排序/分类','ClassifyDetail'),('交接可选历史排序','BoundaryDetail'),('交接槽位管理','AllocateDetail'),('交接数据上传/派发','UploadDetail')])
paths=report['validation']['CpuSpreadScene']['paths']
path_table='\n'.join(f"| {p['name']} | {p['travel']*2:.0f} | {p['uniqueOneMetreCells']} |" for p in paths if p['planar'])
text=f'''# 碰撞体分散与CPU管理优化 — 2026-10-23

场景：`Assets/PBDGrass/Scenes/PBDGrassIterationBudget.unity`。仅修改实验场景及其重建助手；旧ContactLab和联合baseline场景保持原状。

12个碰撞体分散布置，7条平面运动轨迹，3个分布式压杆，2个固定站。主要轨迹横跨36～84米，速度低于6m/s；相机保存为(0,72,-12)、FOV70，仍以主相机参数剔除。兼容中景仍为52/64米，XPBD上限仍32768，2×1/F10和新响应库不变。

| 平面运动体 | 端到端跨度/直径 m | 60秒经过的1米网格数 |
|---|---:|---:|
{path_table}

![分散后的实验场景]({(here/'Spread_t8.png').as_posix()})

## 为何之前CPU要2.17ms

之前2.17ms来自较聚集布局的完整CPU Advance。改为分散布局后，先冻结修改前的三份运行时文件，同样布局重测为{b['advanceCpu']['mean']:.3f}ms，说明不能直接把不同布局的旧2.17ms作为加速比例分母。

原实现已经使用空间索引：流式草块通过整数坐标直接定位环形槽；静态草场通过Dictionary桶定位。单个格子的地址/状态查找是O(1)，没有每帧扫描整个大世界。主要开销是每tick重新构建周边集合、计算相同距离、分类并比较排序，以及新槽位逐个扫描池。一个碰撞体覆盖K个格子仍要处理K个格子，整个物理管理不能称为O(1)。

本次做了：保留原有碰撞优先级/距离/ID排序规则并缓存比较委托；槽位一次收集空闲列表/最小堆，取代每个新格子扫描1024槽；缓存不变的交接环和距离分数；工作ID用基数排序；保留相同输入下边界排序的原有等分顺序；跳过无状态变化的分类运算；复用流式暂存数组和委托，缓存常量uniform。相机、半径、速度、流式换块变化时使相应缓存失效。保留变化格子更新和固定容量历史池检查。第一版模拟池排序缓存因移动体使候选持续变化，实测增加约0.06ms，已撤回；只保留测量有效的改动。

## 同布局配对性能

3080Ti、Ryzen7 5800X、Unity2022.3.62f2c1、D3D11、1600×900、4×MSAA；144米驻留核心、663552根驻留草、中景52/64米、12个碰撞体和外围开启。两个固定用例各预热120帧+采样300帧，每帧严格32768根XPBD和一个完整60Hz tick；CPU用Stopwatch，GPU用原生非阻塞timestamps。读回仅在分开的离线验证程序，不进入计时循环。

| 完整tick区间 | 修改前均值 ms | 修改后均值 ms | 减少 |
|---|---:|---:|---:|
{table}

Advance P95：{b['advanceCpu']['p95']:.3f}→{a['advanceCpu']['p95']:.3f}ms。选择和交接已经包含在Advance中，不能再次相加。

| CPU阶段 | 修改前 ms | 修改后 ms |
|---|---:|---:|
{detail}

相机和碰撞体同时移动的24秒流式用例，各1440帧，逐帧活跃数量、碰撞/收敛/过渡数量、换块事件完全一致；CPU Advance均值{bm['advanceCpu']['mean']:.3f}→{am['advanceCpu']['mean']:.3f}ms，P95 {bm['advanceCpu']['p95']:.3f}→{am['advanceCpu']['p95']:.3f}ms。该用例按真实事件占池，不能作为固定32768根的GPU预算达标证据。

固定满池GPU求解+近景重建均值{b['solveGpu']['mean']:.3f}→{a['solveGpu']['mean']:.3f}ms；这里只改CPU管理，GPU计时的小幅变化不作为算法加速结论。显式GPU buffer总量仍224852832字节（214.44MiB），不是总VRAM。新增CPU缓存为几百KB量级，避免重新生成大规模CPU草叶数据。

## 行为验证

- 19个随机/同分LRU/流式用例，32640次选择，与冻结原版逐步比较选择、槽位、初始化和全部旧状态数组。
- 固定满池与事件驱动两条720tick轨迹，含碰撞停止、再次碰撞、2+1秒回收和流式移动；14个检查点逐位校验CPU选择/历史状态、近景节点/速度/材质框架/塑性/几何，以及整个驻留区压缩姿态、塑性和风状态。
- 600tick新布局模拟和60秒轨迹覆盖检查、生命周期塑性/流式、联合baseline1440ticks、冷/暖入池、运行时响应库切换通过。
- 求解Shader、碰撞和塑性方程、迭代预算、旧/新烘焙资产未改。CPU管理优化没有引入GPU同步读回。

复现：`prepare.py before`或`prepare.py current`写入独立测试项目；通过上级`run.ps1`运行ContactGpuBenchmark.Run，Cases分别before_fixedA;before_fixedB;before_moving和after_fixedA;after_fixedB;after_moving。GrassCpuStateParity.Run读GRASS_CPU_PARITY_REFERENCE作严格对照；GrassPoolCpuParity.Run为CPU随机对照。Summary.json保留均值/P95/最大值，Evidence.zip保存逐帧JSON、日志和图片。非阻塞GPU查询可能乱序返回，逐帧对照先按frame排序并检查缺帧/重复帧。Reference*.cs.txt是本次修改前快照，非默认运行代码。
'''
(here/'Results.md').write_text(text,encoding='utf-8')
for image in ['Spread_t0.png','Spread_t8.png']:
    (here/image).write_bytes((root/'CpuSpreadScene'/image).read_bytes())
with zipfile.ZipFile(here/'Evidence.zip','w',zipfile.ZIP_DEFLATED) as z:
    for folder in ['CpuBeforePerf','CpuAfterPerf','CpuAfterPerfFinal','CpuBeforeParityComplete','CpuAfterParity','CpuAfterParityFinal','CpuPoolParity','CpuPoolParityFinal','CpuSpreadScene','CpuLifecycle','CpuLifecycleFinal','CpuCombined','CpuCombinedFinal','CpuSeeds','CpuSeedsFinal','CpuSwitching','CpuSwitchingFinal']:
        for path in (root/folder).iterdir():
            if path.is_file():z.write(path,folder+'/'+path.name)
for path in [here,*here.iterdir()]:
    if path.suffix=='.meta' or '__pycache__' in path.parts:continue
    meta=Path(str(path)+'.meta')
    if not meta.exists():meta.write_text('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\n'+('folderAsset: yes\n' if path.is_dir() else '')+'DefaultImporter:\n  externalObjects: {}\n',encoding='utf-8')
print(table)
print('Moving CPU Advance',bm['advanceCpu']['mean'],'->',am['advanceCpu']['mean'])
