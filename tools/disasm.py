# Disassemble a window around a VA, resolving RIP-relative targets.
import pefile, capstone, sys

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

def dump(center_va, before=0x60, after=0xC0):
    start_va = center_va - before
    off = rva_to_off(start_va - base)
    code = data[off:off + before + after]
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
    md.detail = True
    print(f"\n===== window around 0x{center_va:X} =====")
    for insn in md.disasm(code, start_va):
        mark = ">>" if insn.address == center_va else "  "
        extra = ""
        for op in insn.operands:
            if op.type == capstone.x86.X86_OP_MEM and op.mem.base == capstone.x86.X86_REG_RIP:
                tgt = insn.address + insn.size + op.mem.disp
                toff = rva_to_off(tgt - base)
                val = ""
                if toff is not None:
                    import struct as st
                    raw = data[toff:toff+8]
                    if len(raw) >= 4:
                        f32 = st.unpack('<f', raw[:4])[0]
                        val = f" => [0x{tgt:X}] f32={f32:.8g}"
                        if len(raw) == 8:
                            f64 = st.unpack('<d', raw)[0]
                            val += f" f64={f64:.8g}"
                extra += val
        print(f"{mark} 0x{insn.address:X}: {insn.mnemonic:7} {insn.op_str}{extra}")

for va in [int(a, 16) for a in sys.argv[1:]]:
    dump(va)
