# Nsight Graphics 2023.3.2 capture

NVIDIA's [2023.3 release notes](https://developer.nvidia.com/nsight-graphics-2023_3) confirm that GPU Trace's Real-Time Shader Profiler supports Ampere/Ada with D3D12 or Vulkan. RTX 3080 Ti is Ampere. Use **Nsight Graphics**, GPU Trace with shader profiling enabled.

The standalone capture player is built from the final production solver in an isolated project. It has no GPU timing plugin, no solver-state readback, no script debugging, and no automatic activation/reclamation. Its diagnostic workload fixes **32768 active XPBD blades, eight substeps, three iterations, one complete tick per rendered frame**, with the existing ContactLab middle and outer coverage. The actual gameplay scene retains contact-driven activation and the 2 s + 1 s lifecycle.

## Launch settings

- Application: `D:\CodexTemp\grass-nsight-capture\GrassCapture.exe`
- Working directory: `D:\CodexTemp\grass-nsight-capture`
- Arguments: `-force-d3d12 -screen-width 1600 -screen-height 900 -screen-fullscreen 0`

Wait for the HUD to display `Direct3D12` and `XPBD 32768`, then allow several seconds of warm-up before capturing. VSync is disabled. The camera is fixed at the elevated ContactLab view. Keep this camera/resolution for comparisons.

Capture two separate states:

1. Default: twelve moving colliders enabled.
2. Press **C** to disable colliders, wait a few seconds, and capture again. Active XPBD remains 32768. Alternatively launch with the additional argument `-grass-no-contact`.

**Esc** exits. Do not use the `-grass-smoke` argument for Nsight: it makes the player quit after 600 frames.

## What to collect

Start with the GPU Trace timeline and its shader profiler. Locate `XPBDSolverLOD0` in `CompactGrassSolver.compute`. There are eight dispatches per tick; near geometry is rebuilt in the last substep, so inspect the last dispatch separately and sum all eight when comparing against the solve+reconstruction budget.

Record the expensive shader's register count, occupancy/active warps, local-memory loads/stores or spills if exposed, memory/cache throughput, instruction throughput, and warp stall breakdown. Look for long-scoreboard memory waits, math-pipeline or special-function pressure; these are hypotheses to test, not established bottlenecks. Also inspect dispatch/barrier gaps in the timeline. Save the native trace and the shader statistics table (export or screenshots), including which contact state was captured.

D3D12 results should be compared against another D3D12 build/capture of the same workload. Do not subtract them from the D3D11 timestamp numbers in `Results.md` to claim an optimization gain. Source-level correlation may require a separate debug-symbol build; this build preserves ordinary optimized shader compilation and does not use `enable_d3d11_debug_symbols` to alter optimization.

## Rebuild

After instrumented benchmark jobs have exited, run `prepare_capture.py`. It restores clean runtime sources in `D:\CodexTemp\codex-grass-contact-profile` and installs the capture harness there only. Set `GRASS_CAPTURE_PLAYER` to the desired `.exe` path, clear `GRASS_REALTIME_CASE`, and invoke Unity 2022.3.62f2c1 with `-batchmode -force-d3d11 -projectPath D:\CodexTemp\codex-grass-contact-profile -executeMethod BuildCapture.Run -logFile <build-log>`. The temporary build enables D3D12 and D3D11 and excludes the D3D11 timer DLL from the Player. Original project graphics settings remain untouched.

For automated smoke testing, add `-grass-smoke -grass-output=<new-output-directory>`. This verifies the API, all 600 full-pool frames, one tick per frame and logged errors, renders the camera explicitly into a texture, then takes one offline screenshot and quits. The offscreen path allows rendering checks while the Windows window is hidden. It is not a physics-state parity test across graphics APIs. D3D12 with contacts, D3D12 without contacts and D3D11 with contacts have all passed, with screenshots inspected.
