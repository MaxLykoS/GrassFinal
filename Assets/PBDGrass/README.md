# PBD 草工作目录

后续 PBD 草的生成、模拟和渲染在此目录迭代。目录外的原代码和资源作为 Archive 保留，没有移动或修改。

## 运行入口

在 Unity 2022.3、Built-in Render Pipeline 下打开 `Scenes/PBDGrassDemo.unity`，等待导入后进入 Play Mode。
场景引用本目录的 `Prefabs/PBDGrassDemo.prefab`，已接好草材质、Compute Shader、风噪声、相机、Hi-Z 深度生成和两个交互球。

- WASD / 方向键：移动相机；右键：观察；Shift：加速；滚轮：前后移动。
- O / P：移动两个交互球，观察草的碰撞与恢复。
- GrassSize 为原场景的 128；每格 32 根，共 524,288 根草。可以在新 prefab 的 GrassDemo 上调整。
- 当前实现使用静态渲染状态；一个场景只放一个此 prefab，并保持根对象位于原点、单位缩放。
- GPU 路径要求 Compute Shader、间接绘制和曲面细分支持，建议使用原工程的 Windows 图形环境。

## 全流程与目录

1. `Demo/GrassDemo.cs`：初始化、风参数、交互球数据和每帧调度。
2. `Runtime/Body/`、`Runtime/Math/`、`Runtime/Constraints/`：草叶生成、节点状态及约束数据。
3. `Runtime/PBDGrassPatchRenderer.cs`：GPU 缓冲、LOD 调度、剔除与间接绘制。
4. `Shaders/PBDSolverCS.compute`：近远 PBD 求解、球体碰撞、顶点/法线重建、网格 LOD 与剔除。
5. `Runtime/Rendering/DepthTexMaker.cs`、`Shaders/HZBMaker.shader`、`Materials/DepthTexMaker.mat`：用于草地遮挡剔除的深度金字塔。
6. `Shaders/PBDGrassShaderDrawProcedual.shader`、`Materials/PBDGrassProcedualMaterial.mat`：当前 GPU 路径的细分与着色。
7. `Textures/Wind.png`：独立复制的风场噪声及原导入设置。

`Runtime/PBDSolver.cs` 和相关碰撞类保留 CPU 求解参考及当前渲染器所用的求解参数。
`PBDGrassShader.shader` / `PBDGrassMaterial.mat` 保留 PBD 网格着色版本，不是演示场景默认的 GPU 绘制路径。
相机控制和交互球控制仅用于独立检查 PBD 草；没有复制角色、UI、后期、非 PBD 草或大型环境模型。

## 本轮隔离改动

- C# 类型统一放入 `GrassFinal.PBDGrass` 命名空间，并使用独立 asmdef；`autoReferenced` 关闭，旧脚本不自动引用新程序集。
- Shader 名称使用 `PBDGrass/Workspace/` 前缀，所有资产和目录获得新 GUID；新材质和场景引用新资源。
- 深度生成器删除向旧 `GrassInstancing` 发布深度图的语句，仅服务新 PBD 渲染器。
- 原脚本的 UTF-8 / Windows 中文编码在新副本中统一为 UTF-8，原文件字节保持不变。
- 演示场景使用独立 prefab、内置平面和球体，重建必要参数与连接；没有携带旧场景的角色、UI或烘焙数据。
- 草生成算法、PBD 力与约束、GPU 内核、LOD、细分、着色和原始时间调度保持原样。

## 后续迭代记录

已知的时间步、近距离细分成本、静态状态、资源释放方式和遮挡剔除问题留待后续显式升级，本轮没有修正这些旧行为。
`ExtractionManifest.json` 记录原文件路径与哈希，用于追踪副本来源。

## 提取验证（2026-10-13）

- 使用已安装 Unity 2022.3 的引擎与 .NET Standard 2.1 引用，独立编译全部 16 个 C# 脚本；UNITY_EDITOR 与播放器条件分支均通过。
- 检查 43 个新资产/目录 GUID，全部唯一且与旧 Assets 中的 GUID 无冲突。
- 场景、prefab 和材质通过 YAML 解析；13 个非 Unity 内置资源引用全部解析到本目录，prefab 层级与组件引用一致。
- 24 个副本与原内容比对通过，只包含上述隔离改动；51 个原始源文件与元数据的 SHA-256 保持不变。
- 未启动 Unity、未执行 Shader/GPU 渲染或 Play Mode 验证，未访问项目 Library；编译输出和检查工具仅放在系统临时目录。
