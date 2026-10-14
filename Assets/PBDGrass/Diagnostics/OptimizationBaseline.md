# 优化前 baseline

2026-10-14，用户确认当前光照和物理效果可以接受后冻结。

- 分支：`codex/pbd-grass-upgrade`；固定标签：`baseline/xpbd-grass-pre-optimization`。
- 入口：`Assets/PBDGrass/Scenes/PBDGrassDemo.unity`；Unity 2022.3.62f2c1。
- 草田：32×32 米，32 根/平方米，共 32768 根；随机种子 12345。近景半径 24 米，池容量 32768，全部参与模拟；仅近景。
- 每根 8 节点、7 段、7 个材料方向；60 Hz，8 个 Small Steps 子步，每子步 3 次约束迭代。包含伸长、切线对齐、各向异性弯曲、扭转、弹性基部、整段球/胶囊接触、地面摩擦。
- 圆弧世界法线、双面光照、Unity Standard GGX/Fresnel、薄叶透射、根部 AO、主光阴影；Gamma/Linear 反射约定修正已包含。
- 材质为 `Lit`（`_DebugView=0`）；Roughness=0.48、SpecularStrength=0.7、NormalCurvature=0.65、SpecularAA=1、Transmission=0.55。
- 保留当前已保存的场景覆盖：主相机黑色背景、地面对象隐藏、太阳局部旋转为 (60°,0°,0°)。物理地面接触仍启用。
- Scene 绘制复用主相机的剔除参数；正常运行无模拟状态同步回读。

提交保留当前 `Assets/PBDGrass/` 的生成、模拟、渲染、资源、Editor 验证脚本和 Diagnostics 资料。目录外原有资源继续作为 Archive；本地编辑器布局不属于 baseline。

历史物理验证见 NearValidation.md，法线验证见 NormalSpaceValidation.md，最终高光验证及 Gamma/Linear 对照见 HighlightValidation.md；GPUPerformance.md 和 LightingValidation.md 中的计时对应各自的历史源码/场景快照。**本次提交没有重新测量当前场景的 GPU 耗时**，后续优化应从此标签检出，在同硬件、同相机、同场景/Quality/材质和模拟预算下重新采集前后数据，不把历史绝对耗时直接当成本标签实测。

对比后续优化：`git diff baseline/xpbd-grass-pre-optimization..HEAD -- Assets/PBDGrass`。
