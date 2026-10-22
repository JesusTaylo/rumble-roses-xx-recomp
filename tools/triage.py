#!/usr/bin/env python3
"""Resume un log de ejecucion/crash en pocas lineas.

Uso: python tools/triage.py <log> [--exit-code N] [--funcs generated/default/rrxx_funcs.h]

Devuelve: direccion guest, funcion recompilada mas cercana, pila resumida (ultimos eventos
relevantes del log) y tipo probable segun docs/CRASHES.md. Nunca imprime el log completo.
"""
import argparse
import bisect
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TYPES = {
    "funcion-no-detectada": "funcion no detectada por el analisis estatico (anadir a [functions] de config/overrides.toml)",
    "tabla-saltos": "tabla de saltos mal resuelta (anadir [[switch_tables]])",
    "kernel-faltante": "llamada al kernel sin implementar / stub",
    "vmx128": "instruccion VMX128 mal traducida",
    "gpu-xenos": "problema de GPU/Xenos",
    "otro": "otro",
}

ADDR = r"0x([0-9A-Fa-f]{8})"
RE_LINE = re.compile(r"^\[(?P<ts>[^\]]+)\] \[(?P<lvl>\w+)\] \[(?P<cat>\w+)\] \[t(?P<tid>\d+)\] (?P<msg>.*)$")
RE_INVALID = re.compile(r"invalid or unregistered function at guest address " + ADDR, re.I)
RE_ANY_GUEST = re.compile(r"\b(?:guest address|address|lr|pc|ip)[ =:]*" + ADDR, re.I)
RE_SWITCH = re.compile(r"switch|jump table|bctr", re.I)
RE_KERNEL = re.compile(r"unimplemented|not implemented|STUB|stub|\[NtCreateFile\] FAILED|import", re.I)
RE_VMX = re.compile(r"vmx|altivec|vperm|PPC_UNIMPLEMENTED", re.I)
RE_GPU = re.compile(r"\bgpu\b|vulkan|d3d12|shader|xenos|swap|present|edram", re.I)
NOISE = re.compile(r"CACHE:|ResolvePath\(CACHE|VFS: 'CACHE|GameControllerDB|SDL ")


def load_funcs(path: Path):
    if not path.exists():
        return []
    text = path.read_text(errors="ignore")
    return sorted({int(m, 16) for m in re.findall(r"sub_([0-9A-F]{8})", text)})


def nearest(funcs, addr):
    i = bisect.bisect_right(funcs, addr) - 1
    return (funcs[i], addr - funcs[i]) if i >= 0 else (None, None)


