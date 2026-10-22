// rrxx - ReXGlue Recompiled Project
//
// Customize your app by overriding virtual hooks from rex::ReXApp.

#pragma once

#include <rex/cvar.h>
#include <rex/filesystem/devices/host_path_device.h>
#include <rex/rex_app.h>
#include <rex/runtime.h>

#include <filesystem>

#ifdef _WIN32
#ifndef NOMINMAX
#define NOMINMAX
#endif
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#endif

REXCVAR_DECLARE(bool, skip_movies);  // definido en main.cpp

class RrxxApp : public rex::ReXApp {
 public:
  using rex::ReXApp::ReXApp;

  static std::unique_ptr<rex::ui::WindowedApp> Create(
      rex::ui::WindowedAppContext& ctx) {
    return std::unique_ptr<RrxxApp>(new RrxxApp(ctx, "rrxx",
        PPCImageConfig));
  }

  // El SDK carga la emulacion de GPU como plugin en runtime; sin esto no hay GPU (ni ventana util).
  void OnPreSetup(rex::RuntimeConfig& config) override {
    if (config.gpu_plugin.empty()) {
      config.gpu_plugin = "xenos";
    }
    // Los videos son XMV con extension .wmv; hacer que fallen al abrirse los salta (el SDK lo
    // implementa en NtCreateFile via fail_open_suffixes).
    if (REXCVAR_GET(skip_movies)) {
      rex::cvar::SetFlagByName("fail_open_suffixes", ".wmv");
    }
  }

  // El juego abre "CACHE:\\" y "PHOTO:\\" directamente (sin XamContentCreate). El SDK no monta
  // dispositivo para ellos; los montamos en carpetas del usuario (fuera del repo), como Xenia.
  void OnPostSetup() override {
    auto* vfs = runtime() ? runtime()->file_system() : nullptr;
    if (!vfs) return;
    struct Mount { const char* link; const char* device; const char* dir; };
    const Mount mounts[] = {
        {"cache:", "\Device\RrxxCache", "guest_cache"},
        {"photo:", "\Device\RrxxPhoto", "photos"},
    };
    for (const auto& m : mounts) {
      std::error_code ec;
      auto host = std::filesystem::absolute(user_data_root() / m.dir, ec);
      std::filesystem::create_directories(host, ec);
      auto dev = std::make_unique<rex::filesystem::HostPathDevice>(m.device, host, false, true);
      if (dev->Initialize() && vfs->RegisterDevice(std::move(dev))) {
        vfs->RegisterSymbolicLink(m.link, m.device);
        REXLOG_INFO("Montado {} -> {}", m.link, host.string());
      } else {
        REXLOG_WARN("No se pudo montar {} en {}", m.link, host.string());
      }
    }
  }

  // Icono de la ventana. El SDK no implementa Window::SetIcon en SDL, asi que se aplica con
  // WM_SETICON usando el icono de grupo embebido en el exe (resources/rrxx.rc, ID 1).
  void OnCreateDialogs(rex::ui::ImGuiDrawer*) override {
#ifdef _WIN32
    if (!window()) return;
    HWND hwnd = static_cast<HWND>(window()->GetNativeWindowHandle());
    HINSTANCE inst = GetModuleHandleW(nullptr);
    if (!hwnd) return;
    auto load = [&](int cx, int cy) {
      return static_cast<HICON>(LoadImageW(inst, MAKEINTRESOURCEW(1), IMAGE_ICON, cx, cy, LR_DEFAULTCOLOR));
    };
    SendMessageW(hwnd, WM_SETICON, ICON_BIG,
                 reinterpret_cast<LPARAM>(load(GetSystemMetrics(SM_CXICON), GetSystemMetrics(SM_CYICON))));
    SendMessageW(hwnd, WM_SETICON, ICON_SMALL,
                 reinterpret_cast<LPARAM>(load(GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON))));
#endif
  }

  // Override virtual hooks for customization:
  // void OnPostInitLogging() override {}
  // void OnLoadXexImage(std::string& xex_image) override {}
  // void OnPostLoadXexImage() override {}
  // std::unique_ptr<rex::ui::ImGuiDialog> CreateAchievementsOverlay() override;
  // std::unique_ptr<rex::ui::AchievementNotificationDialog>
  // CreateAchievementNotificationDialog() override;
  // void OnShutdown() override {}
  // void OnConfigurePaths(rex::PathConfig& paths) override {}
};
