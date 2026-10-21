from pathlib import Path
import zipfile, os
source=Path(__file__).resolve().parent
project=Path(os.environ.get("GRASS_PROFILE_PROJECT","D:/CodexTemp/codex-grass-contact-profile"))
assert project.name=="codex-grass-contact-profile"
target=project/"Assets/PBDGrass/ContactProfileTemporary"
names=["GrassSimulationPool","GrassTieredHandoff","GrassLodHandoff","PBDGrassPatchRenderer"]
with zipfile.ZipFile(source/"Reference.zip") as archive:
 for name in names:
  text=archive.read(name+".cs").decode("utf-8-sig")
  for symbol in names:text=text.replace(symbol,"Legacy"+symbol)
  (target/("Legacy"+name+".cs")).write_text(text,encoding="utf-8")
(target/"Editor/CpuParity.cs").write_bytes((source/"CpuParity.cs.txt").read_bytes())
print("Installed independent reference implementations and parity runner")
