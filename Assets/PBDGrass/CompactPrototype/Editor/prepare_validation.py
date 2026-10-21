"""Sync only the grass assets into the existing isolated Unity validation project."""
from pathlib import Path
import os, shutil, uuid
root=Path(__file__).resolve().parents[2]
target=Path(os.environ["TEMP"])/"codex-xpbd-near-unity-validation/Assets/PBDGrass"
assert target.parent.parent.name=="codex-xpbd-near-unity-validation"
for p in [root/"CompactPrototype", *sorted((root/"CompactPrototype").rglob("*"))]:
    if p.suffix==".meta" or "__pycache__" in p.parts: continue
    meta=Path(str(p)+".meta")
    if not meta.exists():
        text="fileFormatVersion: 2\nguid: "+uuid.uuid4().hex+"\n"
        if p.is_dir():text+="folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
        elif p.suffix==".cs":text+="MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
        elif p.suffix==".compute":text+="ComputeShaderImporter:\n  externalObjects: {}\n  currentAPIMask: 4\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
        elif p.suffix==".shader":text+="ShaderImporter:\n  externalObjects: {}\n  defaultTextures: []\n  nonModifiableTextures: []\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
        else:text+="DefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
        meta.write_text(text,encoding="utf-8")
count=0
for p in root.rglob("*"):
    if not p.is_file() or "__pycache__" in p.parts:continue
    to=target/p.relative_to(root)
    if not to.exists() or to.read_bytes()!=p.read_bytes():
        to.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(p,to);count+=1
print("Synced",count,"changed grass files; source Library was not accessed.")