def classify(text, fatal_kind, crit_lines):
    if fatal_kind == "invalid":
        return "funcion-no-detectada"
    blob = "\n".join(crit_lines) or text[-4000:]
    if RE_SWITCH.search(blob):
        return "tabla-saltos"
    if RE_VMX.search(blob):
        return "vmx128"
    if RE_KERNEL.search(blob):
        return "kernel-faltante"
    if RE_GPU.search(blob):
        return "gpu-xenos"
    return "otro"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("log")
    ap.add_argument("--exit-code", type=int, default=None)
    ap.add_argument("--funcs", default=str(ROOT / "generated/default/rrxx_funcs.h"))
    ap.add_argument("--stack", type=int, default=6, help="eventos relevantes previos a mostrar")
    ap.add_argument("--native", default=None, help="informe native_*.txt del crash handler (pila nativa)")
    a = ap.parse_args()

    p = Path(a.log)
    if not p.exists():
        print(f"triage: no existe {p}")
        return 2
    if p.suffix.lower() == ".dmp":
        print(f"triage: {p.name} es un minidump ({p.stat().st_size // 1024} KB); abrir con WinDbg/VS. Sin log asociado.")
        return 0

    lines = p.read_text(errors="ignore").splitlines()
    parsed = []
    for ln in lines:
        m = RE_LINE.match(ln)
        if m:
            parsed.append(m.groupdict())
    if not parsed:
        print(f"triage: {p.name}: {len(lines)} lineas sin formato reconocido")
        return 1

    addr = fatal_kind = fatal_msg = fatal_idx = None
    for i, e in enumerate(parsed):
        m = RE_INVALID.search(e["msg"])
        if m:
            addr, fatal_kind, fatal_msg, fatal_idx = int(m.group(1), 16), "invalid", e["msg"], i
            break
    if fatal_idx is None:
        for i, e in enumerate(parsed):
            if e["lvl"] in ("critical", "fatal"):
                m = RE_ANY_GUEST.search(e["msg"]) or re.search(ADDR, e["msg"])
                addr = int(m.group(1), 16) if m else None
                fatal_kind, fatal_msg, fatal_idx = "critical", e["msg"], i
                break

    funcs = load_funcs(Path(a.funcs))
    end = fatal_idx if fatal_idx is not None else len(parsed)
    prior = [e for e in parsed[:end] if e["lvl"] in ("warning", "error", "critical") and not NOISE.search(e["msg"])]
    # deduplicar manteniendo orden, ultimos N
    seen, stack = set(), []
    for e in reversed(prior):
        key = re.sub(ADDR, "0xN", e["msg"])
        if key not in seen:
            seen.add(key)
            stack.append(e)
        if len(stack) >= a.stack:
            break
    stack.reverse()

    crashed = fatal_idx is not None or (a.exit_code not in (None, 0))
    kind = classify(" ".join(e["msg"] for e in parsed[-60:]), fatal_kind, [e["msg"] for e in stack])

    native_lines = []
    if a.native and Path(a.native).exists():
        nat = Path(a.native).read_text(errors="ignore").splitlines()
        head = next((l for l in nat if l.startswith("NATIVE CRASH")), "")
        native_lines.append(head[:160])
        frames = [l.strip() for l in nat if re.match(r"\s+#\d+ ", l)]
        guest = [(re.search(r"sub_([0-9A-F]{8})(\+0x[0-9A-F]+)?", f), f) for f in frames]
        guest = [(m, f) for m, f in guest if m]
        short = lambda f: re.sub(r"\(C:\\.*\\([^\\]+:\d+)\)", r"(\1)", re.sub(r"__imp__", "", f))[:150]
        for f in frames[:3]:
            native_lines.append("  " + short(f))
        if guest:
            first = int(guest[0][0].group(1), 16)
            if addr is None:
                addr = first
            native_lines.append("Cadena guest (llamado <- llamador): " +
                                " <- ".join("sub_" + m.group(1) for m, _ in guest[:6]))
        code = re.search(r"code=0x([0-9A-F]+)", head)
        if code and fatal_kind is None:
            c = int(code.group(1), 16)
            if 0xC000008C <= c <= 0xC0000093:
                kind = "otro"
                native_lines.append("Causa: excepcion de punto flotante SSE sin enmascarar (MXCSR); estado FPSCR del hilo mal inicializado, no instruccion mal traducida.")
            elif c == 0xC0000005:
                kind = "otro" if not any("rex::" in f for f in frames[:4]) else "kernel-faltante"
            elif c in (0xC000001D, 0xC000001E):
                kind = "vmx128"
        crashed = True

    out = []
    out.append(f"RESULTADO: {'CRASH' if crashed else 'sin crash'} | log={p.name} | exit={a.exit_code} | lineas={len(lines)}")
    if fatal_msg:
        out.append(f"FATAL: {fatal_msg[:200]}")
    if addr is not None:
        f, off = nearest(funcs, addr)
        if f is not None:
            out.append(f"Guest: 0x{addr:08X} | funcion mas cercana: sub_{f:08X}+0x{off:X}" + ("" if off < 0x1000 else " (lejos: probablemente NO es una funcion registrada)"))
        else:
            out.append(f"Guest: 0x{addr:08X} (sin rrxx_funcs.h para buscar la funcion)")
    else:
        out.append("Guest: no aparece direccion guest en el log")
    out.extend(native_lines)
    if stack:
        out.append("Eventos previos relevantes (mas reciente al final):")
        for e in stack:
            out.append(f"  [{e['lvl'][:4]}/{e['cat']}] t{e['tid']} {e['msg'][:140]}")
    dred = []
    for ln in lines:
        if re.search(r"DRED page fault|DRED .*allocation|<-- FAULT|device removed|Graphics device lost", ln):
            dred.append(re.sub(r"^(\[[^\]]*\] ){4}", "", ln)[:160])
        m = re.search(r"DRED breadcrumb: completed (\d+) of (\d+) ops", ln)
        if m and int(m.group(1)) < int(m.group(2)):
            dred.append(f"DRED breadcrumb incompleto: {m.group(1)} de {m.group(2)} ops")
    if dred:
        out.append("DRED / perdida de dispositivo:")
        out.extend("  " + d for d in dred[-14:])
        if kind == "otro":
            kind = "gpu-xenos"
    out.append(f"Tipo probable: {kind} -> {TYPES[kind]}")
    if kind == "funcion-no-detectada" and addr is not None:
        out.append(f"Sugerencia: revisar si 0x{addr:08X} es inicio de funcion y anadir '0x{addr:08X} = {{}}' en [functions].")
    print("\n".join(out[:40]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
