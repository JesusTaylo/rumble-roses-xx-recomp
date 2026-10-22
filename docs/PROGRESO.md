# Bitácora de progreso

Formato por entrada:

## AAAA-MM-DD — Etapa N
- **Logrado:**
- **Pendiente:**
- **Siguiente paso:**

---

## 2026-10-21 — Etapa 0 (diagnóstico)
- **Logrado:** repo git inicializado, `.gitignore` cubre ISO/XEX/`extraido/`/`xex_decrypted/`. Toolchain instalado (VS Build Tools, CMake, Ninja). ISO extraída con extract-xiso 2.7.1 a `extraido/` (63 archivos, 3,82 GB; `default.xex` presente). Submódulo `third_party/rexglue-sdk` (v0.10.0). XEX descifrado/descompreso en `xex_decrypted/default.pe`. Codegen de prueba con `config/rrxx_manifest.toml`: 13 585 funciones, solo 3 llamadas sin resolver, 0 instrucciones sin implementar. 207/215 imports con implementación en el SDK, 8 solo stub. Detalle en `docs/ETAPA0_DIAGNOSTICO.md`.
- **Pendiente:** compilar el SDK desde fuente (la configuración de CMake fue muy lenta, probablemente por el antivirus; se usó el nightly precompilado de `../tools/`). Nada está commiteado todavía. (Layout resuelto después: repo y datos son carpetas hermanas.)
- **Siguiente paso:** esperar revisión del reporte; etapa 1 = declarar las 3 funciones faltantes en un `overrides.toml`, identificar `setjmp`/`longjmp`, crear el proyecto host (`src/`, CMake) y compilar el código generado.

## 2026-10-21 — Etapa 1 (arranque), sesión 2
- **Logrado:**
  - Reorganización: repo, `rrxx_data` (datos, fuera del repo) y build en `build/`. `.gitignore` y `CLAUDE.md` actualizados (sección "Ahorro de tokens").
  - `config/overrides.toml`: 3 funciones de la etapa 0, `setjmp` = 0x824726C0 y `longjmp` = 0x82472470 (identificadas por patrón), y 56 thunks de vtable.
  - Proyecto host con `rexglue init` (manifiesto en la raíz, `src/`, presets); compila con clang 21. El SDK se compiló desde fuente (`tools/build_sdk.ps1`; hizo falta reemplazar en `thirdparty/` los enlaces simbólicos que git deja como texto en Windows).
  - Opción `skip_movies` (activa por defecto): hace fallar la apertura de `*.wmv` mediante el cvar `fail_open_suffixes` añadido al SDK. Con `--skip_movies=false` el juego crashea en 0x821956A8 (función sin registrar en el reproductor XMV); no tratado.
  - Herramientas: `tools/build.ps1`, `run.ps1`, `triage.py`, `stacks.ps1`, `shot.ps1`, `jugar.bat`; `src/crash_handler.cpp` (minidump + pila con nombres `sub_XXXXXXXX`, y volcado de pilas de todos los hilos con `RRXX_STACKS_AFTER=<s>`).
  - Crashes #1 (thunks de vtable) y #2 (FPSCR de hilos host) corregidos; ver `docs/CRASHES.md`. Parches al SDK en `third_party/patches/` (sin commitear en el submódulo).
  - El juego arranca, crea el dispositivo D3D12, y la GPU Xenos y el audio se inicializan. Ventana abierta, pero **negra** (sin primer frame).
- **Pendiente:** primer frame. Estado del cuelgue: tras fallar el vídeo inicial (esperado), el hilo principal del juego gira en `sub_8206ECA8`/`sub_82072168` (lógica de D3D que mira punteros del buffer de comandos, device+10384) y el procesador de comandos de la GPU se queda en un `WAIT_REG_MEM` de memoria (físico 0x1FBF7004, espera valor 0, lee 1). Ni `--vsync=false` ni `--gpu_wait_timeout_ms` lo resuelven (este último parche es de diagnóstico). Sospecha sin confirmar: una escritura de sincronización CPU↔GPU (interrupción de la GPU o write-back) que no llega. Además `CACHE:\PAC\...` falla (el SDK no monta dispositivo cache a propósito); el juego sigue sin él.
- **Siguiente paso:** sesión nueva dedicada al cuelgue gráfico: (1) registrar qué dirección guest escribe 0x1FBF7004 y qué paquete PM4 espera; (2) comprobar que `DispatchInterruptCallback(1, …)` llega al callback del juego (0x8206E158); (3) si no, comparar el flujo `VdSwap`/`VdInitializeRingBuffer` con el de Xenia.

