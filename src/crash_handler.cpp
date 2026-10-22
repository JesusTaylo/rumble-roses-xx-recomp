// Captura de crashes nativos en Windows: minidump + pila simbolizada en crash_dumps/.
//
// Las funciones recompiladas se llaman sub_XXXXXXXX en el binario, asi que los frames de la pila
// nativa ya identifican la direccion guest (PowerPC) sin necesitar el contexto del SDK.
// Directorio de salida: variable de entorno RRXX_CRASH_DIR (por defecto ./crash_dumps).

#ifdef _WIN32

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <dbghelp.h>
#include <tlhelp32.h>

#include <csignal>
#include <cstdio>
#include <cstdlib>
#include <string>

namespace {

std::string CrashDir() {
  char buf[MAX_PATH];
  DWORD n = GetEnvironmentVariableA("RRXX_CRASH_DIR", buf, sizeof(buf));
  std::string dir = (n > 0 && n < sizeof(buf)) ? std::string(buf, n) : std::string("crash_dumps");
  CreateDirectoryA(dir.c_str(), nullptr);
  return dir;
}

void WriteStack(FILE* f, HANDLE process, HANDLE thread, CONTEXT ctx) {
  STACKFRAME64 frame = {};
  frame.AddrPC.Offset = ctx.Rip;
  frame.AddrFrame.Offset = ctx.Rbp;
  frame.AddrStack.Offset = ctx.Rsp;
  frame.AddrPC.Mode = frame.AddrFrame.Mode = frame.AddrStack.Mode = AddrModeFlat;

  alignas(SYMBOL_INFO) char sym_buf[sizeof(SYMBOL_INFO) + 256];
  for (int i = 0; i < 40; ++i) {
    if (!StackWalk64(IMAGE_FILE_MACHINE_AMD64, process, thread, &frame, &ctx, nullptr,
                     SymFunctionTableAccess64, SymGetModuleBase64, nullptr)) {
      break;
    }
    if (frame.AddrPC.Offset == 0) break;
    auto* sym = reinterpret_cast<SYMBOL_INFO*>(sym_buf);
    sym->SizeOfStruct = sizeof(SYMBOL_INFO);
    sym->MaxNameLen = 255;
    DWORD64 disp = 0;
    const char* name = "?";
    if (SymFromAddr(process, frame.AddrPC.Offset, &disp, sym)) name = sym->Name;
    IMAGEHLP_LINE64 line = {};
    line.SizeOfStruct = sizeof(line);
    DWORD ldisp = 0;
    if (SymGetLineFromAddr64(process, frame.AddrPC.Offset, &ldisp, &line)) {
      fprintf(f, "  #%02d 0x%016llX %s+0x%llX (%s:%lu)\n", i, frame.AddrPC.Offset, name, disp,
              line.FileName, line.LineNumber);
    } else {
      fprintf(f, "  #%02d 0x%016llX %s+0x%llX\n", i, frame.AddrPC.Offset, name, disp);
    }
  }
}

LONG WINAPI CrashFilter(EXCEPTION_POINTERS* ep) {
  static volatile LONG entered = 0;
  if (InterlockedExchange(&entered, 1)) return EXCEPTION_CONTINUE_SEARCH;

  SYSTEMTIME st;
  GetLocalTime(&st);
  char stamp[32];
  snprintf(stamp, sizeof(stamp), "%04d%02d%02d_%02d%02d%02d", st.wYear, st.wMonth, st.wDay,
           st.wHour, st.wMinute, st.wSecond);
  std::string dir = CrashDir();
  std::string base = dir + "\\native_" + stamp;

  HANDLE process = GetCurrentProcess();
  SymSetOptions(SYMOPT_LOAD_LINES | SYMOPT_DEFERRED_LOADS | SYMOPT_UNDNAME);
  SymInitialize(process, nullptr, TRUE);

  if (FILE* f = fopen((base + ".txt").c_str(), "w")) {
    const EXCEPTION_RECORD* er = ep->ExceptionRecord;
    fprintf(f, "NATIVE CRASH code=0x%08lX address=0x%016llX flags=0x%lX\n", er->ExceptionCode,
            reinterpret_cast<unsigned long long>(er->ExceptionAddress), er->ExceptionFlags);
    if (er->ExceptionCode == EXCEPTION_ACCESS_VIOLATION && er->NumberParameters >= 2) {
      fprintf(f, "access violation: %s at 0x%016llX\n", er->ExceptionInformation[0] ? "write" : "read",
              static_cast<unsigned long long>(er->ExceptionInformation[1]));
    }
    fprintf(f, "MXCSR=0x%08lX RIP=0x%016llX RSP=0x%016llX thread=%lu\n", ep->ContextRecord->MxCsr,
            ep->ContextRecord->Rip, ep->ContextRecord->Rsp, GetCurrentThreadId());
    fprintf(f, "stack:\n");
    WriteStack(f, process, GetCurrentThread(), *ep->ContextRecord);
    fclose(f);
  }

  HANDLE file = CreateFileA((base + ".dmp").c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS,
                            FILE_ATTRIBUTE_NORMAL, nullptr);
  if (file != INVALID_HANDLE_VALUE) {
    MINIDUMP_EXCEPTION_INFORMATION mei = {GetCurrentThreadId(), ep, FALSE};
    MiniDumpWriteDump(process, GetCurrentProcessId(), file,
                      static_cast<MINIDUMP_TYPE>(MiniDumpWithDataSegs | MiniDumpWithThreadInfo),
                      &mei, nullptr, nullptr);
    CloseHandle(file);
  }
  fprintf(stderr, "\n[rrxx] NATIVE CRASH 0x%08lX; informe en %s.txt\n", ep->ExceptionRecord->ExceptionCode,
          base.c_str());
  return EXCEPTION_EXECUTE_HANDLER;
}

// Diagnostico de cuelgues: con RRXX_STACKS_AFTER=<segundos> escribe la pila de todos los hilos
// en crash_dumps/stacks_*.txt pasado ese tiempo (el juego sigue corriendo).
DWORD WINAPI StacksThread(LPVOID param) {
  Sleep(static_cast<DWORD>(reinterpret_cast<uintptr_t>(param)) * 1000);
  SYSTEMTIME st;
  GetLocalTime(&st);
  char name[64];
  snprintf(name, sizeof(name), "\\stacks_%04d%02d%02d_%02d%02d%02d.txt", st.wYear, st.wMonth,
           st.wDay, st.wHour, st.wMinute, st.wSecond);
  std::string path = CrashDir() + name;
  HANDLE process = GetCurrentProcess();
  SymSetOptions(SYMOPT_LOAD_LINES | SYMOPT_DEFERRED_LOADS | SYMOPT_UNDNAME);
  SymInitialize(process, nullptr, TRUE);
  FILE* f = fopen(path.c_str(), "w");
  if (!f) return 0;
  HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
  THREADENTRY32 te = {sizeof(te)};
  if (snap != INVALID_HANDLE_VALUE && Thread32First(snap, &te)) {
    do {
      if (te.th32OwnerProcessID != GetCurrentProcessId() || te.th32ThreadID == GetCurrentThreadId())
        continue;
      HANDLE th = OpenThread(THREAD_SUSPEND_RESUME | THREAD_GET_CONTEXT | THREAD_QUERY_INFORMATION,
                             FALSE, te.th32ThreadID);
      if (!th) continue;
      if (SuspendThread(th) != static_cast<DWORD>(-1)) {
        CONTEXT ctx = {};
        ctx.ContextFlags = CONTEXT_FULL;
        if (GetThreadContext(th, &ctx)) {
          fprintf(f, "thread %lu:\n", te.th32ThreadID);
          WriteStack(f, process, th, ctx);
        }
        ResumeThread(th);
      }
      CloseHandle(th);
    } while (Thread32Next(snap, &te));
  }
  if (snap != INVALID_HANDLE_VALUE) CloseHandle(snap);
  fclose(f);
  return 0;
}

// abort() (FatalError/assert del SDK) termina con __fastfail y no pasa por el filtro SEH: se captura
// SIGABRT para dejar la pila del hilo que aborta y volcar el log antes de morir.
void AbortHandler(int) {
  CONTEXT ctx = {};
  RtlCaptureContext(&ctx);
  HANDLE process = GetCurrentProcess();
  SymSetOptions(SYMOPT_LOAD_LINES | SYMOPT_DEFERRED_LOADS | SYMOPT_UNDNAME);
  SymInitialize(process, nullptr, TRUE);
  SYSTEMTIME st;
  GetLocalTime(&st);
  char name[64];
  snprintf(name, sizeof(name), "\\abort_%04d%02d%02d_%02d%02d%02d.txt", st.wYear, st.wMonth,
           st.wDay, st.wHour, st.wMinute, st.wSecond);
  if (FILE* f = fopen((CrashDir() + name).c_str(), "w")) {
    fprintf(f, "ABORT() thread=%lu\nstack:\n", GetCurrentThreadId());
    WriteStack(f, process, GetCurrentThread(), ctx);
    fclose(f);
  }
  fflush(stderr);
}

struct Installer {
  Installer() {
    SetUnhandledExceptionFilter(CrashFilter);
    signal(SIGABRT, AbortHandler);
    freopen((CrashDir() + "\\stderr.txt").c_str(), "w", stderr);
    char buf[16];
    DWORD n = GetEnvironmentVariableA("RRXX_STACKS_AFTER", buf, sizeof(buf));
    if (n > 0 && n < sizeof(buf)) {
      int secs = atoi(buf);
      if (secs > 0) {
        CreateThread(nullptr, 0, StacksThread,
                     reinterpret_cast<LPVOID>(static_cast<uintptr_t>(secs)), 0, nullptr);
      }
    }
  }
} g_installer;

}  // namespace

#endif  // _WIN32
