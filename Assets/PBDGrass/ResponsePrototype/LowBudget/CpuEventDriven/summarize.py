"""Paired event-scheduler results. Never include offline readback in performance."""
from pathlib import Path
import hashlib
import json
import statistics
import subprocess
import uuid
import zipfile

here=Path(__file__).resolve().parent
grass=here.parents[2]
workspace=here.parents[4]
runs=Path('D:/CodexTemp/codex-grass-contact-profile/LowBudgetResults')
after_run='EventCpuAfterPerfVerified'
state_run='EventCpuAfterStateVerified'
validation_runs=['EventCpuLifecycleVerified','EventCpuSeeds02','EventCpuCombined02','EventCpuSwitching02','EventCpuCoverage01','EventCpuModeVerified',state_run]
fields=['advanceCpu','poolCpu','handoffCpu','selectionCpu','streamCpu','totalCpu','solveGpu','displayGpu','totalGpu']

def read(path):return json.loads(path.read_text(encoding='utf-8-sig'))
def metric(rows):
    result={}
    for key in fields:
        v=sorted(row[key] for row in rows)
        result[key]={'mean':statistics.mean(v),'median':statistics.median(v),'p95':v[int(len(v)*.95)],'p99':v[int(len(v)*.99)],'max':v[-1]}
    result['active']={'min':min(r['active']for r in rows),'max':max(r['active']for r in rows),'mean':statistics.mean(r['active']for r in rows)}
    result['gcCollections']=sum(r['gcCollections']for r in rows)
    return result

report={'date':'2026-10-23','scope':'CPU scheduling architecture; GPU solver/material/bank unchanged','cases':{},'workloadParity':{},'validation':{},'sourceSHA256':{},'bankSHA256':{},'earlierPasses':{}}
fixed={}
for version,folder,prefix in [('before','EventCpuBeforePerf','base'),('after',after_run,'event')]:
    all_rows=[]
    for suffix in ['fixedA','fixedB','moving','hold','no_contact']:
        d=read(runs/folder/(prefix+'_'+suffix+'.json'))
        assert d['passed'] and d['failed']==0 and d['dropped']==0,(folder,suffix,d.get('error'))
        rows=sorted(d['rows'],key=lambda r:r['frame']);count=1440 if suffix=='moving' else 300
        assert [r['frame']for r in rows]==list(range(121,121+count)),('missing/duplicate frame',folder,suffix)
        assert all(r['ticks']==1 and (r['active']==32768 if suffix.startswith('fixed') else r['active']<=32768)for r in rows)
        assert d['substeps']==2 and d['iterations']==1
        report['cases'][version+'_'+suffix]={k:d[k]for k in ['caseName','gpu','cpu','api','unity','patchSize','blades','colliders','substeps','iterations','width','height','msaa','gpuBufferBytes','middleFull','middleOuter']}
        report['cases'][version+'_'+suffix]['metrics']=metric(rows)
        if suffix.startswith('fixed'):all_rows+=rows
        if version=='after':
            old=read(runs/'EventCpuBeforePerf'/('base_'+suffix+'.json'))
            assert all(d[k]==old[k]for k in ['patchSize','blades','colliders','substeps','iterations','width','height','msaa','gpuBufferBytes','middleFull','middleOuter'])
            original=sorted(old['rows'],key=lambda r:r['frame'])
            assert all(all(a[k]==b[k]for k in ['frame','active','ticks','contact','settling','fading','shifted','loadedCells','selectCalls'])for a,b in zip(rows,original)),('workload differs',suffix)
            report['workloadParity'][suffix]=True
    fixed[version]=metric(all_rows)
report['fixed']=fixed
for name in validation_runs:
    d=read(runs/name/'Results.json');assert d['passed'],(name,d.get('error'),d.get('failures',[])[:3])
    report['validation'][name]=d
world=report['validation'][state_run]
assert world['exactPassed'] and world['compared'] and len(world['rows'])==280 and not world['failures']
assert not world['tolerances']
oracle=read(here/'DotNetOracle/Results.json')
report['dotNetOracle']=oracle
for name in ['EventCpuAfterPerf01','EventCpuAfterPerf02','EventCpuAfterPerfFinal']:
    for file in (runs/name).glob('*.json'):
        d=read(file)
        report['earlierPasses'][name+'/'+file.name]={'passed':d['passed'],'metrics':metric(d['rows'])}
for path in [grass/'Runtime'/name for name in ['GrassSimulationPool.cs','GrassTieredHandoff.cs','GrassGridQuery.cs','GrassInteractionCoverage.cs','GrassHandoffHeap.cs','PBDGrassPatchRenderer.cs']]:
    report['sourceSHA256'][path.name]=hashlib.sha256(path.read_bytes()).hexdigest()
