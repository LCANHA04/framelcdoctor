# Find .text instructions that RIP-reference any VA in a given set/range.
import pefile, capstone, sys, struct

PATH = r"D:\SteamLibrary\steamapps\common\NieR Replicant ver.1.22474487139\NieR Replicant ver.1.22474487139.exe"
pe = pefile.PE(PATH, fast_load=True)
base = pe.OPTIONAL_HEADER.ImageBase
data = pe.__data__
secs = [(s.Name.rstrip(b'\x00').decode('latin1'), s.VirtualAddress, s.Misc_VirtualSize,
         s.PointerToRawData, s.SizeOfRawData) for s in pe.sections]
def rva_to_off(rva):
    for n, va, vs, pr, sr in secs:
        if va <= rva < va + max(vs, sr): return pr + (rva - va)
    return None

# targets: list of VAs from argv (hex), match a +/- window
want = [int(a, 16) for a in sys.argv[1:]]
WIN = 0x40  # match anything within this many bytes after a target (structs)
def hit(tgt):
    for w in want:
        if w <= tgt < w + WIN:
            return w
    return None

text = next(s for s in secs if s[0] == '.text')
_, tva, tvs, tpr, tsr = text
code = data[tpr:tpr+tsr]
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64); md.detail = True
found = []
for insn in md.disasm(code, base + tva):
    for op in insn.operands:
        if op.type == capstone.x86.X86_OP_MEM and op.mem.base == capstone.x86.X86_REG_RIP:
            tgt = insn.address + insn.size + op.mem.disp
            w = hit(tgt)
            if w is not None:
                found.append((insn.address, insn.mnemonic, insn.op_str, tgt, w))
print(f"xrefs found: {len(found)}")
for va, m, o, tgt, w in found:
    print(f"  0x{va:X}: {m:7} {o:34} -> 0x{tgt:X} (base 0x{w:X})")
