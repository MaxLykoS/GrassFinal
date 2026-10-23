"""Diagnostic FXC CS5 bytecode stats. Temp registers are NOT NVIDIA physical registers."""
from pathlib import Path
import ctypes as c
import json
import re

root=Path('D:/CodexTemp/codex-grass-contact-profile/Assets/PBDGrass/ContactProfileTemporary')
compiler=c.WinDLL('d3dcompiler_47.dll')
ptr=c.c_void_p
compile_shader=compiler.D3DCompileFromFile
compile_shader.argtypes=[c.c_wchar_p,ptr,ptr,c.c_char_p,c.c_char_p,c.c_uint,c.c_uint,c.POINTER(ptr),c.POINTER(ptr)]
compile_shader.restype=c.c_long
disassemble=compiler.D3DDisassemble
disassemble.argtypes=[ptr,c.c_size_t,c.c_uint,c.c_char_p,c.POINTER(ptr)]
disassemble.restype=c.c_long
def method(blob,index,result,*args):
    table=c.cast(blob,c.POINTER(c.POINTER(ptr))).contents
    return c.WINFUNCTYPE(result,ptr,*args)(table[index])
def data(blob):
    if not blob:return b''
    address=method(blob,3,ptr)(blob);size=method(blob,4,c.c_size_t)(blob)
    return c.string_at(address,size)
def release(blob):
    if blob:method(blob,2,c.c_ulong)(blob)
rows=[]
for name in ['reference','frames','shared','lambda','combo','constant']:
    path=root/('CompactReferenceSolver.compute' if name=='reference' else 'CompactRegister_'+name+'.compute')
    binary=ptr();errors=ptr();assembly=ptr()
    try:
        hr=compile_shader(str(path),None,ptr(1),b'XPBDSolverLOD0',b'cs_5_0',1<<15,0,c.byref(binary),c.byref(errors))
        if hr<0:raise RuntimeError(data(errors).decode('utf-8','replace'))
        hr=disassemble(method(binary,3,ptr)(binary),method(binary,4,c.c_size_t)(binary),0,None,c.byref(assembly))
        if hr<0:raise RuntimeError(str(hr))
        text=data(assembly).decode('utf-8','replace')
        temps=re.search(r'dcl_temps (\d+)',text)
        instructions=re.search(r'Approximately (\d+) instruction slots used',text,re.I)
        row={'name':name,'dxbcBytes':len(data(binary)),'dxbcTemps':int(temps[1]) if temps else None,
             'instructionSlots':int(instructions[1]) if instructions else None}
        print(row);rows.append(row)
    finally:
        for blob in [binary,errors,assembly]:release(blob)
Path(__file__).with_name('FxcStats.json').write_text(json.dumps(rows,indent=2),encoding='utf-8')
