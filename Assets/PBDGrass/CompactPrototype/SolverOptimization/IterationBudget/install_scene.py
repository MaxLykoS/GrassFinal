from pathlib import Path
import shutil
import uuid
here=Path(__file__).resolve().parent
target=Path('D:/CodexTemp/codex-grass-contact-profile/Assets/PBDGrass')
for path in [here,*here.iterdir()]:
    if path.suffix=='.meta' or path.name=='__pycache__':continue
    meta=Path(str(path)+'.meta')
    if not meta.exists():
        importer='MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n' if path.suffix=='.cs' else 'DefaultImporter:\n  externalObjects: {}\n'
        meta.write_text('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\n'+('folderAsset: yes\n' if path.is_dir() else '')+importer,encoding='utf-8')
destination=target/'CompactPrototype/SolverOptimization/IterationBudget'
destination.mkdir(parents=True,exist_ok=True)
for suffix in ['', '.meta']:
    shutil.copy2(here/('GrassIterationBudgetDemo.cs'+suffix),destination/('GrassIterationBudgetDemo.cs'+suffix))
shutil.copy2(here/'BuildIterationScene.cs.txt',target/'ContactProfileTemporary/Editor/BuildIterationScene.cs')
print('Installed scene builder and its runtime quality selector only; no scene overwritten.')