with zipfile.ZipFile(here.parent/'Evidence.zip')as z:
    for path in (here.parent/'Baked').glob('GrassResponse*.asset'):
        old=z.read('production/'+path.relative_to(workspace).as_posix());assert old==path.read_bytes()
        report['bankSHA256'][path.name+'/2x1']=hashlib.sha256(old).hexdigest()
for path in (grass/'ResponsePrototype/Baked').glob('GrassResponse*.asset'):
    old=subprocess.check_output(['git','show','HEAD:'+path.relative_to(workspace).as_posix()],cwd=workspace);assert old==path.read_bytes()
    report['bankSHA256'][path.name+'/8x3']=hashlib.sha256(old).hexdigest()
(here/'Summary.json').write_text(json.dumps(report,indent=2,ensure_ascii=False),encoding='utf-8')
b=fixed['before'];a=fixed['after']
table='\n'.join(f"| {title} | {x['advanceCpu']['mean']:.3f} | {y['advanceCpu']['mean']:.3f} | {y['advanceCpu']['p95']:.3f} | {100*(1-y['advanceCpu']['mean']/x['advanceCpu']['mean']):.1f}% |"for title,x,y in [
    ('固定32768根，600个完整tick',b,a),
    ('移动碰撞体＋相机，1440ticks',report['cases']['before_moving']['metrics'],report['cases']['after_moving']['metrics']),
    ('碰撞体静止，草仍持续模拟',report['cases']['before_hold']['metrics'],report['cases']['after_hold']['metrics']),
    ('无碰撞，事件池为空',report['cases']['before_no_contact']['metrics'],report['cases']['after_no_contact']['metrics'])])
