"""Spread only the iteration-budget test scene; no shared runtime or old scene edits.

Run from any directory with Python. Re-running is idempotent. The .cs.txt rebuild
helper receives the same layout so rebuilding the experiment preserves its paths.
"""
from pathlib import Path
import math
import re
import uuid

ROOT = Path(__file__).resolve().parents[4]
SCENE = ROOT / 'Assets/PBDGrass/Scenes/PBDGrassIterationBudget.unity'
BUILDER = ROOT / 'Assets/PBDGrass/CompactPrototype/SolverOptimization/IterationBudget/BuildIterationScene.cs.txt'

# Order matches GrassShowcaseController.Actors. Keep station motion types, shape
# dimensions, collider IDs, material, response bank and physics budget unchanged.
LAYOUT = [
    ((-8, .50, -22), (1, 0, 0), 30, 36),  # 01 SWEEP: 60 m end to end
    ((-24, .60, 8), (1, 0, 0), 16, 20),   # 02 CIRCLE: 32 m diameter
    ((23, 1, -3), (0, 0, 1), 30, 36),     # 03 WALKER
    ((2, .48, 12), (1, 0, 1), 30, 36),    # 04 ROLLER: diagonal
    ((-36, .30, -28), (0, 0, 0), 0, 9),   # 05 PRESS S
    ((-6, .45, 36), (0, 0, 0), 0, 11),    # 06 PRESS M
    ((30, .60, 31), (0, 0, 0), 0, 13),    # 07 PRESS L
    ((7, .75, 6), (0, 0, 0), 0, 10),      # 08 STATIC: original fixed position
    ((-36, 1.55, 14), (0, 0, 1), 18, 24), # 09 TIP BRUSH
    ((0, .70, -6), (1, 0, 0), 42, 48),    # 10 CROSS A: 84 m end to end
    ((8, .70, 7), (0, 0, 1), 33, 40),     # 11 CROSS B: 66 m end to end
    ((38, .90, 20), (0, 0, 0), 0, 10),    # 12 POST: original fixed position
]
CAMERA = (0, 72, -12)
TARGET = (0, .6, 0)
FOV = 70


def vector(v):
    return '{' + ', '.join(f'{key}: {value:.9g}' for key, value in zip('xyz', v)) + '}'


def replace_once(text, pattern, replacement):
    text, count = re.subn(pattern, lambda _: replacement, text, flags=re.M)
    assert count == 1, (pattern, count)
    return text


