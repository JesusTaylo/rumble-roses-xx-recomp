"""Resume las lineas 'FPS:' de un log de ejecucion (gpu_log_fps). Uso: python -I tools/fps.py LOG [muestras_max]"""
import re, statistics, sys
vals = [float(m.group(1)) for l in open(sys.argv[1], encoding='utf-8', errors='replace')
        if (m := re.search(r'FPS: ([0-9.]+)', l))]
if len(sys.argv) > 2: vals = vals[:int(sys.argv[2])]
if not vals: sys.exit('sin muestras de FPS')
s = sorted(vals)
print(f"n={len(vals)} media={statistics.mean(vals):.1f} mediana={statistics.median(vals):.1f} "
      f"min={s[0]:.1f} p10={s[len(s)//10]:.1f} max={s[-1]:.1f}")
