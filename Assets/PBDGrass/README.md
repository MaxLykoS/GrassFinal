# 近景 XPBD 草工作目录

此目录是当前生成、模拟和渲染的开发入口；目录外的原有代码和资源继续作为 Archive。本版只实现近景，没有接入中景或远景。

当前已接受版本作为优化前 baseline，配置与复现约定见 [OptimizationBaseline.md](Diagnostics/OptimizationBaseline.md)，Git 标签为 `baseline/xpbd-grass-pre-optimization`。

当前工作版本在该 baseline 上加入长叶建模和有限弯曲塑性，说明与实测见 [PlasticGrassValidation.md](Diagnostics/PlasticGrassValidation.md)。baseline 标签及其历史报告保持不变。

2026-10-15 用户确认长叶塑性倒伏效果，并指定后续硬预算：**RTX 3080 Ti、32768 根活动草，每个完整模拟 tick 的 GPU 求解＋几何重建不超过 1 ms**。当前实测约 2.457 ms，尚未达标；优化需保留已认可的倒伏与卸载回弹。剔除、绘制和 CPU 池管理另行记录，不用未模拟帧的平均值或减少活动数量代替达标。

## 查看效果

打开 `Scenes/PBDGrassDemo.unity`。Scene 视图在编辑模式显示静止草叶，进入 Play Mode 后显示当前模拟结果。
Scene 视图的剔除仍使用 `GrassDemo.Cam` 主相机的视锥、距离、投影和 Hi-Z；Scene 相机移动不会改变被剔除的草叶集合。编辑预览和 Scene 相机渲染不会额外推进模拟。

- WASD / 方向键移动相机，右键观察，Shift 加速；Space 开关风，观察卸载后的恢复。
- O / P 移动胶囊体和球体，观察草叶受压、沿接触面滑动和恢复。
- 默认生成 32 × 32 米、每平方米 32 根的固定草田，共 32,768 根。
- `GrassDemo.BladeShape` 控制自然叶形：三次贝塞尔中心线、随机高度/宽度/朝向、整体倾斜、前弯和轻微侧弯；基础高度参数 1.5 米、变化 ±15%，实际叶尖高度受倾斜和弯曲影响。仍为 8 节点、15 顶点。贝塞尔只定义自然形态，不覆盖 XPBD 模拟结果。
- 轻触保持弹性恢复；超过屈服曲率/基部角度后积累有限残余弯曲。`GrassDemo` 组件菜单的 `Reset Grass Deformation` 可清除本次运行的形变；退出 Play、重建组件也会清除，尚未写入存档。
- Demo 固定为 32,768 根标杆：模拟池上限 32,768 根，激活半径 24 米覆盖方形草田的四角，全部参与模拟。通用设置类仍保留 16 米半径供其他场景使用；相机可见性只控制绘制。
- `SimulationFocus` 可指向玩家/交互中心；为空时使用草田根对象位置。模拟池在已生成草田内移动，没有无限世界流式生成。
- 近景范围外暂时不绘制草。这个版本用于检查近景效果，边界还没有中景形状过渡。

形态参数在 `GrassDemo.BladeShape`，物理参数在 `GrassDemo.Physics`。柔顺度越大越软。质量单位为 kg，长度单位为 Unity 米。风仍为启发式世界空间加速度，并非完整空气动力学。运行时修改叶形需重新进入 Play；编辑模式的 Inspector 改动会重建预览。
根对象的旋转和缩放保持单位值；支持非零平移原点。

## 共享数据和模拟池

`GrassBladeData` 保存稳定草叶 ID、根位置、尺寸、静止中心线、材料坐标系、截面缩放、质量权重和渲染拓扑。将来中景可以从同一份数据取形状，不需要生成另一批草。

`GrassSimulationPool` 按每格 32 根分配 GPU 状态槽，严格限制容量。候选格以交互中心距离排序；仍驻留的格保留位置、速度、材料方向和角速度。范围外驻留槽暂停模拟，作为 LRU 缓存；有空余容量时，重新进入不会重置状态。槽被其他格淘汰后，重新进入从包含塑性偏置的自然形态初始化，速度归零；不保证恢复淘汰前的瞬时振动。

塑性弯曲单独按稳定草叶 ID 保存在 GPU，不跟随可回收的模拟槽位。每关节一个 float4；当前 32768 根、7 关节共 3.5 MiB。此缓冲按已经生成的整片草田分配，不是无限世界流式缓存。

