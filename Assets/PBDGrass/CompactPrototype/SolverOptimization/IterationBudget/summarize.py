"""Archive the iteration experiment, keeping accuracy and lifecycle conclusions separate."""
from pathlib import Path
import json
import statistics
import subprocess
import uuid
import zipfile

here=Path(__file__).resolve().parent
workspace=here.parents[4]
results=Path('D:/CodexTemp/codex-grass-contact-profile/IterationResults')
summary={'date':'2026-10-22','performance':[],'quality':{},'validation':{},
    'scope':'Experimental accuracy tradeoff; original scenes, solver equations and bake assets unchanged.'}
for folder in ['Performance','PerformanceLow','FinalPerformance']:
    for path in sorted((results/folder).glob('*.json')):
        r=json.loads(path.read_text(encoding='utf-8-sig'))
        assert r['passed'] and len(r['rows'])==300
        assert all(row['active']==32768 and row['ticks']==1 for row in r['rows'])
        out={k:r[k] for k in ['caseName','gpu','api','substeps','iterations','gpuBufferBytes','dropped','failed']}
        out['run']=folder;out['samples']=len(r['rows']);out['metrics']={}
        for name in ['solveGpu','displayGpu','coreCullGpu','outerGpu','renderGpu','totalGpu','advanceCpu','poolCpu','handoffCpu','totalCpu']:
            values=sorted(row[name] for row in r['rows'])
            out['metrics'][name]={'mean':statistics.mean(values),'median':statistics.median(values),'p95':values[int(len(values)*.95)]}
        summary['performance'].append(out)
summary['quality']=json.loads((results/'Quality/Results.json').read_text(encoding='utf-8-sig'))
assert summary['quality']['passed']
for folder in ['Combined8x1','Combined4x2','Lifecycle8x1','Lifecycle4x2','Scene']:
    r=json.loads((results/folder/'Results.json').read_text(encoding='utf-8-sig'))
    assert r['passed'],folder
    summary['validation'][folder]=r
summary['validationMeaning']='Combined passes LOD/plastic/bank-integrity assertions; the isolated copy collects all 8 rebake deviations without the original near-identity thresholds. These reports do NOT establish identical bake compatibility.'
for relative in ['Assets/PBDGrass/Scenes/PBDGrassContactLab.unity','Assets/PBDGrass/Scenes/PBDGrassCombinedBaseline.unity',
                 'Assets/PBDGrass/CompactPrototype/Shaders/CompactGrassSolver.compute','Assets/PBDGrass/Runtime/XPBDGrassSettings.cs']:
    actual=(workspace/relative).read_bytes().replace(b'\r\n',b'\n')
    expected=subprocess.check_output(['git','show','HEAD:'+relative],cwd=workspace).replace(b'\r\n',b'\n')
    assert actual==expected,relative
