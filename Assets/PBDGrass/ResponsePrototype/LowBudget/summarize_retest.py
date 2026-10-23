"""Summarize a completed interleaved full-pool retest without mixing old loaded runs."""
from pathlib import Path
import json
import statistics
import sys
import uuid
here=Path(__file__).resolve().parent
run=sys.argv[1] if len(sys.argv)>1 else 'PerformanceRetest01'
folder=Path('D:/CodexTemp/codex-grass-contact-profile/LowBudgetResults')/run
report={'date':'2026-10-23','run':run,'cases':[],'scope':'32768 active XPBD, complete60Hz tick,2x1,F10; GPU timestamps, D3D11',
        'loadBefore':{'pstate':'P8','gpuPercent':21,'graphicsMHz':210,'powerW':37.85},
        'loadAfter':{'pstate':'P8','gpuPercent':32,'graphicsMHz':210,'powerW':38.43}}
matched=[];legacy=[]
def metrics(rows):
    out={}
    for key in ['streamGpu','selectionGpu','solveGpu','windGpu','displayGpu','coreCullGpu','outerGpu','renderGpu','opaqueGpu','totalGpu',
                'streamCpu','selectionCpu','advanceCpu','poolCpu','handoffCpu','totalCpu']:
        v=sorted(row[key] for row in rows)
        out[key]={'mean':statistics.mean(v),'median':statistics.median(v),'p95':v[int(.95*len(v))],'p99':v[int(.99*len(v))],'max':v[-1]}
    return out
for name in ['legacy_fixedA','matched_fixedA','matched_fixedB','legacy_fixedB']:
    raw=json.loads((folder/(name+'.json')).read_text(encoding='utf-8-sig'))
    assert raw['passed'] and raw['dropped']==0 and raw['failed']==0 and len(raw['rows'])==300,name
    assert raw['substeps']==2 and raw['iterations']==1 and all(row['active']==32768 and row['ticks']==1 for row in raw['rows']),name
    case={k:raw[k] for k in ['caseName','gpu','api','unity','cpu','blades','colliders','width','height','msaa','substeps','iterations','gpuBufferBytes','middleFull','middleOuter','warmup','sampleTarget']}
    case['metrics']=metrics(raw['rows']);case['over1ms']=sum(row['solveGpu']>1 for row in raw['rows']);report['cases'].append(case)
    (matched if name.startswith('matched') else legacy).extend(raw['rows'])
report['matched']={'frames':len(matched),'over1ms':sum(row['solveGpu']>1 for row in matched),'metrics':metrics(matched)}
report['legacy']={'frames':len(legacy),'metrics':metrics(legacy)}
(here/'PerformanceRetest.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
m=report['matched']['metrics'];l=report['legacy']['metrics'];peak=m['solveGpu']['max'];over=report['matched']['over1ms']
table='\n'.join(f"| {r['caseName']} | {r['metrics']['solveGpu']['mean']:.3f} | {r['metrics']['solveGpu']['p95']:.3f} | {r['metrics']['solveGpu']['max']:.3f} | {r['over1ms']}/300 |" for r in report['cases'])
breakdown='\n'.join(f"| {label} | {m[key]['mean']:.3f} | {m[key]['p95']:.3f} |" for label,key in [
    ('GPU 求解＋近景重建','solveGpu'),('GPU 显示交接／重建','displayGpu'),('GPU 核心剔除','coreCullGpu'),('GPU 外围准备','outerGpu'),
    ('GPU 绘制区间','renderGpu'),('GPU 总计时区间','totalGpu'),('CPU Advance','advanceCpu'),('CPU 模拟池选择（包含在 Advance）','poolCpu'),
    ('CPU 交接管理（包含在 Advance）','handoffCpu'),('CPU 总提交区间','totalCpu')])
text=f'''# 移除重负载后的性能复测 — 2026-10-23

本次重新测量覆盖此前受其他程序 GPU 重负载影响的性能结论。功能和烘焙数据未修改。

RTX 3080 Ti，Ryzen7 5800X，Unity2022.3.62f2c1，D3D11，1600×900、4×MSAA。实验场景相同的 144m 流式核心、663552 根驻留草，中景 full/outer=52/64m，12 个碰撞体和外围表示均开启。**每帧强制32768根活跃XPBD、完整一个60Hz tick、2子步×1迭代、SoftBendCompliance=1.95**，不平均空闲帧。

同进程按旧库A→新库A→新库B→旧库B排列；每用例预热120帧、测量300帧。四组均通过计时完整性检查，dropped=failed=0。旧库控制组同样运行2×1＋F10，只改变中远景响应库，不是视觉正确性参考；正式效果以 matched 新库为准。

测试进程启动前 GPU 为 P8、21%利用率、210MHz、约37.85W；测试结束后 P8、210MHz、约38.43W。此前为99%、P0、约348W。本次没有关闭用户应用或更改驱动功率配置，仍属于桌面环境下的测量，不声称完全独占GPU。

## 满池求解＋几何重建

| 用例 | 均值 ms | P95 ms | 最大值 ms | 超过1ms |
|---|---:|---:|---:|---:|
{table}

正式新库合并600帧：均值 **{m['solveGpu']['mean']:.3f}ms**，P95 **{m['solveGpu']['p95']:.3f}ms**，最大值 **{peak:.3f}ms**；{over}/600帧（{over/6:.2f}%）超过1ms。**平均和P95达到1ms预算，但不是每帧都低于1ms。**

旧库控制组均值 {l['solveGpu']['mean']:.3f}ms；新库均值 {m['solveGpu']['mean']:.3f}ms，本轮没有观察到新增的求解均值成本。不能将单个用例的小幅波动当成稳定加速或退化。

## 正式新库的其他成本

下列均为新库600帧合并统计。求解预算仅指第一项；GPU总计时区间包含本测试场景相机工作，不代表整个游戏的全部GPU帧，也不能简单将各P95相加。绘制区间含碰撞体可视模型和文字等场景物件；CPU和GPU可重叠，不能直接相加为帧时长。

| 区间 | 均值 ms | P95 ms |
|---|---:|---:|
{breakdown}

GPU buffer 字节数仍为224852832（约214.44MiB），是显式缓冲统计，不是总VRAM。相较求解，接下来更值得关注显示交接／重建和CPU模拟池／交接管理；这次仅重测，不改实现。

原始逐帧JSON随主 Evidence.zip 归档，PerformanceRetest.json保留每组及合并的均值、中位数、P95、P99、最大值和所有区间。失败或重负载旧记录继续保留，不混入本次统计。

复现：运行 LowBudget/prepare.py 后，用 run.ps1 -Method ContactGpuBenchmark.Run -Name 新输出名；默认仍为上述四组顺序。完成后运行 summarize_retest.py 新输出名，再运行 summarize.py 更新主报告。
'''
(here/'PerformanceRetest.md').write_text(text,encoding='utf-8')
for path in [here/'summarize_retest.py',here/'PerformanceRetest.json',here/'PerformanceRetest.md']:
    meta=Path(str(path)+'.meta')
    if not meta.exists():meta.write_text('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\nDefaultImporter:\n  externalObjects: {}\n',encoding='utf-8')
print(table)
print('Matched full tick mean',round(m['solveGpu']['mean'],4),'P95',round(m['solveGpu']['p95'],4),'over1ms',over,'/600')
