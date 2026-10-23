"""Create the eight-type comparison scene and record independent bank identities."""
from pathlib import Path
import hashlib
import json
import re
import uuid
here=Path(__file__).resolve().parent
grass=here.parents[1]
source=grass/'ResponsePrototype/GrassResponseComparison.unity'
text=source.read_text(encoding='utf-8-sig')
manifest={'profile':'60Hz,2 substeps,1 iteration,SoftBendCompliance x0.5','types':[]}
for i in range(1,9):
    old=grass/f'ResponsePrototype/Baked/GrassResponse{i}.asset'
    new=here/f'Baked/GrassResponse{i}.asset'
    def guid(path):return re.search(r'^guid: (\w+)',Path(str(path)+'.meta').read_text(),re.M)[1]
    assert text.count(guid(old))==1
    text=text.replace(guid(old),guid(new))
    manifest['types'].append({'index':i,'path':str(new.relative_to(grass.parent.parent)).replace('\\','/'),
        'guid':guid(new),'sha256':hashlib.sha256(new.read_bytes()).hexdigest()})
(grass/'Scenes/PBDGrassLowBudgetResponse.unity').write_text(text,encoding='utf-8')
(here/'BankManifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
paths=[here,*here.rglob('*'),grass/'Scenes/PBDGrassLowBudgetResponse.unity',
       grass/'ResponsePrototype/Editor/GrassLowBudgetBaker.cs',grass/'ResponsePrototype/Editor/GrassLowBudgetResponseValidation.cs',
       grass/'CompactPrototype/Editor/GrassLowBudgetIntegrationValidation.cs']
for path in paths:
    if path.suffix=='.meta' or '__pycache__' in path.parts:continue
    meta=Path(str(path)+'.meta')
    if meta.exists():continue
    importer='MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n' if path.suffix=='.cs' else 'DefaultImporter:\n  externalObjects: {}\n'
    meta.write_text('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\n'+('folderAsset: yes\n' if path.is_dir() else '')+importer,encoding='utf-8')
print('Created eight-type comparison scene and independent bank manifest.')
