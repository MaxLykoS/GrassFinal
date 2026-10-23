from pathlib import Path
import runpy
import uuid
here=Path(__file__).resolve().parent
runpy.run_path(str(here.parent/'prepare.py'))
target=Path('D:/CodexTemp/codex-grass-contact-profile/Assets/PBDGrass')
temporary=target/'ContactProfileTemporary/Editor'
renderer=target/'Runtime/PBDGrassPatchRenderer.cs'
text=renderer.read_text(encoding='utf-8-sig')
needle='            this.camera = camera; this.settings = settings; this.sourceMaterial = sourceMaterial;'
text=text.replace(needle,'''            if(float.TryParse(Environment.GetEnvironmentVariable("GRASS_TUNE_SOFT_SCALE"),System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,out float softScale))settings.SoftBendCompliance*=softScale;
'''+needle)
renderer.write_text(text,encoding='utf-8')
text=(here.parent/'IterationQuality.cs.txt').read_text(encoding='utf-8')
start=text.index('            foreach(var budget in new[]{')
end=text.index('                RunCase(c,report);',start)
text=text[:start]+'''            foreach(var c in new[]{
                new Case{name="8x3",substeps=8,iterations=3},
                new Case{name="2x1",substeps=2,iterations=1},
                new Case{name="tuned2x1",substeps=2,iterations=1}})
            {
'''+text[end:]
needle='        demo.AutoTour=false;demo.ContactEnabled=true;'
text=text.replace(needle,'        if(c.name=="tuned2x1")demo.Physics.SoftBendCompliance*=.5f;\n'+needle)
text=text.replace('public static class IterationQuality','public static class GrassTuningQuality')
(temporary/'GrassTuningQuality.cs').write_text(text,encoding='utf-8')
(temporary/'GrassTuningProbe.cs').write_bytes((here/'GrassTuningProbe.cs.txt').read_bytes())
benchmark=temporary/'ContactGpuBenchmark.cs'
text=benchmark.read_text(encoding='utf-8-sig')
needle='            demo.PatchSize=name.StartsWith("small")?96:144;'
text=text.replace(needle,'            if(name.Contains("_tuned_"))demo.Physics.SoftBendCompliance*=.5f;\n'+needle)
benchmark.write_text(text,encoding='utf-8')
for path in [here,*here.iterdir()]:
    if path.suffix=='.meta' or path.name=='__pycache__':continue
    meta=Path(str(path)+'.meta')
    if not meta.exists():meta.write_text('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\n'+
        ('folderAsset: yes\n' if path.is_dir() else '')+'DefaultImporter:\n  externalObjects: {}\n',encoding='utf-8')
print('Installed tuning measurements in isolated project only.')
