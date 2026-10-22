# Etapa 0 — Diagnóstico de `default.xex`

Solo cifras y observaciones; nada del contenido del juego.

## Entorno
- Disposición de carpetas: el repo y, a su lado (fuera del repo), `rrxx_data/extraido/` con los archivos del juego.
- Datos: imagen de disco XGD2 de la copia propia del usuario, extraída con extract-xiso 2.7.1 (63 archivos, 3,82 GB).
- SDK: submódulo `third_party/rexglue-sdk` en v0.10.0 (el diagnóstico inicial usó un nightly precompilado 0.10.0.24).
- Aviso: la wiki del SDK está desfasada respecto a v0.10 (`rexglue init` ahora usa `--project-name/--xex-path`, y el manifiesto es `[project]`/`[entrypoint]`, como en el port de Banjo-Tooie).
- Toolchain: VS Build Tools 2022 (MSVC + clang), CMake, Ninja.

## XEX
| Campo | Valor |
|---|---|
| Formato | XEX2, módulo de título, no es parche |
| Cifrado | AES-128 normal, clave retail (la descifra el propio SDK) |
| Compresión | *basic* (zero-run), no LZX. 5,1 MB de datos + 6,1 MB de ceros = imagen de 0xAC0000 (10,75 MiB) |
| Título / media ID | 4B4E07D1 / 1A6D4362, versión 3, build 2006-02-18 |
| Base / entry | 0x82000000 / 0x82066F10 |
| Pila por defecto | 0x180000 |
| Máquina PE | 0x1F2 (PowerPC BE), 16 secciones |
| `.text` | 4,28 MB (~4,17 MB de código en total, incl. `.embsec_`), 1,08 M instrucciones |
| `.data` | 6,3 MB virtuales (casi todo BSS) |
| `.pdata` | 77 KB (PDATA presente, ayuda al descubrimiento de funciones) |
| Librerías estáticas | XDK 2417: XAPILIB, D3D9, D3DX9, XGRAPHC, LIBCMT, X3DAUD, XAUD, XACT, XMP, XMEDIA, XNET, XONLINE, XHV |
| Imports | 215 **funciones distintas** (ordinales únicos), que es la cifra que importa para la cobertura. Las cifras 273 (`xboxkrnl.exe`) y 168 (`xam.xex`) son **registros** (entradas de 4 bytes) de la tabla de imports del XEX, no funciones. Cada función importada suele ocupar 2 registros (el de ordinal/dato y el del thunk de llamada) y cada variable importada 1; 441 ≈ 2 × 215 + ~11 variables (interpretación mía, coherente con las cifras; no la verifiqué registro a registro). Las dos cifras no son comparables. |

El SDK descifra y descomprime el XEX por sí mismo al ejecutar `rexglue codegen`; no hace falta ninguna herramienta externa.

## Codegen (rexglue 0.10.0.24, sin overrides)
- 6,2 s, 13 585 funciones, 73 MB de C++ en 70 archivos de código (en `generated/`, ignorado).
- Solo 3 errores `UnresolvedCall` (saltos `b` a destinos no detectados como función): 0x823649B0, 0x82364958, 0x82344F90.
- 0 `PPC_UNIMPLEMENTED`; 592 `__builtin_trap` son los `default:` de `switch` fuera de rango, no instrucciones faltantes.
- Vector: 2,8 % de las instrucciones (30 327). VMX128 explícito ≈ 12 000 (`lvx128/stvx128`, `vmsum4fp128`, `vmulfp128`, `vpermwi128`, `vrlimi128`, `vpkd3d/vupkd3d`, `lvlx/lvrx/stvlx/stvrx`). Uso moderado, sin núcleos masivos.
- Sin accesos MMIO detectados; `setjmp`/`longjmp` aún sin identificar (hay SEH y C++ EH: se importan `__C_specific_handler`, `RtlUnwind`, `RtlRaiseException`).

## Imports del kernel frente al SDK
- 207 de 215 con implementación real (203 `REX_EXPORT` + 4 `REX_HOOK_RAW`: `DbgPrint`, `sprintf`, `vsprintf`, `_vsnprintf`).
- 8 solo con stub (`REX_EXPORT_STUB`): `NetDll_XNetRegisterKey`, `NetDll_XNetUnregisterKey`, `VdInitializeEDRAM`, `XamShowGamerCardUIForXUID`, `XamShowPlayerReviewUI`, `XamUserCreateStatsEnumerator`, `XamVoiceSubmitPacket`, `XNetLogonGetTitleID`.
- Ninguno falta del todo. Que exista no implica que sea correcto: la verificación real es en ejecución.
- Ojo: `VdInitializeEDRAM` es stub y el juego lo llama; hay que ver si el backend gráfico lo necesita.

## Estructura de datos del juego
`default.xex`, `$SystemUpdate/` (actualización del sistema, irrelevante), `movies/` (32 `.wmv`, 648 MB), `music/` (4 `.xwb` de XACT, 311 MB), `pac/` (15 `.pac` + 2 `.h`, 735 MB), `pac/ch/` (6 `.pac`, 1,56 GB), `pac/ed/` (4 `.pac`, 385 MB). Contenedores `.pac` propios de Yuke's.
