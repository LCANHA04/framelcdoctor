#!/usr/bin/env python3
"""Generate export forwarders for a proxy DLL.

When FrameLCDoctor is packaged for a game, its core DLL is renamed to the proxied
system DLL (d3d11/dxgi/winmm/...). To stay transparent it must re-export every symbol
of the real DLL, forwarding to a renamed copy (e.g. d3d11_orig.dll).

Usage:
    python gen_forwarders.py C:\\Windows\\System32\\d3d11.dll d3d11_orig > forwarders.generated.h

Requires dumpbin (VS) on PATH, or pass --dumpbin <path>.
"""
import subprocess, sys, argparse, shutil, re

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("real_dll", help="path to the real system DLL")
    ap.add_argument("forward_module", help="module name to forward to, e.g. d3d11_orig")
    ap.add_argument("--dumpbin", default=shutil.which("dumpbin") or "dumpbin")
    args = ap.parse_args()

    out = subprocess.run([args.dumpbin, "/exports", args.real_dll],
                         capture_output=True, text=True)
    if out.returncode != 0:
        sys.exit(f"dumpbin failed: {out.stderr}")

    names = []
    for line in out.stdout.splitlines():
        m = re.match(r"\s+\d+\s+[0-9A-Fa-f]+\s+[0-9A-Fa-f]+\s+(\w+)", line)
        if m:
            names.append(m.group(1))

    print(f"// Auto-generated. Forwards {len(names)} exports to {args.forward_module}.dll")
    print("#pragma once")
    for n in names:
        print(f'#pragma comment(linker, "/export:{n}={args.forward_module}.{n}")')

if __name__ == "__main__":
    main()