池与风场不依赖相机朝向，转动相机不会重置模拟。相机用视锥/可选 Hi-Z 剔除已激活格的绘制；默认关闭 Hi-Z。运行帧没有同步 GPU 回读。
池容量、截面厚度比和交互列表长度在初始化时确定，修改后重新进入 Play Mode；柔顺度、阻尼、质量、半径和风可以运行时调节。

## 八节点弹性草叶

每根草有 8 个中心线节点、7 段、7 个单位四元数材料坐标系，渲染为 15 顶点 / 13 三角形的连续带状叶片。材料局部 x 是宽度方向，y 是材料截面法线，z 是草段切线。当前三角形绕序的正面渲染法线沿 `cross(width, tangent)`，与材料 y 轴符号相反；这不改变弯曲/扭转约束的材料坐标系。

1. 根节点位置固定；地上 7 个节点全部参与动力学。基部材料方向通过弹性约束获得土壤支撑，不额外固定地上节点的位置。
2. 相邻节点的段长使用 XPBD 伸长弹簧。
3. 单位切线与材料 z 轴的 XPBD 对齐约束，将节点位置与材料旋转耦合。
4. 相邻材料方向的相对四元数与静止相对四元数比较，得到两个弯曲分量和一个扭转分量。弯曲绕宽度轴容易、绕法线轴困难；三个柔顺度可分别调整。
5. 静止相对旋转保留草叶自然初始曲率，不使用地上点拉回固定世界位置的弹簧。
6. 质量按段长、逐渐变窄的宽度、逐渐变薄的厚度积分并分配到节点，再归一化到 `BladeMass`。默认每根 0.01 kg。
7. 参考角柔顺度按段长和截面缩放：易弯/扭转约为 `L/(w*t³)`，难弯约为 `L/(t*w³)`。转动惯量使用段质量与长度的标量近似。

长叶默认柔顺度：易弯 3、难弯 0.2、扭转 1、基部 0.08；增加支撑以避免直接沿用短草参数时，重力和普通风就导致永久倒伏。它们是可调的有效材料参数，尚未由实测草种的 Young 模量和截面标定。新叶形按实际宽度轮廓估计质量/截面缩放，旧形态验证仍保留旧参数。
法线和叶片宽度由模拟材料方向重建；移除了旧渲染器额外的曲面位移，以便画面直接反映物理形状。着色阶段额外构造圆弧截面法线，不改变模拟中心线或几何轮廓。

## 有限弯曲塑性

`EnablePlasticity` 开启时，关节的自然相对方向叠加局部二维弯曲偏置，扭转和段长仍为纯弹性。叶身以相邻段平均长度将角变形换算为曲率；默认 `PlasticYieldCurvature=2.5` rad/m、`PlasticFlowRate=6` /s、`PlasticMaxCurvature=4` rad/m。基部单独使用 `BaseYieldDegrees=40` 和 `BaseMaxPlasticDegrees=50`。

每个物理子步完成弹性和接触求解后，超过阈值的弯曲按 `1-exp(-rate*h)` 向塑性状态转移，并限制最大偏置。下一子步围绕更新后的自然形态求解。材料演化不放在约束迭代循环中，避免迭代次数直接倍增塑性速度。三维旋转使用局部旋转向量的弯曲分量构造有界偏置，是面向草叶的简化率相关材料模型，并非完整连续体返回映射。

卸载后保留残余形状，无自动愈合；普通风下不应持续累积塑性。未加入拉伸塑性、断裂、草间碰撞或永久损伤软化。关闭 `EnablePlasticity` 会忽略已有偏置，用于弹性对照；需要完全重置时使用组件重置菜单。

## 草叶光照

当前材质默认 `Lit` 正常光照。`Debug View` 可实时切换到 `SpecularOnly`，仅输出实际主光高光贡献，保留 GGX、灯光颜色/强度和阴影衰减，使用原始 1 倍强度，不叠加漫反射、透射、环境光或雾；也可选择 `SimulationNormals` GPU 原始世界法线、`ShadingNormals` 世界空间圆弧正面法线、`SpecularNormals` 草丛过滤后的正面法线、`FacingNormals` 双面 BRDF 实际使用的法线（按三角形正反面翻转）。法线模式 RGB = N × 0.5 + 0.5。Game/Scene 视图均生效，此开关即时更新，不需要重新初始化模拟；历史光照截图和性能数据均在 `Lit` 模式下采集。法线方向、相机不变性验证见 [NormalSpaceValidation.md](Diagnostics/NormalSpaceValidation.md)。

