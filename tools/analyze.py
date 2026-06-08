# Phase 1 static analysis: locate the frame-timer / fixed 1/60 delta in the exe.
import pefile, capstone, struct, sys

PATH = r"D:\SteamLibrary\steamapps\common\NieR Replicant ver.1.22474487139\NieR Replicant ver.1.22474487139.exe"
pe = pefile.PE(PATH, fast_load=True)
pe.parse_data_directories(directories=[pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_IMPORT']])
base = pe.OPTIONAL_HEADER.ImageBase
data = pe.__data__

secs = []
for s in pe.sections:
    name = s.Name.rstrip(b'\x00').decode('latin1')
    secs.append((name, s.VirtualAddress, s.Misc_VirtualSize, s.PointerToRawData, s.SizeOfRawData))

def off_to_rva(off):
    for n, va, vs, pr, sr in secs:
        if pr <= off < pr + sr:
            return va + (off - pr)
    return None
def rva_to_off(rva):
    for n, va, vs, pr, sr in secs:
        if va <= rva < va + max(vs, sr):
            return pr + (rva - va)
    return None

# ---- 1. constant data sites ----
consts = {
    '1/60 f32':   struct.pack('<f', 1/60),
    '1/60 f64':   struct.pack('<d', 1/60),
    '60.0 f32':   struct.pack('<f', 60.0),
    '60.0 f64':   struct.pack('<d', 60.0),
    '1000/60 f32':struct.pack('<f', 1000/60),
    '0.0333 f32': struct.pack('<f', 1/30),   # 1/30 in case it targets 30
}
target_const = {}  # rva -> name
for name, b in consts.items():
    start = 0
    while True:
        i = data.find(b, start)
        if i < 0: break
        if i % 4 == 0:
            rva = off_to_rva(i)
            if rva is not None:
                target_const[rva] = name
        start = i + 1

# ---- 2. timing IAT slots ----
WANT = {'QueryPerformanceCounter','QueryPerformanceFrequency','timeBeginPeriod',
        'timeGetTime','Sleep','SleepEx','WaitForSingleObject',
        'CreateWaitableTimerW','SetWaitableTimer'}
iat = {}  # iat-slot rva -> funcname
for entry in pe.DIRECTORY_ENTRY_IMPORT:
    for imp in entry.imports:
        if imp.name and imp.name.decode('latin1') in WANT:
            iat[imp.address - base] = imp.name.decode('latin1')

print(f"image base 0x{base:X}")
print(f"const data sites: {len(target_const)}   timing IAT slots: {len(iat)}")
for rva, fn in sorted(iat.items()):
    print(f"  IAT slot va 0x{base+rva:X}  {fn}")

# ---- 3. linear disasm of .text, find RIP refs to consts + IAT ----
text = next(s for s in secs if s[0] == '.text')
_, tva, tvs, tpr, tsr = text
code = data[tpr:tpr+tsr]
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
md.detail = True

hits_const = []   # (va, mnem, opstr, const-name)
hits_iat   = []   # (va, mnem, opstr, func)
for insn in md.disasm(code, base + tva):
    for op in insn.operands:
        if op.type == capstone.x86.X86_OP_MEM and op.mem.base == capstone.x86.X86_REG_RIP:
            tgt = insn.address + insn.size + op.mem.disp
            trva = tgt - base
            if trva in target_const:
                hits_const.append((insn.address, insn.mnemonic, insn.op_str, target_const[trva]))
            elif trva in iat:
                hits_iat.append((insn.address, insn.mnemonic, insn.op_str, iat[trva]))

print(f"\n=== refs to 1/60 & 60.0 constants ({len(hits_const)}) ===")
for va, m, o, nm in hits_const:
    print(f"  0x{va:X}  {m:6} {o:30}  ; {nm}")
print(f"\n=== refs to timing imports ({len(hits_iat)}) ===")
for va, m, o, fn in hits_iat:
    print(f"  0x{va:X}  {m:6} {o:30}  ; {fn}")
