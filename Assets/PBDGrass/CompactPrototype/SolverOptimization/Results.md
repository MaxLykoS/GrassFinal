# Contact solver optimization, 2026-10-21

Baseline: `6197058`. Final runtime change: pack only enabled collider records for GPU upload and set `ColliderCount` to that count each tick. Per-actor previous/current endpoints remain in the original arrays, so disabling and re-enabling an actor preserves the original motion history. Contact order is preserved. No shader, physical parameter, baked asset, pool capacity, or lifecycle setting changed.

The final paired test shows a useful reduction during settling and fading. It does **not** reduce the full 32768-blade solver cost to the 1 ms target.

## Final paired GPU timestamps

RTX 3080 Ti, Unity 2022.3.62f2c1, D3D11, 1600x900, ContactLab 144 m resident square, middle full/outer radii 52/64 m, unchanged outer coverage. Eight substeps and three constraint iterations. Native GPU timestamps collected asynchronously, not CPU duration or synchronous state readback.

Two baseline and two candidate runs, balanced order. Each fixed run has 120 warm-up ticks and 300 measured ticks. Each lifecycle run has 120 warm-up ticks followed by 120 contact, 120 settling, 60 fading, and 300 idle samples. Every sampled frame advances exactly one complete simulation tick. Both versions have identical active-count sequences.

Mean solve + near-geometry reconstruction time, milliseconds:

| Workload | Mean active blades | Baseline A/B | Final A/B | Mean change |
| --- | ---: | ---: | ---: | ---: |
| Fixed full pool, 12 enabled actors | 32768 | 3.227 / 3.237 | 3.264 / 3.270 | +1.1%; no improvement |
| Event-driven contact | 11232 | 1.435 / 1.474 | 1.420 / 1.425 | -2.2%; small difference |
| Contact ended, settling | 10947 | 1.046 / 1.028 | 0.786 / 0.794 | -23.8% |
| Live XPBD to Bezier fade | 10069 | 0.918 / 1.021 | 0.755 / 0.787 | -20.5% |
| Reclaimed, idle | 0 | 0.00187 / 0.00186 | 0.00091 / 0.00091 | Not a full-pool result |

Full-pool candidate GPU display reconstruction/update is 0.792 ms, core culling about 0.009 ms, outer preparation about 0.042 ms, and rendering about 0.909 ms; measured total is 5.065 ms. These are reported separately from the 3.267 ms solver target scope. CPU `Advance` is about 2.341 ms (pool selection 0.737 ms, handoff management 1.390 ms, included within Advance). This is an instrumented Editor/manual-tick workload, not standalone game frame rate. CPU/GPU durations must not be added as if serial.

The lifecycle active counts are below 32768 and cannot establish the 1 ms full-pool target. Previous-session absolute timings also must not be treated as this change's gain: use the paired reference runs captured here.

## Correctness and memory

- Independent reference/current full-pool runs, 480 ticks each, 12 moving actors, disable/re-enable and a 16 m streaming shift: nine checkpoints have zero bitwise mismatches in nodes, velocities, frames, angular velocities, plastic state, geometry, normals, or slot mapping.
- Compact-state regression passed.
- Contact lifecycle: 2 s settling, 1 s live simulation during fade, preserved solver on recontact, final reclamation, persistent plastic state, streaming/unloading, cap and outer coverage all passed. First fade 2.0167 s, release 3.0333 s; existing quantization unchanged.
- Combined baseline: 32768 active XPBD + 98304 Bezier, 1440 ticks, 1972 exiting and 1082 re-entering grids; seed and retained-plastic errors zero. All eight transient response-bank rebakes match the accepted data exactly. Accepted assets unchanged.
- Showcase regression passed.
- GPU buffer allocation stays 224852832 bytes (214.44 MiB); this is buffer accounting, not total VRAM. CPU adds one reusable `ColliderMotion[]`: 768 payload bytes for twelve actors, plus array header/alignment. No new per-tick allocation or runtime GPU readback.
- The pre-existing abrupt deep-collider-removal stress case still has an immediate physical tip movement of about 2.215 m. This change does not repair or worsen that known behavior. Fade maximum tip step is about 3.23 cm; final reclamation step about 1.13 mm.

## Rejected experiments

1. Conservative segment/capsule AABB rejection ahead of the existing sphere rejection: passed strict parity, but full-pool timings showed no stable gain. Reverted.
2. Remapping blades into 32/64/128-thread workgroups: no stable gain; restored original 32-thread mapping. Exact tested group sources are inside `Evidence.zip/GroupExperimentSources.zip`.

The shader and shader generator are checked against `6197058` by `summarize.py`. Only the disabled-collider upload filtering remains in production. Nsight is the next useful step: determine whether the expensive kernel is limited by register pressure/local-memory spills, instruction throughput, or memory stalls before changing its structure again.

## Evidence and reproduction

`Summary.json` contains per-case/per-phase means, medians, p95, counts, API, GPU, and source hashes. `Evidence.zip` contains raw per-frame JSON, correctness reports, rejected group sources, final production sources, and reproduction scripts. Raw Unity logs remain in the isolated project and are not copied into the repository.

Run `prepare_solver.py` first, then `run_checks.ps1 -Output <new-check-directory>` and `run_performance.ps1 -Output <new-perf-directory>`. The scripts refuse to overwrite results. Run jobs serially against the isolated project. `prepare_capture.py` subsequently restores clean production runtime before building the Nsight player, so rerun `prepare_solver.py` before another instrumented benchmark.

The standalone capture build also passed 600-frame smoke runs in D3D12 with contacts, D3D12 without contacts, and D3D11 with contacts. All frames have exactly 32768 active blades and one tick, and no logged errors. Offscreen screenshots were inspected for grass, collision deformation, and the green/blue/orange coverage. These are rendering smoke tests, not bitwise physics parity across APIs. See `Nsight.md` for capture setup. The initial hidden-window backbuffer screenshot was black; final smoke tests explicitly render the camera into a texture to avoid Windows occlusion suppressing screen rendering.
