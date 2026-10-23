"""Freeze either CPU implementation into the isolated test project."""
from pathlib import Path
import runpy
import sys
here=Path(__file__).resolve().parent
grass=here.parents[2]
runpy.run_path(str(here.parent/'prepare.py'))
target=Path('D:/CodexTemp/codex-grass-contact-profile/Assets/PBDGrass')
mode=sys.argv[1] if len(sys.argv)>1 else 'current'
if mode=='before':
    for name in ['GrassSimulationPool','GrassTieredHandoff','PBDGrassPatchRenderer']:
        (target/'Runtime'/(name+'.cs')).write_bytes((here/('Reference'+name+'.cs.txt')).read_bytes())
    profile=(grass/'CompactPrototype/Showcase/Performance/prepare_profile.py').read_text(encoding='utf-8')
    exec(profile[profile.index('def replace('):],{'target':target})
hooks=target/'ContactProfileTemporary/ContactGpuHooks.cs'
text=hooks.read_text(encoding='utf-8')
fields=['PoolQuery','PoolRank','PoolAssign','Config','BuildQuery','Classify','Boundary','Allocate','Upload']
text=text.replace('public static double PoolCpu,HandoffCpu,SelectionCpu;',
    'public static double PoolCpu,HandoffCpu,SelectionCpu; public static double '+','.join(x+'Detail' for x in fields)+';')
text=text.replace('public static void ResetCpu() {','public static void ResetCpu() {'+''.join(x+'Detail=0;' for x in fields))
hooks.write_text(text,encoding='utf-8')
benchmark=target/'ContactProfileTemporary/Editor/ContactGpuBenchmark.cs'
text=benchmark.read_text(encoding='utf-8')
text=text.replace('public int frame,active,ticks,loadedCells,selectCalls,gcCollections;',
    'public int frame,active,ticks,loadedCells,selectCalls,gcCollections;public double '+','.join(x+'Detail' for x in fields)+';')
text=text.replace('            lab.SetView(name.Contains("overview")?4:1);','            // Keep the spread scene\'s saved elevated camera for both CPU versions.')
text=text.replace('            cpu[frame]=rowNow;',''.join('            rowNow.'+x+'Detail=ContactGpuHooks.'+x+'Detail;\n' for x in fields)+'            cpu[frame]=rowNow;')
benchmark.write_text(text,encoding='utf-8')
if mode in ['before','current']:
    pool=target/'Runtime/GrassSimulationPool.cs';text=pool.read_text(encoding='utf-8')
    text=text.replace('            candidates.Clear(); InitializedSlots.Clear();','            long cpuPhase=ContactGpuHooks.Stamp();\n            candidates.Clear(); InitializedSlots.Clear();')
    direct_sort='            candidates.Sort(' in text
    query_end='            candidates.Sort(' if direct_sort else '            // Collision priority changes'
    rank_end='            BudgetRejectedGrids =' if direct_sort else '            foreach (int g in candidates) if (gridSlot[g] >= 0)'
    assign_end='        }\n        public int SlotForGrid' if mode=='before' else '        }\n        private int CompareRank'
    text=text.replace(query_end,'            ContactGpuHooks.PoolQueryDetail+=ContactGpuHooks.Ms(cpuPhase);cpuPhase=ContactGpuHooks.Stamp();\n'+query_end)
    text=text.replace(rank_end,'            ContactGpuHooks.PoolRankDetail+=ContactGpuHooks.Ms(cpuPhase);cpuPhase=ContactGpuHooks.Stamp();\n'+rank_end)
    text=text.replace(assign_end,'            ContactGpuHooks.PoolAssignDetail+=ContactGpuHooks.Ms(cpuPhase);\n'+assign_end)
    pool.write_text(text,encoding='utf-8')
    tiered=target/'Runtime/GrassTieredHandoff.cs';text=tiered.read_text(encoding='utf-8')
    text=text.replace('            center=focus; Configure(time);','            long cpuPhase=ContactGpuHooks.Stamp();center=focus; Configure(time);\n            ContactGpuHooks.ConfigDetail+=ContactGpuHooks.Ms(cpuPhase);cpuPhase=ContactGpuHooks.Stamp();')
    sort_work='            work.Items.Sort();' if mode=='before' else '            SortWork();'
    text=text.replace(sort_work,'            ContactGpuHooks.BuildQueryDetail+=ContactGpuHooks.Ms(cpuPhase);cpuPhase=ContactGpuHooks.Stamp();\n'+sort_work)
    text=text.replace('            if(required>slotGrid.Length)','            ContactGpuHooks.ClassifyDetail+=ContactGpuHooks.Ms(cpuPhase);cpuPhase=ContactGpuHooks.Stamp();\n            if(required>slotGrid.Length)')
    text=text.replace('            for(int slot=0;slot<slotGrid.Length;slot++)','            ContactGpuHooks.BoundaryDetail+=ContactGpuHooks.Ms(cpuPhase);cpuPhase=ContactGpuHooks.Stamp();\n            for(int slot=0;slot<slotGrid.Length;slot++)')
    text=text.replace('            HistoryResidentBlades=','            ContactGpuHooks.AllocateDetail+=ContactGpuHooks.Ms(cpuPhase);cpuPhase=ContactGpuHooks.Stamp();\n            HistoryResidentBlades=')
    upload_end='        }\n        private static void UploadDirty' if mode=='before' else '        }\n        private void AddBoundary'
    text=text.replace(upload_end,'            ContactGpuHooks.UploadDetail+=ContactGpuHooks.Ms(cpuPhase);\n'+upload_end)
    tiered.write_text(text,encoding='utf-8')
for name in ['GrassCpuStateParity.cs','GrassPoolCpuParity.cs','GrassSpreadSceneValidation.cs']:
    path=here/(name+'.txt')
    if path.exists():(target/'ContactProfileTemporary/Editor'/name).write_bytes(path.read_bytes())
reference=(here/'ReferenceGrassSimulationPool.cs.txt').read_text(encoding='utf-8').replace('GrassSimulationPool','GrassReferenceSimulationPool')
(target/'ContactProfileTemporary/GrassReferenceSimulationPool.cs').write_text(reference,encoding='utf-8')
print('Prepared CPU',mode,'with identical spread scene, physics and bank.')
