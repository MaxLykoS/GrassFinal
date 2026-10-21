# Compact 草场 CPU 内存优化

2026-10-21。保持 Contact Lab 的 32768 根活动 XPBD、144×144 米驻留窗口、663552 根驻留容量、中景 52～64 米、外围 256 米、12 个碰撞体及既有物理参数。优化仅作用于 Compact 流式初始化和 CPU 数据表示；原联合 baseline 及非 Compact 路径继续使用原有生成方式。

## 结果

Unity 2022.3.62f2c1、D3D11、RTX 3080 Ti / Ryzen 7 5800X；在 D 盘隔离工程复测。前后使用相同内存诊断流程：默认视角、1600×900 HDR / 4× MSAA，180 个静止 tick 加 600 个移动 tick，始终 32768 根活动草。主动 GC 只用于检查点，不用于帧耗时测试。

| 指标 | 优化前 | 优化后 |
| --- | ---: | ---: |
| 草系统已识别常驻 CPU 数组载荷 | 973.19 MiB | **3.72 MiB** |
| 初始化结束、主动 GC 前的托管已用总量 | 2886.26 MiB | 13.25 MiB |
| 移动测试后 GC 的托管已用总量 | 994.45 MiB | **8.94 MiB** |
| Mono 堆容量 | 2937.25 MiB | 17.79 MiB |
| 移动后进程工作集 | 3605.74 MiB | 548.00 MiB |
| 进程峰值工作集 | 4006.62 MiB | 548.02 MiB |
| 草系统 GPU Buffer 逻辑容量，含外围 | 214.44 MiB | **214.44 MiB** |

草系统数组减少 **约 99.6%**。优化后数组合计 3900352 B；全场节点、杆段、顶点、法线、UV、索引、元数据和逆质量的常驻展开数组均已移除。余下主要是格位置/方向、世界换页表、模拟池与 LOD 管理数组。单次初始化（含外围 Buffer 创建）约 620.84 ms，这是本次样本，未建立初始化耗时的多次统计。

这些口径不可相加：数组载荷不含对象头；托管总量包含隔离 Editor 和诊断脚本；工作集包含 Editor、原生资源与驱动映射。GPU Buffer 数值不含纹理、RT、驱动开销，不是总显存。本轮未以发行 Player 验证上述进程总量。旧 Editor 进程已扩张的堆可能仍保留；比较峰值应使用新启动的进程。

## 为什么原来大，以及如何处理

旧 Compact 路径仍经由全场 `PBDGrassPatch`：先生成 663552 个 Body 和完整 Mesh，随后按世界身份重新生成 Body，再复制出 `GrassBladeData` 的节点、杆段和各类网格数组。GPU 使用压缩模板，CPU 却保留约 969 MiB 展开数据；初始化还产生大量临时对象。

现在 `GrassWorldLayout.CreateCompactData` 只构造 **32 根草的小样本**，按旧路径相同的世界根位置、旋转和运算顺序提取模板。根方向仍在 CPU 按原顺序计算，避免改用另一种浮点计算方式后放大碰撞差异。最大草长仍通过不分配 Body/网格的逐叶计算保持原有剔除和交互范围，因此初始化尚有一次随驻留数量增长的计算，但不再有随草叶展开的大数组分配。

`GrassBladeData` 在 Compact 模式只保留格表及模板引用，根位置、ID、种子、节点和杆段通过索引按需生成。身份数据使用 4096 根的临时批次上传 GPU，不在 CPU 保留完整副本。压缩塑性清零使用最多 1 MiB 的零数据分批上传，避免每次创建约 20 MiB 的临时数组。

正常帧、换页、重置只读取数量和格级信息；物理仍在 GPU 上。没有加入同步状态回读，没有修改 GPU 求解、压缩格式、烘焙资产或光照。

## 诊断接口

