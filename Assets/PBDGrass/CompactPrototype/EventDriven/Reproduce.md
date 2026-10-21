# 复现本轮检查

2026-10-21，Unity 2022.3.62f2c1 / D3D11 / RTX 3080 Ti / Ryzen 5800X。

以下检查在隔离项目 `D:/CodexTemp/codex-grass-contact-profile` 运行。准备脚本只复制 `Assets/PBDGrass`、`ProjectSettings` 和必要的引擎包列表，不读取源项目 Library。不要同时在同一隔离项目运行两个 Unity。

1. 在工程根目录用 Python 执行 `Assets/PBDGrass/CompactPrototype/CpuOptimization/prepare_cpu.py optimized`。此步骤在隔离项目安装 GPU 时间戳插件和 CPU 插桩；用户项目不安装插件。
2. 用 Unity 的 `-batchmode -force-d3d11 -projectPath <隔离项目> -executeMethod <下面的方法> -logFile <独立日志>` 启动每项检查。由测试自行退出，不能加 `-quit` 提前结束。后台启动使用 `Start-Process -WindowStyle Hidden`。
3. 为该进程设置 TEMP/TMP 为隔离项目的 `TempTools`，UPM_CACHE_ROOT 为其 `UpmCache`。各输出目录必须分别设置，已有证据应保留。

| 检查 | executeMethod | 输出环境变量 |
| --- | --- | --- |
| 生命周期 | GrassFinal.PBDGrass.Editor.GrassContactLifecycleValidation.Run | GRASS_LIFECYCLE_OUTPUT |
| 45 秒演示往返 | GrassFinal.PBDGrass.Editor.GrassShowcaseValidation.Run | GRASS_SHOWCASE_OUTPUT |
| 联合 baseline 和八类型烘焙 | GrassFinal.PBDGrass.Editor.GrassCombinedBaselineValidation.Run | GRASS_COMBINED_OUTPUT |
| GPU / CPU 性能 | ContactGpuBenchmark.Run | GRASS_PROFILE_OUTPUT |

性能检查另设 `GRASS_PROFILE_CASE=wide_lifecycle`、`GRASS_PROFILE_SUITE=1`，同进程依次测 600 个事件生命周期帧和 300 个 `wide_fixed` 满池帧；每个用例均预热 120 帧。固定 60 Hz、每帧 1 tick、8 substeps × 3 iterations，1600×900、MSAA×4、12 个演示碰撞体、144×144 驻留窗口、中景完整半径 52 米、过渡至 64 米。时间戳异步收集，不用同步回读计 GPU 耗时。

本次两份性能报告均完成且 passed，900 个样本没有丢弃/失败。Unity 随后在退出阶段停留超过 90 秒，确认项目路径和启动时间后仅终止隔离实例；记录在 `Evidence.zip/PerformanceShutdown.json`。该清理发生在时间戳已全部收集和报告已保存之后。

汇总：执行 `summarize_event_driven.py <EventDrivenResults 目录>`；该目录含 `AcceptedLifecycle/Results.json`、`Showcase/Results.json`、`Combined/Results.json`、两份 Performance 报告和 Shutdown 记录。脚本生成 Summary、源码 SHA256 和证据包。

Shader 生成器也已做不落盘检查：拦截 `Path.write_text`，比较 6 个生成结果与现有 shader，换行归一化后全部一致。

生命周期专项中的 GPU 状态回读仅用于离线断言，不属于常规运行或性能样本。完整联合 baseline 沿用历史固定池，八份临时重烘焙没有写回接受的烘焙资产。
