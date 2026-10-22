# Rumble Roses XX — Recompilación estática a PC

Instrucciones permanentes para Claude Code. Léelas al inicio de cada sesión, junto con `docs/PROGRESO.md`.

## Objetivo
Port nativo a PC (Windows primero, Linux/Steam Deck después) de **Rumble Roses XX (Xbox 360, Yuke's/Konami, 2006)** por recompilación estática de PowerPC a C++ con el SDK **ReXGlue** (https://github.com/rexglue/rexglue-sdk, derivado de Xenia). Referencia de estructura y flujo: port de Banjo-Tooie de iron-jay (https://github.com/iron-jay/banjo_tooie_xbla_recomp).

## Disposición de carpetas
- `rrxx-recomp/` — este repo (la compilación va a `build/`, ignorado por git).
- `../rrxx_data/` — carpeta hermana, **fuera del repo**: `extraido/` con los archivos del juego extraídos de la copia del usuario.

## Reglas legales (no negociables)
- Los datos del juego viven FUERA del repo, en `../rrxx_data/`.
- Nunca copies al repo, subas ni pegues en commits: el `.xex` (cifrado o descifrado), texturas, audio, modelos, textos del juego ni código recompilado generado del ejecutable.
- Lo generado va en `generated/` o `build/` (en `.gitignore`). El repo solo tiene herramientas originales: config, runtime, parches, launcher y docs.
- Antes de cada commit, `git status` para verificar que no entre nada de lo anterior.

## Alcance
Un jugador y multijugador local (pantalla compartida, varios mandos). Fuera de alcance: Xbox Live/online; si el juego lo exige, se parchea o simula "sin conexión".

## Etapas
Avanza en orden; no pases a la siguiente sin confirmación del usuario.
0. Diagnóstico del `default.xex` (hecha). 1. Arranque hasta el primer frame. 2. Menús con mando. 3. Primera lucha 1v1 completa. 4. Contenido completo. 5. Mejoras (resolución, framerate, QoL). 6. Linux/Steam Deck.

## Forma de trabajar
- **No adivines APIs de ReXGlue**: lee el SDK (`third_party/rexglue-sdk`) y busca en el código si no está documentado. La wiki puede estar desfasada respecto a v0.10.
- Cambios pequeños y verificables: compila y prueba después de cada uno.
- Arreglos del SDK, en nuestro fork del submódulo, con commits separados y descritos.
- Explica en español qué hiciste y por qué; el usuario es desarrollador.

## Protocolo de crash
1. Captura dirección guest (PPC), función recompilada y pila (`tools/triage.py`).
2. Clasifica: función no detectada, tabla de saltos mal resuelta, kernel sin implementar, VMX128 mal traducida, GPU/Xenos, otro.
3. Arregla con el patrón correspondiente (p. ej. añadir función/tabla al manifiesto).
4. Registra el caso en `docs/CRASHES.md`, aunque parezca trivial.

## Bitácora
Al terminar cada sesión, añade una entrada breve a `docs/PROGRESO.md`: logrado, pendiente, siguiente paso concreto.

## Ahorro de tokens
- Toda salida de compilación va a `logs/build.log`; léela solo con grep de errores (máx. ~50 líneas).
- Nunca abras archivos completos de `generated/`; busca solo la función concreta.
- Nunca leas logs de ejecución completos; usa `tools/triage.py`.
- Una tarea por sesión: al terminar, actualiza `PROGRESO.md` y sugiere abrir sesión nueva.
- Antes de tareas pesadas (compilar todo, leer muchos archivos grandes), avisa y estima el costo.
