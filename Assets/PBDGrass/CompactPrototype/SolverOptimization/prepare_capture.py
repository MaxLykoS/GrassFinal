"""Run after measurements; restore clean runtime and install the standalone capture harness."""
from pathlib import Path
import os
import shutil
import uuid

here = Path(__file__).resolve().parent
grass = here.parents[1]
project = Path(os.environ.get('GRASS_PROFILE_PROJECT', 'D:/CodexTemp/codex-grass-contact-profile'))
assert project.name == 'codex-grass-contact-profile'
target = project / 'Assets/PBDGrass'
for relative in ['Runtime/PBDGrassPatchRenderer.cs', 'Runtime/GrassTieredHandoff.cs']:
    shutil.copy2(grass / relative, target / relative)
temporary = target / 'ContactProfileTemporary'
shutil.copy2(here / 'ContactCaptureRunner.cs.txt', temporary / 'ContactCaptureRunner.cs')
shutil.copy2(here / 'BuildCapture.cs.txt', temporary / 'Editor/BuildCapture.cs')
for path in [here, *here.rglob('*')]:
    if path.suffix == '.meta' or '__pycache__' in path.parts:
        continue
    meta = Path(str(path) + '.meta')
    if not meta.exists():
        meta.write_text('fileFormatVersion: 2\nguid: ' + uuid.uuid4().hex + '\n' +
                        ('folderAsset: yes\n' if path.is_dir() else '') +
                        'DefaultImporter:\n  externalObjects: {}\n', encoding='utf-8')
print('Restored clean runtime; capture runner installed only in', project)
