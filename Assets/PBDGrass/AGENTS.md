# PBD grass workspace

- This directory is the active workspace for PBD grass generation, simulation and rendering.
- Keep future PBD grass changes and new dependencies inside `Assets/PBDGrass/`.
- Treat the pre-existing code and resources elsewhere in Assets as archive references. Do not edit, move, rename, or replace them unless the user explicitly requests it.
- Keep the copied assembly, namespaces, shaders and Unity asset GUIDs independent of archived assets.
- Do not access `Library/`; the root project exclusion instructions also apply here.
- This extraction preserves the original algorithms. Make upgrades as explicit changes to this copy, and document meaningful behavior changes.
- Performance target: RTX 3080 Ti, 32,768 active XPBD blades, at most 1 ms of GPU solving plus geometry reconstruction per complete simulation tick. Preserve the accepted long-leaf, elastic rebound and plastic lodging behavior when optimizing. Report culling, rendering and CPU management separately; do not claim success by averaging in frames without simulation or reducing the active blade count. Use GPU timestamps rather than synchronized CPU/readback timings.