breakdown='\n'.join(f"| {label} | {b[k]['mean']:.3f} | {a[k]['mean']:.3f} |"for label,k in [('CPU完整Advance','advanceCpu'),('其中模拟池＋共享覆盖','poolCpu'),('其中显示交接管理','handoffCpu')])
moving=report['cases']['after_moving']['metrics']['active']
text=f'''# CPU事件调度重构 — 2026-10-23

测试场景仍为 [PBDGrassIterationBudget.unity](D:/UnityProject/GrassFinal/Assets/PBDGrass/Scenes/PBDGrassIterationBudget.unity)，12个分散碰撞体、144米流式核心、663552根驻留草、中景52/64米、外围覆盖和高视角不变。32768为物理上限；2×1、F10、匹配响应库、GPU求解和显示Shader方程不变。

## 实测CPU

RTX3080Ti，Ryzen7 5800X，Unity2022.3.62f2c1/D3D11，1600×900/4MSAA。CPU用Stopwatch、GPU用独立非阻塞timestamps。每用例预热120tick；两个固定满池用例合并600tick，移动1440tick，其余各300tick。所有采样每帧恰好执行一个完整60Hz物理tick。

| 工作负载 | 旧CPU均值 ms | 新CPU均值 ms | 新CPU P95 ms | 均值降低 |
|---|---:|---:|---:|---:|
{table}

固定满池拆分（子项已包含在Advance中）：

| 区间 | 旧 ms | 新 ms |
|---|---:|---:|
{breakdown}

移动用例按事件实际占池：活跃数量{moving['min']}～{moving['max']}根，均值{moving['mean']:.0f}根，不能把该组CPU/GPU结果称为固定32768根的结果。

“固定满池”严格每tick32768根，不平均空闲帧；“静止碰撞”保持碰撞体位置，GPU仍继续物理计算，不能当32768满池GPU达标证据。移动用例逐帧active/contact/settling/fading、流式换块和tick数与旧版一致。固定满池前后显式GPU buffer总量同为224852832字节（214.44MiB），不是总VRAM。

本轮重新测量冻结的1.747ms版本，因此旧均值会随桌面负载变化。保留01/02及Final早期测试的全部逐帧数据和汇总；Final之后仅补充到期事件的浮点判定守卫，并完整重测为Verified。02出现GPU求解和CPU提交同时升高的波动，不据此声称GPU算法变慢。报告采用最终源码的完整复测，不从多个版本混选最快帧。CPU和GPU可重叠，二者不直接相加为整帧。

## 改动

- **共享覆盖。** 每个碰撞体缓存接触与预热覆盖，按草块引用计数合并；模拟池与交接共用进入/退出差分，避免重复查询。重叠碰撞体离开一个不会误触发释放。
- **保留物理身份。** 活跃列表、世界草块到GPU槽的映射长期保留，增删时才改变。GPU活跃列表仅在载荷变化时上传；内部列表和槽编号允许变化。
- **到期事件。** 每个活跃块最多一个可取消的堆节点；离开碰撞后2秒切淡出，3秒回收，期间一直跑XPBD。重碰撞保留节点、速度和塑性，取消原期限。堆弹出后用原始阶段判定式复核；Mono中表达式中间精度与已存储期限可能不同，未到期时重新排到下一float，避免默认2＋1秒边界早一tick切换。
- **按变化交接。** 只分类所有权变化、过渡到期和流式替换的草块；不每tick扫描全部历史槽。历史槽足够时保留全部optional请求，真正超额前才更新距离排名并仲裁。
- **固定满池也优化。** 相机不动时保留近圈距离顺序，使用已排序的活跃/非活跃两条流合并滞回分数；不重新比较排序全部候选。

这里没有把CPU管理工作简单降频。GPU物理、移动碰撞输入、塑性、逐时渐变继续按原tick运行。每次流式回收取消对应事件和旧身份，防止同环形索引对应新世界草块后串状态。

## 验证

两个场景各1440ticks，合计20个检查点/280组通道，按世界坐标及叶片ID对齐后全部逐位一致，未设置任何容差。包括活跃身份、节点位置/预测位置/速度、材料旋转/角速度、塑性、渲染几何、全驻留区压缩姿态/塑性以及风状态。包含淡出中重碰、平滑移焦、换块、256米整片卸载和返回，事件场景最终排空。

生命周期、冷暖入池、32768＋98304联合baseline、八型临时重烘焙、运行时质量档/响应库切换均通过。额外模式测试验证淡出中切回固定模式时保留槽位、物理和显示权重，随后切回事件模式能完整回收；同时修复了原模式切换可能残留释放时间/永久phase2的问题。

[纯CPU oracle复现与限制](D:/UnityProject/GrassFinal/Assets/PBDGrass/ResponsePrototype/LowBudget/CpuEventDriven/DotNetOracle/README.md)：24万tick对照冻结原版（该.NET oracle运行早于Mono期限守卫补丁；最终Mono源码另由Unity逐帧负载与世界状态检查覆盖），预算1/16/128/1024，固定/事件模式分别验证活跃世界ID、统计、退出时间和稳定槽。1024步覆盖oracle在Unity内也通过，含重叠、索引换人、不同覆盖形状、Reset及worldRevision索引复用。所有GPU读回仅在独立离线验证中，不进入性能循环或运行时代码。

旧/新两套共16个烘焙资产哈希保持不变，无重烘焙迁移、无新GPU buffer。

## 仍然存在的工作

移动碰撞体仍需检查它的有限覆盖区域；连续移动相机为保持旧预热几何语义，可能使多个碰撞体覆盖缓存失效。相机移动也会更新有界活跃池的边界距离，历史池超额时仍需要局部排名。因此这版不是任何负载都恒定O(1)，也不宣称稳定0.1ms。收益来自消除了逐tick重建两套全候选管理流程。

新增的是有界CPU索引/堆/覆盖数组，没有恢复中景全节点CPU副本。仅Pool＋共享覆盖固定数组相对旧事件路径约增加0.74MiB，另有交接索引和局部列表；这一数字不是整个进程内存或总新增内存。

复现：`prepare.py before/current`安装至独立项目；上级run.ps1运行ContactGpuBenchmark.Run，用例分别base_fixedA;base_fixedB;base_moving;base_hold;base_no_contact与event_fixedA;event_fixedB;event_moving;event_hold;event_no_contact。GrassEventStateValidation.Run用GRASS_CPU_EVENT_REFERENCE指向旧Results.json，默认严格逐位检查；ModeSwitch为单独模式测试，GrassEventCpuValidation.Run为覆盖oracle。

Summary.json保留各组均值/P95/P99/最大值和验证通道；Evidence.zip保留原始逐帧JSON和日志。大体积原始checkpoint gzip仅留在D:/CodexTemp独立输出目录，不复制进Assets。Reference*.cs.txt是本次重构前快照。
'''
(here/'Results.md').write_text(text,encoding='utf-8')
folders=['EventCpuBeforePerf','EventCpuAfterPerf01','EventCpuAfterPerf02','EventCpuAfterPerfFinal',after_run,'EventCpuBeforeState02','EventCpuAfterState01','EventCpuAfterState02',*validation_runs]
with zipfile.ZipFile(here/'Evidence.zip','w',zipfile.ZIP_DEFLATED)as z:
    for name in dict.fromkeys(folders):
        for path in (runs/name).iterdir():
            if path.is_file() and path.suffix in ['.json','.log']:
                z.write(path,name+'/'+path.name)
for path in [here,*here.rglob('*'),grass/'Runtime/GrassInteractionCoverage.cs']:
    if path.suffix=='.meta' or '__pycache__'in path.parts:continue
    meta=Path(str(path)+'.meta')
    if not meta.exists():
        importer='MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {fileID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'if path.suffix=='.cs'else'DefaultImporter:\n  externalObjects: {}\n'
        meta.write_text('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\n'+('folderAsset: yes\n'if path.is_dir()else'')+importer,encoding='utf-8')
print(table)
