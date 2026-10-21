"""Prepare optimized, reference, or parity code in the isolated D: project."""
from pathlib import Path
import sys
source=Path(__file__).resolve().parent
profile=source.parent/"Showcase/Performance/prepare_profile.py"
mode=sys.argv[1] if len(sys.argv)>1 else "optimized"
assert mode in ["optimized","reference","parity"]
code=profile.read_text(encoding="utf-8-sig")
if mode=="reference":
 hook="""
with zipfile.ZipFile(source / 'CompactPrototype/CpuOptimization/Reference.zip') as archive:
    for name in archive.namelist():
        (target / 'Runtime' / name).write_bytes(archive.read(name))
"""
 code=code.replace("def replace(text, before, after):",hook+"\ndef replace(text, before, after):")
exec(compile(code,str(profile),"exec"),{"__file__":str(profile)})
if mode=="parity":
 script=source/"prepare_parity.py"
 exec(compile(script.read_text(),str(script),"exec"),{"__file__":str(script)})
print("Prepared",mode)
