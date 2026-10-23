# 已接受的 2×1＋F10：独立响应库与 LOD 匹配

2026-10-23。当前实验场景 `Assets/PBDGrass/Scenes/PBDGrassIterationBudget.unity` 默认进入 2×1＋F10，SoftBendCompliance=1.95。新库位于本目录 Baked，八类草型分别按自身原柔度乘 0.5 烘焙；60Hz、2 子步、每子步 1 轮。独立资产和 GUID，不覆盖旧库，不改变网格或塑性数据格式。

F5～F8 使用旧参考库，F9＋F10 使用新库；F10 关闭恢复旧参考库供原始 2×1 对照。当前新库只对应柔度比例 0.5，Inspector 改成其他比例时属于未匹配调校，界面显示 reference。各模式切换保留近景节点、速度、材料帧、塑性、池槽和历史；质量档切换时中远景曲线会变化，正常 LOD 始终使用所选档位的同一响应库。R 仍用于重置。

## 出生和风响应

新库以新的 XPBD 材料为参考，进行原有八类型验证。无风预热、稳定风、随机风、快速阵风、有限状态、烘焙收敛检查均通过原阈值，没有放宽阈值。25 个风样本逐类临时重烘焙，与独立新库的节点和控制点最大差均为 0。原始八类旧库的默认 8×3 重烘焙也均为 0，旧资产哈希保持不变。

下表是每类 32 XPBD 对 32 Bezier 的比较；3120 ticks 的连续风测试含强制方向变化。均值不代表每一根叶子或任意风速都精确一致。

| 类型 | 无风出生后叶尖漂移 mm | 随机风平均叶尖差 cm | 随机风最大差 cm | 快速阵风最大差 cm |
|---|---:|---:|---:|---:|
| Short / Narrow / Firm | 0.026 | 1.04 | 1.77 | 11.21 |
| Short / Narrow / Soft | 0.103 | 1.03 | 2.05 | 11.32 |
| Short / Wide / Firm | 0.022 | 1.04 | 2.06 | 11.50 |
| Short / Wide / Soft | 0.094 | 1.05 | 2.53 | 11.64 |
| Tall / Narrow / Firm | 0.180 | 1.49 | 2.33 | 6.54 |
| Tall / Narrow / Soft | 0.473 | 1.63 | 4.52 | 6.74 |
| Tall / Wide / Firm | 0.266 | 1.58 | 4.41 | 7.33 |
| Tall / Wide / Soft | 0.337 | 2.06 | 5.57 | 11.98 |

无风 Bezier 与 XPBD 的最大节点差约 2.35 cm，包含曲线拟合和按段长重建误差。出生后最大 XPBD 叶尖漂移小于 0.5 mm，避免旧库与低预算物理不匹配引起的出生后二次弯折。八类测试是独立均匀草束，仍需按具体游戏风场和碰撞分布观察。

## 塑性、状态交接与资源

- 联合负载：32768 XPBD＋98304 Bezier，1440 ticks；发生 1972 次草格退出、1082 次重入。位置、速度、材料帧与角速度初始化最大误差均为 0；采样的被压倒草叶塑性保存误差为 0。
- 实际 compact/event 生命周期：空闲零模拟、接触唤醒、2 秒继续模拟、1 秒边模拟边过渡、回收、重接触、塑性保存、流式重叠保留、卸载清除、32768 上限及外围覆盖互补均通过原断言。回收瞬间最大叶尖步变 1.02 mm；过渡期间最大单帧步变 8.81 cm。后者包含正在进行的物理运动，不等同于表示切换误差。
- compact 冷进入和暖历史进入专项：最大位置差 0 m、速度差 0 m/s、材料帧角差 0 度、角速度差 0 rad/s。冷区速度仍是原有估计值；这里验证的是按当前表示播种的连续性，没有声称恢复未知历史。
- 在确有活跃 XPBD 和非零塑性的场景中切换旧／新库，节点、速度、材料帧、角速度和塑性逐元素不变，初始化次数和 tick 不变，renderer 对象和 GPU buffer 字节数不变；外围 GPU 控制点逐元素匹配所选库。
- 切库仅上传 75 个控制点、175 个材料帧、频率／阻尼和塑性力矩补偿；复用现有 GPU 缓冲，不增加常规帧内 dispatch，不做运行时 GPU 回读。仅允许相同几何、质量、厚度及风范围的响应库互换。

## 性能

