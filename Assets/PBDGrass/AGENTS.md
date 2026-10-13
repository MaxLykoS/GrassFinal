# PBD grass workspace

- This directory is the active workspace for PBD grass generation, simulation and rendering.
- Keep future PBD grass changes and new dependencies inside `Assets/PBDGrass/`.
- Treat the pre-existing code and resources elsewhere in Assets as archive references. Do not edit, move, rename, or replace them unless the user explicitly requests it.
- Keep the copied assembly, namespaces, shaders and Unity asset GUIDs independent of archived assets.
- Do not access `Library/`; the root project exclusion instructions also apply here.
- This extraction preserves the original algorithms. Make upgrades as explicit changes to this copy, and document meaningful behavior changes.