- `GetBlade`、`GetRestNode`、`GetRod`、`GetInverseMass` 返回单项值，不展开整场。已有探针和编辑器验证改用这些接口。
- Compact 模式下，旧 `Blades`、`RestNodes`、`Rods` 数组属性生成调用者持有的完整诊断快照；系统不会缓存它们。整场快照仍会产生较大临时分配，正常帧不要调用，更不要在循环里反复获取属性。
- Compact 模式下 `InverseMass` 以及旧 CPU 网格数组为空；质量使用索引访问，渲染几何使用 GPU 诊断读取。非 Compact 路径原有数组及其行为保持兼容。
- `ReadNodes` / `ReadFrames` 等整场诊断仍可使用，输出内存由调用者负责；不能把诊断快照峰值当成正常常驻内存。

## 正确性与性能检查

1. 独立旧路径对照：600 组模拟池用例、2400 tick（Contact Lab 1800＋原联合场景 600）、12 次换页，活动量始终 32768；14 个 GPU 检查点的节点、方向、塑性、最终顶点/法线、压缩姿态及分类/风状态 **逐位一致**。包括碰撞与释放、往返、远距离传送、预热模式切换和 Reset。旧实例仍从整场 Patch 生成，不与新实例共享初始模拟状态。
2. Contact Lab Play 模式完整 2701 tick，12 个碰撞体、9 次换页、倒伏进入中景再返回及四个视角全部通过；倒伏标记往返前后均约 1.0879，最大近景节点距离 1.58449 米。
3. 原联合 baseline：32768 XPBD＋98304 贝塞尔，1440 tick 通过；播种位置/速度/方向/角速度及塑性保留误差为零，Scene 相机行为保持。八种材料临时重烘焙的节点、控制点、方向、频率和阻尼差异均为零，已接受的烘焙资产未改变。
4. 最终版默认视角性能抽查：预热 120 tick 后采样 300 个完整 tick，CPU 池选择 0.585 ms、LOD 管理 1.151 ms、完整 tick CPU 提交 1.873 ms，草场与相机总提交 2.158 ms。GPU 求解＋近景重建 2.971 ms，中景更新 0.644 ms，无失败/丢弃样本。未更改 GPU 算法，不把时钟或系统负载波动算作求解器优化收益；**1 ms GPU 目标仍未达成**。

内存、独立对照、Play 表现和联合验证正常完成。性能采样写出全部 300 个成功样本后，其隔离 Unity 进程在退出阶段挂起超过四分钟，随后仅终止该测试进程；外层启动脚本因此返回非零退出码。采样结果与退出清理异常分别记录在 `Evidence.zip` 的性能 JSON 和 `PerformanceShutdown.json`，未操作用户打开的其他 Editor。

## 证据与复现

- `Summary.json` 汇总内存、正确性和性能结果；`Evidence.zip` 保存前后内存 JSON、对照/Play/联合验证日志与结果、300 个逐帧性能样本、内存测试源码。
- `Reference.zip` 保存本轮修改前的八个核心源文件；原 CPU 对照工具的参考实现此前已通过与该版本的逐位比较。
- `Sources.json` 记录最终代码和场景哈希，并核对本轮未改的 GPU shader、场景、材质及烘焙资源。
- 先运行 `../CpuOptimization/prepare_cpu.py parity`，再按原 `CpuParity.Run` 流程做新旧对照；内存脚本在 `Evidence.zip` 的 `GrassMemoryAudit.cs`，只复制到隔离工程的 `ContactProfileTemporary/Editor/`，设置 `GRASS_MEMORY_OUTPUT` 后执行 `GrassMemoryAudit.Run`。
- 表现与烘焙验证分别执行 `GrassFinal.PBDGrass.Editor.GrassShowcaseValidation.Run`、`GrassFinal.PBDGrass.Editor.GrassCombinedBaselineValidation.Run`；性能沿用 `ContactGpuBenchmark.Run`。

更细的优化前分项保留在 [MemoryAnalysis/Memory.md](../MemoryAnalysis/Memory.md)。本轮没有压缩或缩小 GPU 中景/历史池/外围 Buffer，显存仍可作为独立方向继续处理。