(here/'Summary.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
latest=[r for r in summary['performance'] if r['run']=='FinalPerformance']
table=[]
for quality in summary['quality']['cases']:
    name=quality['name'];rows=[r for r in latest if r['caseName'].startswith('q'+name+'_')]
    mean=statistics.mean(r['metrics']['solveGpu']['mean'] for r in rows)
    p95=max(r['metrics']['solveGpu']['p95'] for r in rows)
    wind=quality['rows'][0];tail=quality['rows'][-1]
    table.append(f"| {name} | {mean:.3f} | {p95:.3f} | {wind['tipDifferenceRms']*100:.2f} | {tail['plasticMean']:.5f} |")
report='''# XPBD 迭代预算实验（2026-10-22）

最新状态：默认已固定为 2×1＋F10，并接入独立匹配响应库，详见 [新库与 LOD 验证](../../../ResponsePrototype/LowBudget/Results.md)。下文为此前实验记录，默认档位描述以当时为准。

2026-10-23 更新：F9 增加了 F10 可切换的支撑调校，详见 [调校报告](Tuning/Results.md)。以下保留调校前的原始测量记录。

已创建 `Assets/PBDGrass/Scenes/PBDGrassIterationBudget.unity`。默认使用用户已接受的 3×1；F5 原版 8×3，F6 为 8×1，F7 为 4×2，F8 为 3×1，F9 为 2×1。高迭代路径保留，只有选中对应档位才执行。切换保留现有物理状态，按 R 重置草及碰撞体运动后观察。原 ContactLab、联合 baseline、全局默认参数、求解器和烘焙资产不变。

## 同轮性能和质量

RTX 3080 Ti、Unity 2022.3.62f2c1、D3D11。每帧一个完整 60 Hz tick，固定 32768 根活跃 XPBD，12 个碰撞体，中远景照常参与渲染。每档预热 120 帧、采样 300 帧，正序/逆序各一次。GPU 时间包含全部子步求解和近景几何重建，不含显示交接、剔除与绘制；这些项目及 CPU 提交时间单独保存在 Summary.json。没有用空闲帧或减少草数量降低平均时间。

以下均来自 FinalPerformance，同档平均两轮；P95 取两轮中较高值。不同测试轮次的原版均值分别约 4.05、3.01、3.40 ms，因此只做同轮比较，不混用最快结果。

| 子步×迭代 | 求解均值 ms | 较高轮次 P95 ms | 无碰撞叶尖 RMS 偏差 cm | 撤去碰撞 4 秒后平均塑性弯角 rad |
|---|---:|---:|---:|---:|
'''+ '\n'.join(table)+'''

8×1 相对本轮原版节约约 29%，4×2 约 55%。3×1 均值接近 1 ms，但没有 P95 余量，并有明显姿态变化；2×1 平均时间低于预算，P95 仍超过 1 ms，且无碰撞姿态偏差更大。不能据此宣称满足了原有质量要求下的 1 ms 目标。这里只减少求解次数，仍使用原有 float32 计算。

## 质量测量的边界

Quality 对每档运行 720 ticks：先 3 秒风场，再 5 秒多碰撞体，最后 4 秒撤去碰撞。固定模拟 32768 根，每隔 8 根采样，共 4096 根；在 8 个检查点读取节点、材料帧和塑性。同步读取只用于离线质量分析，不进入运行时代码或性能计时。样本有限，不代表已覆盖所有高速运动和极端碰撞。

叶尖偏差以原版同世界草叶、同时间点为参照。碰撞会改变倒伏方向和塑性路径，故不以逐点相同为通过条件。8×1 撤去碰撞后平均塑性与原版相近；4×2 约少 29%，3×1 约少 40%，2×1 约少 51%。这是本测试的采样统计，不能等同于任意踩踏下的视觉倒伏幅度。

所有候选采样状态均有限。无碰撞长度误差较小，但多碰撞体重叠区域存在离群拉伸和残余穿透，原版也存在；4×2 曾出现约 9.96 倍的最大相对段长误差，不能只看 P95 宣称无拉伸问题。穿透测量排除了不可移动根部所在的第 0 段；穿透 P95 只统计正穿透样本。原始各时刻均值、P95、最大值完整保留。

`Evidence.zip` 内含全部八档接触末尾与释放后的截图。已人工对照原版与 4×1 的释放截图：整体场景结构保留，但局部倒伏形状不同。静态截图不能证明动态视觉体验相同。

## 交接及烘焙匹配

8×1、4×2 均通过现有生命周期断言：空闲零模拟、2 秒继续求解、1 秒继续求解并过渡、回收、重接触不重置、塑性保存、流式重叠保留、卸载清除、32768 上限、外围覆盖连续。回收时最大叶尖步变均约 1.14 mm；渐变期间最大单帧叶尖步变分别约 3.90 cm / 3.10 cm。

两档均跑完联合场景的 32768 XPBD + 98304 Bezier、1440 ticks，相机移动、LOD 出入、塑性保存和精确初始状态交接断言通过。八种类型均做了临时重烘焙；未写回已接受资产。由于本实验允许精度变化，隔离工程中的比较器保留全八类误差记录，不使用原来的近似逐点相同阈值中途终止。原比较器源码没有修改。

| 档位 | 八类最大节点偏差 | 最大控制点偏差 | 最大材料帧角差 | 拟合角频率向量最大差 | 拟合阻尼比向量最大差 |
|---|---:|---:|---:|---:|---:|
| 8×1 | 4.98 cm | 6.11 cm | 3.31° | 5.15 rad/s | 0.72 |
| 4×2 | 3.13 cm | 3.13 cm | 2.47° | 4.51 rad/s | 0.12 |

因此数据格式兼容、状态交接流程通过，不代表旧贝塞尔响应与低迭代 XPBD 动态完全匹配。尤其拟合频率和阻尼有变化。暂时作为独立可观察实验，后续选定档位后再决定是否为它生成独立响应库；未覆写旧烘焙来掩盖差异。3×1、2×1 尚未进行完整八类型重烘焙及生命周期验收。

## 文件与复现

- `prepare.py`：从生产源创建隔离测试代码。只在隔离 renderer 注入环境变量覆盖；性能用例自行指定子步/迭代。
- `run.ps1`：串行启动隐藏的 Unity batchmode。Method 可选 ContactGpuBenchmark.Run、IterationQuality.Run 以及现有 Lifecycle/Combined 验证；输出到 D:/CodexTemp/codex-grass-contact-profile/IterationResults。指定新的 Name 防止覆盖。
- `IterationQuality.cs.txt`：离线采样器。
- `GrassIterationBudgetDemo.cs`：唯一新增运行时脚本，仅挂在新实验场景中；不分配模拟资源、不读回 GPU。
- `install_scene.py` 与 `BuildIterationScene.cs.txt`：安装并生成场景；重新打开序列化场景并测试每档一次完整 tick。当前场景保存档位为 3×1；此前性能与质量测试记录保持原样。
- `Summary.json`、`Evidence.zip`：全部计时、质量、回归和截图证据。Quality 的 passed 表示采样完成且状态有限；Combined 的 passed 含义见上文，不表示通过旧烘焙的数值等价阈值。

没有提交或推送本轮实验。
'''
(here/'Results.md').write_text(report,encoding='utf-8')
with zipfile.ZipFile(here/'Evidence.zip','w',zipfile.ZIP_DEFLATED) as archive:
    for path in sorted(results.glob('*/*')):
        if path.suffix=='.json' or (path.suffix=='.png' and path.parent.name=='Quality'):
            archive.write(path,'results/'+str(path.relative_to(results)))
    for path in sorted(here.iterdir()):
        if path.suffix in ['.py','.ps1','.cs','.txt','.md','.json']:archive.write(path,'reproduction/'+path.name)
for path in [here,*here.iterdir()]:
    if path.suffix=='.meta' or path.name=='__pycache__':continue
    meta=Path(str(path)+'.meta')
    if not meta.exists():meta.write_text('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\n'+
        ('folderAsset: yes\n' if path.is_dir() else '')+'DefaultImporter:\n  externalObjects: {}\n',encoding='utf-8')
print('\n'.join(table))
print('Original scenes, solver, settings verified unchanged. Evidence saved.')