当前 Demo 使用 `Materials/PBDGrassProcedualMaterial.mat`，着色器是 `Shaders/PBDGrassShaderDrawProcedual.shader`。参考《对马岛之魂》GDC 2021 草演讲的圆弧法线、草丛法线参与高光抗闪烁，以及叶脉/光泽/颜色纹理组织；完整 BRDF 未在演讲中公开，本版是在 Unity Built-in Forward 中实现这些公开思路，并非原游戏着色器的逐字复刻。此前自行添加的掠射角视线法线偏置已移除，没有将它作为原游戏实现保留。

- 叶片宽度方向来自当前模拟几何，圆弧法线随 XPBD 的弯曲、扭转一起变化。世界空间正面法线与三角形绕序一致；双面光照使用 `SV_IsFrontFace` 在 BRDF 阶段翻转，不用 `dot(N,V)` 修补法线方向。
- GGX 非金属高光使用光泽纹理，按像素足迹混合空间草丛法线，并用法线导数提高有效粗糙度，减少细叶高光闪烁。草丛法线与叶片正面法线对齐，不再与相机对齐。`SpecularNormals` 仍会随像素足迹/LOD 改变过滤量；严格检查相机不变性请用 `SimulationNormals` 或 `ShadingNormals`。
- 高光直接复用 Unity Standard 的 GGX 分布、Smith 可见性和 Fresnel 辅助函数。基础反射率及漫反射能量分配使用 `unity_ColorSpaceDielectricSpec`：Gamma 为约 0.221，Linear 为 0.04；此前仅修正 GGX 平方根分支、仍硬编码 Fresnel=0.04 的混用已修复。相同输入对照 Unity `BRDF1_Unity_PBS` 的直接高光，Gamma/Linear 均通过逐像素验证，见 [HighlightValidation.md](Diagnostics/HighlightValidation.md)。草丛过滤从叶宽接近 1 像素时开始，限制粗糙度增量；法线在每个 60 Hz 模拟 tick 最后一个子步重建，没有模拟推进的渲染帧直接复用。
- `Textures/LeafVeinProfile.png` 和 `LeafGlossProfile.png` 是横跨叶宽的 1D 纹理；`LeafColorVariation.png` 是以稳定草叶种子与叶高采样的 2D 颜色变化纹理。三者为线性数据、Clamp、带 mipmap；没有增加逐帧 CPU 颜色计算或模拟回读。
- 漫反射、高光和薄叶透射均乘灯光颜色、强度和阴影衰减；根部颜色也参与光照。环境光使用 SH，根部 AO 只衰减环境光。不会再用未受灯光控制的加色模拟逆光。
- 新增与模拟几何一致的双面 ShadowCaster，接收 Unity 主光阴影并支持雾。当前只实现 ForwardBase 主光，不含 ForwardAdd 多光源叠加、反射探针高光、SSGI 或游戏原版的屏幕空间接触阴影。

材质可调 `Rounded Cross Section`、`Roughness`、`Specular Anti Aliasing`、`Transmission`、`Root Ambient Occlusion` 和根/尖颜色。草丛是根位置生成的空间着色分组，尚未接入美术草丛分布数据。

Demo 沿用工程当前 Quality 设置；如果该档位关闭 Shadows，即使灯光选择 Soft Shadows 也不会出图。启用 Quality 的阴影后会增加阴影图开销；这里只提交主相机剔除后可见的草，画外草叶向画内投影仍有遗漏。没有为了光照改动工程全局 Quality 配置。

渲染器初始化时复制材质参数，修改材质后重新开关 `GrassDemo` 或重新进入 Play Mode 查看。实际新旧对比、验证和本轮 GPU 开销见 [LightingValidation.md](Diagnostics/LightingValidation.md)。

## 时间、接触与摩擦

默认固定 60 Hz、每 tick 8 个 Small Steps 子步，每子步执行 3 次耦合约束扫掠。XPBD 使用 `alpha = compliance/h²`；乘子在子步开始时清零，在子步内扫掠之间保留。
为了兼容 Unity 2022 的 Direct3D 编译器，每个子步独立提交，单线程求解整根草；固定七段的调用使用显式索引。正常帧无需 CPU 读取模拟结果。

