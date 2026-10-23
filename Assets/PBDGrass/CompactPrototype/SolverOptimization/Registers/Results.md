# XPBD solver optimization experiments — 2026-10-22

No new solver optimization was accepted in this round. Production remains on the previous solver, with the previously implemented disabled-collider filtering. Collision, plasticity, the 2-second settling / 1-second live transition, baked data and LOD settings are unchanged. The 1 ms full-pool target is not met.

## Workload and measurement

- NVIDIA GeForce RTX 3080 Ti; Unity 2022.3.62f2c1; isolated project, Direct3D11.
- Fixed 32768 active blades, 8 substeps, 3 constraint iterations, 12 moving colliders. Each performance case has 120 warm-up frames and 300 measured frames, one simulation tick per frame.
- Reported solve GPU time covers the full eight-substep solve and near geometry reconstruction. Final transpose measurements also include both state staging and unstaging. They do not represent the entire rendered frame.
- Reference shader: commit `6197058`. Both reference and candidate measurements use the same disabled-collider filtering.
- Nsight's original DX12 capture motivated these experiments; these D3D11 timestamps are not a new DX12 capture or a measurement of NVIDIA physical register allocation.
- FXC reports 109 temporary vector registers for the reference and 92 for the combined shared-memory variant. These are DXBC temporary declarations, not Nsight's physical register count of 255. Register spilling remains unconfirmed.

## Candidates

| Candidate | Change | Result |
|---|---|---|
| `material`, `both` | Load rod parameters on demand; recompute rest frames to reduce local arrays | Strict state comparison failed; rejected before performance acceptance |
| `frames` | Reload prior frame orientation near its use | ContactLab strict parity passed; no stable GPU gain |
| `shared` | Store per-blade rest orientations in group shared memory | ContactLab strict parity passed; no stable GPU gain |
| `lambda` | Move constraint multipliers to group shared memory | ContactLab strict parity passed; no stable GPU gain |
| `combo` | Combine shared rest orientations, multipliers and shorter frame lifetimes | ContactLab strict parity passed; no stable GPU gain |
| `constant` | Upload uniform material parameters once as constants | ContactLab strict parity passed; no stable GPU gain |
| `split` | Separate predict, collision, bending, alignment and finish kernels | ContactLab strict parity passed; 8.82 / 9.14 ms versus 3.05 / 3.17 ms reference; rejected |
| `loop` | Replace full unrolling with dynamic segment loops | State comparison failed; rejected |
| `special` | Separate intermediate-substep and final-substep entry points | ContactLab strict parity passed; 3.32 / 3.24 ms versus 3.20 / 3.17 ms reference; no gain |
| `soa`, `soa32` | Transpose individual state components across blades; 128 / 32 threads | Broader compact-middle collision regression failed |
| `structure`, `structure32` | Transpose original structs across blades; 128 / 32 threads | Broader compact-middle collision regression failed |

Shared-memory and shorter-lifetime candidates were compared in `Performance1` and `Performance2`. The measured ranges overlap and reference timing drift prevents claiming a small speedup. The split variant adds 9.625 MiB scratch and raises dispatch count from 8 to 96 per tick; its measured cost is substantially higher.

## Correction to preliminary transpose timing

`PerformanceSoa` initially reported 2.79 / 2.84 ms against 3.20 / 3.19 ms. Its end marker preceded the copy back to canonical state, so the previously mentioned roughly 12% reduction was incomplete. It must not be treated as a full-solver improvement. `prepare.py` now places the end marker after that copy.

The corrected `PerformanceTransposeFull` means, including both copies, are:

| Candidate | A (ms) | B (ms) | Additional GPU scratch |
|---|---:|---:|---:|
| reference | 4.8170 | 3.2808 | 0 |
| component transpose, 128 threads | 4.6592 | 4.8685 | 15.625 MiB |
| component transpose, 32 threads | 4.4344 | 4.5696 | 15.625 MiB |
| original-struct transpose, 32 threads | 5.1560 | 4.9940 | 22.5 MiB |

Execution order was reference A, soa A, soa32 A, structure32 A, structure32 B, soa32 B, soa B, reference B. The large reference drift means this round cannot establish a reliable percentage improvement. Timing alone is insufficient for accepting any of these variants, and all fail the required collision equivalence test.

## Collision regression and restored production

The 128-thread transposed candidates passed the separate ContactLab comparison: 480 ticks, moving colliders, contact activation/deactivation, streaming movement and nine strict state checkpoints. This did not cover every existing scenario. The broader compact-middle test caught changes to independent near collision geometry:

| Candidate | Maximum near geometry difference at `IndependentLodgedNear` |
|---|---:|
| component transpose, 128 threads | 1.870571 m |
| original-struct transpose, 128 threads | 2.037229 m |
| component transpose, 32 threads | 1.941984 m |
| original-struct transpose, 32 threads | 1.872408 m |

No tolerance was relaxed. The exact cause has not been established; compiler floating-point differences, state handling and indexing must not be presumed innocent without tracing the first divergence. Keeping 32 threads did not resolve the regression. All candidates were removed from production.

After restoration, all five existing checks passed in `RestoredChecks`: FinalParity, Compact, Lifecycle, Combined and Showcase. Combined covers 32768 XPBD plus 98304 Bezier blades and 1440 ticks. The production compute shader and generator match `6197058`; the renderer matches `ReferenceRenderer.cs.txt`, which preserves the prior disabled-collider filtering.

## Evidence and reproduction

- `Summary.json`: 35 recorded timing cases and 26 check reports. Check count includes failed setup and failed candidates; it is not a pass count.
- `Evidence.zip`: raw JSON reports, source variants and reproduction scripts. Unity logs and cache contents are excluded.
- `*.compute.txt`: inactive experiment source snapshots. Scenes do not reference these files.
- `prepare.py`: generates all candidates and installs temporary instrumentation in `D:/CodexTemp/codex-grass-contact-profile`. It reads project source and settings; it does not access the source project's Library directory.
- For performance, run `ContactGpuBenchmark.Run` in the isolated Unity project with `GRASS_PROFILE_OUTPUT`, `GRASS_PROFILE_CASE` and semicolon-separated `GRASS_SOLVER_CASES`; use the case names from `Summary.json`. Run with `-batchmode -force-d3d11`.
- For ContactLab strict parity, select `GRASS_REGISTER_VARIANT`, set `GRASS_SOLVER_PARITY_OUTPUT` and run `ContactSolverParity.Run`.
- For the broader compact test, install a selected candidate as the isolated compact shader with its normal relative includes and use the matching temporary renderer generated by `prepare.py`. Set `GRASS_COMPACT_OUTPUT` and run `GrassFinal.PBDGrass.Editor.GrassCompactValidation.Run`.
- Initial `Parity_material`, `Parity_frames`, `Parity_both` reports reflect a shader-name prefix setup error, subsequently fixed. The first loop candidate had a compile failure before the later numerical failure. Neither is evidence of a valid measured solver.
- `../prepare_solver.py` followed by `../prepare_capture.py` restores clean production runtime sources in the isolated project. It does not rebuild or replace the existing standalone Nsight player.

The next useful step is to trace the first divergent state write in the transpose variant before further tuning its throughput, and to obtain actual native register/spill evidence if continuing the register-pressure investigation. Lower DXBC temporary counts alone did not predict a useful speedup in these tests.
