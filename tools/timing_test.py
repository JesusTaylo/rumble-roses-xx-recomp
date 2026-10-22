"""Arranca el juego N veces SIN input, en secuencia, y anota si crasheo y en que segundo.

Uso: python -I tools/timing_test.py [--runs 10] [--seconds 180] [--preset dev-release-opt]
Salida: logs/timing_test.csv (run, resultado, segundo, exit, va_dred, op_dred). Los logs completos
solo se conservan (en build/timing/) de las ejecuciones que fallan.
"""
import argparse
import csv
import os
import re
import subprocess
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT.parent / "rrxx_data" / "extraido"
OUT = ROOT / "build" / "timing"


def dred_info(log):
    va = op = ""
    if not log.exists():
        return va, op
    prev = ""
    for ln in log.read_text(errors="ignore").splitlines():
        m = re.search(r"DRED page fault at VA (0x[0-9A-Fa-f]+)", ln)
        if m:
            va = m.group(1)
        if "<-- FAULT" in ln:
            m = re.search(r"op type (\d+)", ln)
            op = m.group(1) if m else ""
    return va, op


def oob_info(log):
    """Avisos 'VIEW OOB' del log: {clave: (veces_max, exceso_max)}."""
    res = {}
    if not log.exists():
        return res
    for ln in log.read_text(errors="ignore").splitlines():
        m = re.search(r"VIEW OOB \[([^\]]+)\].*?\(max (\d+)\) veces=(\d+)", ln)
        if m:
            k = m.group(1)
            res[k] = (max(int(m.group(3)), res.get(k, (0, 0))[0]), max(int(m.group(2)), res.get(k, (0, 0))[1]))
    return res


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--runs", type=int, default=10)
    ap.add_argument("--seconds", type=int, default=180)
    ap.add_argument("--preset", default="dev-release-opt")
    ap.add_argument("--tag", default="", help="sufijo de los CSV (timing_test_<tag>.csv)")
    ap.add_argument("--extra", nargs="*", default=[], help="argumentos extra para el juego")
    a = ap.parse_args()
    suffix = f"_{a.tag}" if a.tag else ""
    exe = ROOT / "build" / a.preset / "rrxx.exe"
    OUT.mkdir(parents=True, exist_ok=True)
    (ROOT / "logs").mkdir(exist_ok=True)
    rows = []
    oob_all = {}  # clave -> [corridas_con_aviso, suma_veces, exceso_max]
    for i in range(1, a.runs + 1):
        log = OUT / f"timing{suffix}_{i:02d}.log"
        log.unlink(missing_ok=True)
        env = dict(os.environ, RRXX_CRASH_DIR=str(OUT))
        t0 = time.time()
        p = subprocess.Popen([str(exe), "--game_data_root", str(DATA), "--log_file", str(log)] + a.extra,
                             cwd=exe.parent, env=env)
        try:
            code = p.wait(timeout=a.seconds)
            res, sec = "CRASH", round(time.time() - t0, 1)
        except subprocess.TimeoutExpired:
            p.kill()
            p.wait()
            res, sec, code = "OK", a.seconds, 0
        va, op = dred_info(log) if res == "CRASH" else ("", "")
        for k, (n, ex) in oob_info(log).items():
            e = oob_all.setdefault(k, [0, 0, 0])
            e[0] += 1
            e[1] += n
            e[2] = max(e[2], ex)
        with open(ROOT / "logs" / f"timing_oob{suffix}.csv", "w", newline="") as f:
            w = csv.writer(f)
            w.writerow(["clave", "corridas_con_aviso", "veces_total", "exceso_max_bytes"])
            for k, (r, n, ex) in sorted(oob_all.items()):
                w.writerow([k, r, n, ex])
        if res == "OK" and not any("FUERA" in k for k in oob_info(log)):
            log.unlink(missing_ok=True)
        rows.append([i, res, sec, code, va, op])
        with open(ROOT / "logs" / f"timing_test{suffix}.csv", "w", newline="") as f:
            w = csv.writer(f)
            w.writerow(["run", "resultado", "segundo", "exit", "va_dred", "op_dred"])
            w.writerows(rows)
        time.sleep(5)  # deja que el driver/GPU se asiente entre ejecuciones


if __name__ == "__main__":
    main()