def main():
    text = SCENE.read_text(encoding='utf-8-sig')
    parts = re.split(r'(?=^--- !u!\d+ &\d+\n)', text, flags=re.M)
    docs = {int(re.match(r'--- !u!\d+ &(\d+)', p)[1]): p
            for p in parts if p.startswith('--- !u!')}
    controller_id = 114106515
    controller = docs[controller_id]
    actor_blocks = re.findall(r'  - Collider:.*?(?=  - Collider:|  Labels:)', controller, re.S)
    label_ids = [int(v) for v in re.findall(r'  - \{fileID: (\d+)\}', controller.split('  Labels:')[1])]
    assert len(actor_blocks) == len(label_ids) == len(LAYOUT) == 12

    pitch = math.atan2(CAMERA[1] - TARGET[1], TARGET[2] - CAMERA[2])
    rotation = '{x: %.9g, y: 0, z: 0, w: %.9g}' % (math.sin(pitch / 2), math.cos(pitch / 2))
    for index, (block, (origin, axis, travel, period)) in enumerate(zip(actor_blocks, LAYOUT)):
        updated = block
        for field, value in [('Origin', vector(origin)), ('Axis', vector(axis)),
                             ('Travel', str(travel)), ('Period', str(period))]:
            updated = replace_once(updated, rf'^    {field}:.*$', f'    {field}: {value}')
        controller = controller.replace(block, updated, 1)

        # Set the saved editor pose exactly as ApplyActors(0, true) would place it.
        movement = int(re.search(r'    Movement: (\d+)', block)[1])
        phase = float(re.search(r'    Phase: ([^\n]+)', block)[1])
        angle = phase / max(1, period) * math.tau
        position = list(origin)
        if movement == 0:
            length = math.sqrt(sum(v * v for v in axis))
            position = [p + a / length * math.sin(angle) * travel for p, a in zip(position, axis)]
        elif movement == 1:
            position[0] += math.cos(angle) * travel
            position[2] += math.sin(angle) * travel
        elif movement == 2:
            cycle = (phase / max(1, period)) % 1
            smooth = lambda a: a * a * (3 - 2 * a)
            lift = 1 - smooth(cycle / .18) if cycle < .18 else 0 if cycle < .40 else smooth((cycle - .40) / .18) if cycle < .58 else 1
            position[1] += 3 * lift
        collider_id = int(re.search(r'Collider: \{fileID: (\d+)\}', block)[1])
        go_id = int(re.search(r'm_GameObject: \{fileID: (\d+)\}', docs[collider_id])[1])
        transform_id = int(re.search(r'component: \{fileID: (\d+)\}', docs[go_id])[1])
        docs[transform_id] = replace_once(docs[transform_id], r'^  m_LocalPosition:.*$', '  m_LocalPosition: ' + vector(position))
        label_id = label_ids[index]
        docs[label_id] = replace_once(docs[label_id], r'^  m_LocalPosition:.*$', '  m_LocalPosition: ' + vector((origin[0], 2.3, origin[2] + 1.2)))
        docs[label_id] = replace_once(docs[label_id], r'^  m_LocalRotation:.*$', '  m_LocalRotation: ' + rotation)

    docs[controller_id] = controller
    docs[695139530] = replace_once(docs[695139530], r'^  m_LocalPosition:.*$', '  m_LocalPosition: ' + vector(CAMERA))
    docs[695139530] = replace_once(docs[695139530], r'^  m_LocalRotation:.*$', '  m_LocalRotation: ' + rotation)
    docs[695139530] = replace_once(docs[695139530], r'^  m_LocalEulerAnglesHint:.*$', '  m_LocalEulerAnglesHint: ' + vector((math.degrees(pitch), 0, 0)))
    docs[695139528] = replace_once(docs[695139528], r'^  field of view:.*$', f'  field of view: {FOV}')
    SCENE.write_text(''.join(docs[int(re.match(r'--- !u!\d+ &(\d+)', p)[1])] if p.startswith('--- !u!') else p for p in parts), encoding='utf-8', newline='\n')

    source = BUILDER.read_text(encoding='utf-8-sig')
    call = '            ConfigureSpreadLayout(Object.FindObjectOfType<GrassShowcaseController>());\n'
    if call not in source:
        source = source.replace('            demo.gameObject.name=', call + '            demo.gameObject.name=', 1)
    v3 = lambda v: 'new Vector3(' + ','.join(f'{x:.9g}f' for x in v) + ')'
    method = '''    // BEGIN spread_scene.py generated layout
    private static void ConfigureSpreadLayout(GrassShowcaseController lab)
    {
        if(lab==null || lab.Actors.Length!=12)throw new Exception("Expected 12 contact stations.");
'''
    for i, (origin, axis, travel, period) in enumerate(LAYOUT):
        method += f'        lab.Actors[{i}].Origin={v3(origin)};lab.Actors[{i}].Axis={v3(axis)};lab.Actors[{i}].Travel={travel};lab.Actors[{i}].Period={period};\n'
    method += f'''        var camera=lab.Demo.Cam;camera.fieldOfView={FOV};
        camera.transform.position={v3(CAMERA)};camera.transform.LookAt({v3(TARGET)});
        lab.ApplyActors(0,true);
        for(int i=0;i<lab.Labels.Length;i++)
        {{
            var origin=lab.Actors[i].Origin;
            lab.Labels[i].position=new Vector3(origin.x,2.3f,origin.z+1.2f);
            lab.Labels[i].rotation=camera.transform.rotation;
        }}
    }}
    // END spread_scene.py generated layout
'''
    if '    // BEGIN spread_scene.py generated layout' in source:
        source = re.sub(r'    // BEGIN spread_scene.py generated layout.*?    // END spread_scene.py generated layout\n', lambda _: method, source, flags=re.S)
    else:
        assert source.endswith('}\n')
        source = source[:-2] + method + '}\n'
    BUILDER.write_text(source, encoding='utf-8', newline='\n')
    meta = Path(str(Path(__file__).resolve()) + '.meta')
    if not meta.exists():
        meta.write_text(f'fileFormatVersion: 2\nguid: {uuid.uuid4().hex}\nDefaultImporter:\n  externalObjects: {{}}\n', encoding='utf-8')
    print('Spread 12 stations; 7 planar paths, 3 distributed stamps, 2 unchanged fixed stations.')
    print('Saved elevated view: (0,72,-12), FOV70. Middle52/64 and accepted2x1 response bank untouched.')


if __name__ == '__main__':
    main()
