// rrxx - ReXGlue Recompiled Project

#include "generated/default/rrxx_init.h"

#include <rex/cvar.h>

#include "rrxx_app.h"

// Saltar los videos del juego (movies/*.wmv). Desactivado por defecto (el video funciona desde el crash #3); antes: la reproduccion WMV pasa por
// XMEDIA estatico dentro del juego y es el candidato mas probable a bloquear el arranque.
// Desactivar con --skip_movies=false. Se aplica en RrxxApp::OnPreSetup (rrxx_app.h) mediante el cvar
// fail_open_suffixes del SDK, que hace fallar la apertura de *.wmv (son XMV en realidad).
REXCVAR_DEFINE_BOOL(skip_movies, false, "RRXX", "Skip game movies (movies/*.wmv)");

REX_DEFINE_APP(rrxx, RrxxApp::Create)
