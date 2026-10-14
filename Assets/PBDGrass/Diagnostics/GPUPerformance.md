# RTX 3080 Ti：当前 Demo GPU 性能重评估

结论：32,768 根全部参与模拟时，每个 60 Hz tick 的**求解＋几何重建 GPU 平均 2.127 ms**；1080p 主相机整帧 GPU 工作平均 **2.577 ms**。CPU 模拟提交平均 0.652 ms。
这些是直接 GPU 时间戳测量，替代此前“批次提交＋节点回读＋CPU 整理/校验，除以 30”的约 5 ms 口径。

## 测量配置

- GPU：NVIDIA GeForce RTX 3080 Ti；CPU：AMD Ryzen 7 5800X 8-Core Processor；Unity：2022.3.62f2c1，Direct3D11，Windows Mono Development Player。
- 实际加载当前 `Scenes/PBDGrassDemo.unity`：32 × 32 米、每平方米 32 根、生成与激活均为 32,768 根。8 节点、7 段、8 子步，每子步 3 次耦合扫掠。
- 相机保持 prefab 的默认位置 `(0, 2.5, -8)`、默认旋转、60 度 FOV。风、重力、两个静止交互体及材质均保持 Demo 默认值。
- 原工程 Ultra 画质：4× MSAA、HDR 相机、Gamma 色彩空间；目标为 1920 × 1080 的 HDR 离屏纹理。绘制使用主相机剔除后的可见草，模拟始终覆盖全部 32,768 根。
- 关闭 VSync / 帧率上限，热身后每轮取 600 帧；默认配置独立重复一次，共 1,200 个基线样本。
- 固定负载阶段每个渲染帧恰好推进一个 1/60 秒模拟 tick，避免用高 FPS 下大量“没有模拟 tick”的帧稀释模拟成本。
- 另取 900 帧实时 60 Hz 物理 / 不限渲染帧率对照；两种模式不能混为同一性能口径。

## 默认配置 GPU 与 CPU

| 项目 | 平均 | P95 |
|---|---:|---:|
| GPU 求解＋几何重建 / tick | 2.127 ms | 2.480 ms |
| GPU 主相机剔除 | 0.009 ms | 0.009 ms |
| GPU 不透明绘制：草＋地面＋交互体 | 0.209 ms | 0.210 ms |
| GPU Hi-Z 金字塔生成 | 0.136 ms | 0.137 ms |
| GPU 整帧工作，含一个完整模拟 tick | 2.577 ms | 2.936 ms |
| CPU 模拟准备与提交 / tick | 0.652 ms | 0.774 ms |
| 上一项中 CPU 模拟池选择、数组整理与上传 | 0.614 ms | 0.732 ms |
| CPU 主相机剔除准备与提交 | 0.018 ms | 0.022 ms |
| CPU Hi-Z 命令准备与提交 | 0.079 ms | 0.095 ms |

CPU 与 GPU 时间可以重叠，不能直接相加得到帧时间。GPU 整帧区间还包含清屏、深度、解析/拷贝及提交间隙，因此不会严格等于表中四个 GPU 子项之和。
P95 表示 95% 样本不超过该数值，并非最大值。

## 对照实验

| 配置：其他参数保持一致 | GPU 整帧平均 | GPU 不透明绘制平均 |
|---|---:|---:|
| 默认主相机，第一轮 | 2.572 ms | 0.207 ms |
| 只关闭草的绘制，保留全部模拟与剔除 | 2.363 ms | 0.043 ms |
| 只禁用 Hi-Z 组件 | 2.379 ms | 0.211 ms |
| 主相机＋第二个 SceneView 类型观察相机 | 2.722 ms | 主相机 0.211 / 观察相机 0.093 ms |
| 默认主相机，重复轮 | 2.582 ms | 0.211 ms |

草绘制的不透明阶段增量约 **0.166 ms**，来自默认配置与“只关闭草绘制”的阶段对照，适用于当前相机可见范围。
增加观察相机时，总 GPU 工作增加约 **0.145 ms**，模拟仍只执行一次。观察相机也使用主相机剔除，位置为 `(18,16,-22)`，朝向草田中心；不包含 Unity 编辑器的网格、Gizmo 和界面。
Demo 的 `UseOcclusionCulling=false`，但 DepthTexMaker 仍生成金字塔。禁用该组件使整帧平均减少约 0.198 ms；差值也包含它的图像处理链变化，不能全部归为金字塔内核。

## 求解与重建的边界

几何重建位于最后一个模拟子步的同一计算内核中，因此精确测量值是“求解＋重建”。前 7 子步平均每步 0.252 ms，最后子步含重建为 0.363 ms。
两者之差约 0.111 ms，只能作为重建增量的估计，因为不同子步的接触/约束分支也可能不同。本次没有拆开内核或修改物理质量参数。

## 实时物理对照

不限离屏渲染帧率、物理按实际时间运行 60 Hz 时，900 帧中只有 29 帧执行模拟，871 帧没有 tick。
该阶段 GPU 整帧平均 0.519 ms，但包含大量不做模拟的帧；实际执行 tick 的样本仍约 2.121 ms。评估求解器预算应使用固定负载表中的每 tick 值。

## 计时与适用范围

使用临时原生插件，在 Unity 渲染线程上提交 D3D11 `TIMESTAMP` 与 `TIMESTAMP_DISJOINT` 查询。结果在后续帧以 `D3D11_ASYNC_GETDATA_DONOTFLUSH` 检查一次；未完成则留到之后，不进行等待循环或强制 flush。
没有调用 `ReadNodes`、`ReadFrames`、`VisibleGridCount`、`ImpulseBlade`，也没有读取节点、速度、材料方向或间接参数缓冲区。
共取得 3900 个有效 GPU 样本，时间戳失效/丢弃 0，插件设备/查询失败 0。

为保持隐藏运行而明确提交相机绘制，本测试使用离屏 Camera.Render。数值覆盖当前 Demo 的实际计算和渲染工作，排除窗口呈现、编辑器 UI 与其他项目内容；不能据此直接宣称编辑器或发行版的实际 FPS。
预热后遥测显示 GPU 为 P0，主要 SM 时钟约 2010–2025 MHz。结果对应当前机器、持续负载状态和测试视角。

当前最高 GPU 成本是求解＋重建；CPU 模拟提交中约 94.2% 花在模拟池选择/更新。固定草田、固定交互中心仍每 tick 排序并上传同一批格，是明确的后续优化对象。未启用遮挡剔除时仍生成 Hi-Z，也存在可直接移除的工作。
本轮只测量，没有修改源工程求解器、Demo 参数或 Archive。

- [汇总和源文件哈希](GPUPerformanceSummary.json)
- [全部逐帧采样](GPUPerformanceRaw.json)
- [GPU 遥测](GPUPerformanceTelemetry.csv)
- [临时测量代码与原生插件复现包](GPUPerformanceReproduction.zip)：仅供独立临时工程使用，不是当前工程依赖。

计时方法参考：[Microsoft D3D11 查询](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/ne-d3d11-d3d11_query)、[非阻塞查询标志](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/ne-d3d11-d3d11_async_getdata_flag)、[Unity 原生渲染插件接口](https://github.com/Unity-Technologies/NativeRenderingPlugin)。