位置使用以每根草根为原点的局部坐标，避免世界坐标舍入被放大成速度。小于 0.1 微米的段长残差及约 1 微弧度的角残差不投影，避免初始浮点误差形成预应力。
追帧超过预算会丢弃过多积累时间，`DroppedTime` 提供诊断。

`GrassInteractionCollider` 支持球体和胶囊体；未附加此组件的旧交互 Transform 仍使用 `ColliderRadius` 球体。禁用组件/对象可暂停该接触。
每段求草段与胶囊轴线的最近点，按两个端点的重心权重和逆质量分配接触修正，因此两个节点之间也能发生接触。
接触处理同时考虑碰撞体的位移；多个固定 tick 分担一帧的运动，各子步再插值。重采样位置以 10 微米量化，避免帧率插值的浮点末位差异触发不同擦边接触分支。
切向位移修正按 `mu * 法向修正量` 截断，形成位置形式的近似 Coulomb 摩擦；地面同样处理滑动。

当前接触仍是离散子步碰撞，地面是草根高度的水平面；不是任意 Terrain/MeshCollider，不含连续碰撞检测、自碰撞或完整叶片表面接触。没有跨帧静摩擦接触缓存。根点进入障碍物属于不可满足的接触，根仍保持固定。

## 代码入口

- `Demo/GrassDemo.cs`：确定性生成、交互中心、风输入和运行；Space 开关风；主相机/Scene 相机渲染回调及编辑静态预览。
- `Runtime/GrassBladeData.cs`：共享草叶身份、静止形状、质量和截面参数。
- `Runtime/GrassBladeShapeSettings.cs`：长叶贝塞尔自然形态参数。
- `Runtime/GrassSimulationPool.cs`：容量、滞回、驻留和 LRU 槽。
- `Runtime/XPBDGrassSettings.cs`：近景预算、弹性、时间和接触参数。
- `Runtime/GrassInteractionCollider.cs`：球/胶囊交互形状。
- `Runtime/PBDGrassPatchRenderer.cs`：GPU 资源、槽初始化、固定时间、碰撞体重采样和绘制。
- `Shaders/PBDSolverCS.compute`：位置/方向 XPBD、Small Steps、整段接触、摩擦、几何重建和剔除。
- `Editor/XPBDValidation.cs`：真实 GPU 验证、基准测试和动画帧捕获。
- `Editor/PlasticGrassValidation.cs`：长叶形态、轻触恢复、强压残余、槽位淘汰、帧率一致性、完整 Demo 和对照图验证。

提取时保留的 CPU `PBDSolver.cs`、CPU 碰撞类和另一套网格着色版本仅作为旧方案参考，不参与当前演示。
`ExtractionManifest.json` 记录最初提取来源，不代表升级后内容仍与原文件一致。命名空间、程序集、Shader 名称和 GUID 保持独立。

## 验证

光照升级前的 32,768 根 Demo 直接 GPU 时间戳评估见 [GPUPerformance.md](Diagnostics/GPUPerformance.md)。当时 1080p HDR / 4× MSAA、每帧一个完整 tick，求解与重建约 2.13 ms，离屏整帧 GPU 工作约 2.58 ms；光照升级后的数据见 [LightingValidation.md](Diagnostics/LightingValidation.md)。正常帧不回读模拟状态。此前同步基准作为历史记录保留。

当前版本的测量、限制和动画见 `Diagnostics/NearValidation.md` 与 `Diagnostics/XPBDNearPreview.gif`。
旧四节点版的 `Validation.md`、`XPBDValidationResults.json`、`XPBDPreview.gif` 保留作为历史对照。
Unity 菜单 `Tools > PBD Grass > Validate XPBD and Capture Preview` 在临时场景验证并捕获图像；`XPBD_VALIDATION_OUTPUT` 可指定输出目录。
自动验证使用系统临时目录中的独立 Unity 工程，只复制本工作目录，不访问源工程的 Library。
同步基准包含 CPU 提交与结束回读，排除绘制，不等于纯 GPU 求解耗时或完整场景帧率。

方法参考：[XPBD](https://mmacklin.com/xpbd.pdf)、[Small Steps](https://matthias-research.github.io/pages/publications/smallsteps.pdf)、[Position and Orientation Based Cosserat Rods](https://vci.rwth-aachen.de/publication/0550/)。本实现使用世界空间旋转增量与可调 XPBD 柔顺度，并非论文求解器的逐字移植。