### 移除其他程序重负载后的最新复测

完整四组交错对照通过，新库600帧：求解＋近景重建均值 **0.662ms**、P95 **0.863ms**、最大 **1.383ms**，超过1ms为14/600帧。均值和P95在预算内，仍有偶发尖峰。详见 [最新性能复测](PerformanceRetest.md)。以下保留旧高负载测试，未混入新统计。

### 此前的高负载记录


3080 Ti / D3D11，ContactLab 同等范围和碰撞体，固定 32768 活跃 XPBD；每帧一个完整 60Hz tick。120 帧预热、每轮 300 帧。求解包含所有子步和近景几何重建。交接、绘制与 CPU 提交分别列出；不是整帧成本，也未用空闲帧或更少草叶降低均值。

此前计时受到既有 GPU 工作负载影响：在 PairedPerformance 的测试 Unity 进程启动前，nvidia-smi 已显示 GPU 利用率 99%、P0、1845 MHz、约 348 W。首次两次新库测量求解为 15.74 / 17.32 ms，交接及绘制也同时大幅放大。因此增加同进程旧／新库交错对照，下表仅用于记录该负载下的相对表现，不作为独占 GPU 耗时或 1ms 验收。没有关闭用户应用。

legacy 与 matched 都运行新的 2×1＋柔度×0.5；legacy 仅使用旧响应库作为计时控制，不能作为正确的风响应配置。计划顺序为旧 A、新 A、新 B、旧 B；新 B 轮在 298/300 样本时发生 Timestamp drain incomplete，旧 B 没有执行。因此对照未完整结束，下表只有旧 A、新 A 的完整样本；失败轮不计算均值或用于验收，其原始记录保留。

| 用例 | 求解均值 ms | 求解 P95 ms | 显示交接均值 ms | 绘制提交区间 GPU 均值 ms | CPU Advance 均值 ms |
|---|---:|---:|---:|---:|---:|
| legacy_fixedA | 11.151 | 20.346 | 13.874 | 16.974 | 3.568 |
| matched_fixedA | 11.600 | 29.960 | 15.293 | 17.562 | 3.734 |

详细剔除、外围、GPU buffer 字节和 CPU 管理指标见 Summary.json。独立响应库校验完成不等同于全部场景下稳定满足 1ms；按表中的均值与尾部耗时评估。

## 文件和复现

- 主场景：`Assets/PBDGrass/Scenes/PBDGrassIterationBudget.unity`，绿 XPBD／蓝兼容贝塞尔／橙外围。
- 八类型直观比较：`Assets/PBDGrass/Scenes/PBDGrassLowBudgetResponse.unity`，左右分别为同一新材质的 XPBD 和 Bezier。
- `BankManifest.json`：8 个新资产的 GUID、配置和 SHA256。
- `GrassLowBudgetBaker.Bake`：生成独立新库；旧 Bake 菜单仍生成旧配置。`ComputeType` 的默认参数保持 8×3。
- `GrassLowBudgetResponseValidation.Run`：八类型风响应和新库临时重烘焙。`GrassLowBudgetIntegrationValidation` 的 Lifecycle、Combined、Seeds、Switching 调用原场景断言或专项检查；两个既有验证器的源码没有放宽。
- `prepare.py`：安装 GPU 时间戳到隔离工程；`run.ps1` 指定方法和新的输出 Name；均不访问 Library。Seeds 首次使用过小核心场地而在初始化失败，Seeds64 修正为支持的 64m 流式核心后完成验证，旧失败记录保留在 Evidence.zip。
- `Summary.json` 和 `Evidence.zip` 保存原始结果、截图和复现源。同步回读仅用于离线校验，与性能采样分开。

原 ContactLab、联合 baseline 场景、求解器方程、旧八类资产保持原样。本轮没有 commit 或 push。

当前 CPU 管理已进一步改为共享覆盖与事件调度，固定 32768 根的完整 tick 从 1.711ms 降到 0.622ms，详见 [CpuEventDriven/Results.md](D:/UnityProject/GrassFinal/Assets/PBDGrass/ResponsePrototype/LowBudget/CpuEventDriven/Results.md)。此前的碰撞体分散与局部更新记录保留在 [CpuIncremental/Results.md](D:/UnityProject/GrassFinal/Assets/PBDGrass/ResponsePrototype/LowBudget/CpuIncremental/Results.md)，不能混用不同版本的数据。