## 2026-10-21 — Etapa 1 (arranque), sesión 3
- **Logrado:** commit de la etapa 1. **Hay imagen: el video de intro se reproduce** (capturas a 25 s y 90 s, `logs/shot_*.png`, no versionadas). La causa de la pantalla negra era forzar el fallo del `.wmv`. Se arregló con funciones sin registrar (crashes #3 y #4 en `docs/CRASHES.md`): nuevo `tools/gaps.py` detecta en lote funciones sin llamada directa tras el fin de otra; 116 añadidas a `config/overrides.toml` (con filtro de destinos de tablas de salto, crash #5). `skip_movies` ahora es `false` por defecto.
- **Pendiente:** no se ha verificado que pase del video a los menús (a 90 s seguía en video; ¿el intro es largo o hay cuelgue?). El log aún muestra `WAIT_REG_MEM ... 1FBF7006` una vez, pero ya no bloquea (plan C no hizo falta). `CACHE:\` sin dispositivo (el juego sigue). Video con aspecto algo borroso/posterizado: revisar decodificación XMV más adelante. Los candidatos de gaps.py descartados (18 por tabla de saltos, 1 por zona de datos) podrían incluir funciones reales: si hay otro "unregistered function", revisar `logs/gaps.txt`.
- **Siguiente paso:** sesión nueva: dejar correr >3 min con captura periódica para ver si llega a menús; si el video termina y cuelga, comparar con el comportamiento de Xenia; luego entrada de mando (etapa 2).

## 2026-10-21 — Hito: 40 min de juego sin crash
- **Logrado:** sesión de 40 min (`logs/run_20261021_205612.log`, no versionado): video de intro, menús y 2 combates completos sin crash. Fin de la etapa 1 y avance sustancial de las etapas 2–3 (confirmado por el usuario jugando).
- **Pendiente:** TDR de GPU visto una vez (run_20261021_202750), advertencias de textura inválida, `PHOTO:\`/`CACHE:\` sin dispositivo, rendimiento (lag/tirones en iGPU Intel UHD).
- **Siguiente paso:** ver entrada siguiente (optimización y pendientes).

## 2026-10-21 — Sesion de pendientes (TDR, avisos, montajes, rendimiento) — INCOMPLETA
- **Logrado (en el repo):**
  - `CACHE:\` y `PHOTO:\` montados en `<carpeta de datos de usuario>\rrxx\guest_cache` y `\photos` (`src/rrxx_app.h`, `OnPostSetup`); verificado en log ("Montado cache: ...").
  - Avisos "fetch constant invalid" (textura y vertex) una sola vez por tipo; contador de FPS (`--gpu_log_fps=N`, `tools/fps.py` lo resume); `--gpu_track_draws`. Todo es parche al SDK: `third_party/patches/0004-*.patch` (el submodulo sigue sin commitear).
  - `config/rrxx.toml` (RTV/ROV, `resolution_scale`, `gpu_log_fps`); `tools/run.ps1` lo copia junto al exe si no existe. Preset nuevo `dev-release-opt` (Release, -march=native, LTO/lld) en `CMakeUserPresets.json`; compila (~1,5 min).
  - Crash #6 (0x822156F0) registrado en overrides. Falta verificar con un build nuevo tras `-Codegen` (compilo, no probado en partida larga).
- **Mediciones (sin entrada, ~2 min desde el arranque, escena de titulo; FPS por swap):** RelWithDebInfo+ROV: titulo ~15 FPS (media 34,7 en 22 muestras). RelWithDebInfo+RTV: titulo ~40 FPS (media ~52). Release y Release+LTO+native con RTV: sin diferencia medible (la escena depende de la GPU; muestras truncadas por los TDR). **La ganancia real es ROV->RTV (~2,7x en la escena de titulo).** Sin comprobar aun: errores graficos con RTV (nadie lo ha jugado), escala 1x (ya es el valor por defecto), cache de shaders (log: "Translated 18 shaders from the storage"; parece persistir).
- **TDR (punto 1):** NO es limite de ROV ni de "tess_mode" (es un rectlist/quad, el registro tess queda de dibujos anteriores). Pasa con RTV y ROV, intermitente, y un TDR real son >2 s en un submit, no lentitud: parece bug de backend/shader o driver Intel. Pista: en la submission colgada aparecieron VS 0A6D1DD7767FDF27/ps 2E372EA28CC404B7 (rect 3 vertices), VS 5CE193255CA96621 / mismo PS (quad), VS 4260E486DE39BA45/PS 9567C79307ACC6F5 (quad), VS 6F26188EB7F685FF/PS B14C2134FC2A557E (1145 verts). 3 intentos agotados; no resuelto.
- **Pendiente / siguiente paso:** (1) rebuild SDK Release + `dev-release-opt` y repetir `--gpu_track_draws=true --dump_shaders=build/shaderdump` hasta un TDR para ver el orden de draws (el flush del log ya esta anadido); leer el ucode de esos PS. (2) Confirmar con juego real que RTV no da errores graficos y dejar `render_target_path_d3d12="rtv"` (ya es el valor de `config/rrxx.toml`). (3) Medir FPS en combate (sin entrada no se llega).

## 2026-10-21 — Sesion TDR #7 (RTV por defecto, preset versionado, diagnostico)
- **Logrado:**
  - `dev-release-opt` movido a `CMakePresets.json` (commit 7c75489; usa `CMAKE_PREFIX_PATH=build/sdk-install` directamente, ya no depende de `user-env`). `config/rrxx.toml` ya tenia `render_target_path_d3d12 = "rtv"` (verificado, identico al del exe).
  - SDK y juego recompilados con `dev-release-opt`; arranca y corre (~45-60 FPS en intro/titulo, tramos a 30 = vsync).
  - Diagnostico del TDR: flush del log + mensaje en las dos rutas de perdida de dispositivo (antes el log se perdia: la perdida se detectaba en `Presenter`/`GraphicsSystem` y hacia `abort()` sin volcar), `abort()` deja pila (`crash_dumps/abort_*.txt`) y stderr (`stderr.txt`), DRED activo sin necesitar `--d3d12_debug`. Parche `third_party/patches/0005-*`.
  - **Hipotesis de teselacion descartada** (ver crash #7): `tess_mode=1` aparece solo en los draws que fallan *despues* de perder el dispositivo; el tracker marca `tess=false` en todos. No se anade opcion de config para teselacion (no existe ni hay evidencia). Reproduccion del usuario (Candy Cane c1, Dixie en personalizacion) no se pudo reproducir aqui sin entrada; 3 capturas gastadas (run 224627, 224923, 225308).
- **Pendiente:** causa del TDR. Siempre aparece PS 2E372 (`oC0=r0`) + VS 5CE19/0A6D en el ultimo draw, que parece sintoma. La linea "DRED enabled" no aparecio en el log (puede ser filtro de categoria `ui`); falta confirmar que los breadcrumbs se imprimen.
- **Siguiente paso:** el usuario reproduce Candy Cane color 1 con este build y pasa el log: ahora debe traer `Ultimos draws`, breadcrumbs DRED (op que falla) y la pila en `abort_*.txt`. Si hay texto `DRED page fault`, es acceso a memoria invalida (textura/buffer); si no, probar `--d3d12_submit_on_primary_buffer_end=false`, `--gpu_allow_invalid_fetch_constants=true` (el aviso de fetch constant invalido aparece ~10 s antes de los dos TDR de hoy) y subir `TdrDelay` (HKLM\...\GraphicsDrivers, 10-20 s) para distinguir cuelgue real de lentitud.


## 2026-10-22 — Mando virtual y experimento de tiempo del TDR #7
- **Logrado:** parche 0006 al SDK (`--input_record` / `--input_script`, mando virtual slot 0; formato `tiempo_ms BOTON down|up`); `tools/run.ps1`/`jugar.bat` usan `dev-release-opt` (uso: `cd` al repo y `-GameArgs '--x=y'`); `triage.py` extrae lineas DRED; etiqueta git `jugable-v1`; `tools/timing_test.py` (N arranques sin input, secuenciales, CSV). Resultado: 3/10 TDR, todos a ~174 s, con page fault DRED en un `DrawIndexedInstanced` (ver `docs/CRASHES.md`, crash #7). Con el script del usuario hasta la seleccion hubo TDR a ~62 s. dev-release-opt **si** sufre el TDR.
- **Pendiente:** causa del page fault; `autotest.py` y la bateria de 88 casos en pausa (faltan cursor inicial, boton de color y punto de corte de `base.txt`; el usuario los dara otro dia).
- **Siguiente paso:** probar (con permiso) `--d3d12_tiled_shared_memory=false` con `timing_test.py` (n=10): si desaparecen los TDR, la causa es el buffer tiled de memoria compartida.

## 2026-10-22 (2) — Diagnostico de asignaciones para el TDR #7
- **Logrado:** parche 0007 al SDK (solo diagnostico): DRED OUTPUT1 con asignaciones existing/recently freed, `SetName` + log de nombre/GPU VA/tamano de los recursos grandes (shared memory, EDRAM, index buffers, upload pool, scratch, texturas...). `triage.py` incluye esas lineas. SDK y dev-release-opt recompilados; arranque de 25 s sin crash y con las lineas `GPU resource ...`. Hipotesis del buffer tiled descartada por el usuario.
- **Pendiente:** reproducir el TDR con este build (el usuario, jugando) y leer `DRED EXISTING/RECENTLY FREED allocation` + la VA contra los `GPU resource`. Si sale RECENTLY FREED: use-after-free (explicar cual y por que antes de arreglar).
- **Siguiente paso:** `python -I tools/triage.py <log>` tras el TDR; no se ha cambiado logica del juego.

## 2026-10-22 (3) — TDR #7: causa localizada y mitigada
- **Logrado:** parche 0008 (nombres de pool, avisos de lectura fuera de limites, margen de 64 KB en las paginas de las pools de subida, cvar `d3d12_upload_page_guard_margin`, activo por defecto). El TDR cae siempre en el fin de la `constant_page` mas reciente. Sin margen 6/10 TDR (~178 s); con margen 0/15. `timing_test.py` ampliado (`--tag`, `--extra`, CSV de avisos).
- **Pendiente:** el origen exacto del exceso de lectura (la CPU no ve ninguna CBV mayor que su asignacion salvo `descriptor_indices`, <=12 B en pagina); el margen es mitigacion, no arreglo de fondo. Bateria de 88 casos sigue en pausa (faltan datos de navegacion).
- **Siguiente paso:** el usuario prueba jugando (menu de personalizacion varias veces); decidir si ademas se rellena `descriptor_indices` a 16 B y si se investiga la lectura GPU de CBV.

## 2026-10-22 (4) — Confirmacion del crash #7, icono y preparacion de copia publica
- **Logrado:** el usuario confirmo jugando que el TDR #7 ya no ocurre con el margen de 64 KB (2 combates + varias entradas a personalizacion). Registrado en `docs/CRASHES.md`. `config/rrxx.toml` queda con `d3d12_upload_page_guard_margin = true` (y `gpu_allow_invalid_fetch_constants = true`, que seguia activo durante la prueba; pendiente decidir si se quita).
- **Pendiente:** causa de fondo del exceso de lectura de CBV; bateria de 88 casos en pausa.
- **Siguiente paso:** ver entradas posteriores (icono, copia publica).

## 2026-10-22 (5) — Icono del exe y la ventana
- **Logrado:** `resources/rrxx.rc` (icono ID 1) agregado al target en CMake (`enable_language(RC)`); `RrxxApp::OnCreateDialogs` aplica el icono a la ventana con `WM_SETICON` (el SDK no implementa `Window::SetIcon` en SDL). Verificado: `ExtractAssociatedIcon` del exe y `WM_GETICON` de la ventana devuelven el icono. `rrxx_vino.ico` queda sin versionar (variante no usada).

## 2026-10-22 (6) — Preparacion de la copia publica
- **Logrado:** primer commit publico (historial desde cero), rutas relativas (`build/` en el repo, datos en `../rrxx_data`), `setup.bat`/`setup.ps1` probado desde cero con un clon limpio (extract-xiso, SDK parcheado, codegen, build, `jugar.bat`, acceso directo; el juego arranca y corre sin crash 40 s), README, plantilla de issues y `third_party/patches/README.md`. Los parches se regeneraron como serie LF que aplica limpia sobre el SDK c94f5eb.
- **Pendiente:** licencia (propuesta BSD-3-Clause, sin agregar hasta aprobacion); publicar.
