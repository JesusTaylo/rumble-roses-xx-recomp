# Patches to the ReXGlue SDK

These patches apply, **in order**, on top of ReXGlue SDK commit `c94f5eb` (v0.10.0, the submodule pinned in
`third_party/rexglue-sdk`). `tools/prepare_sdk.ps1` applies them (setup.bat runs it for you); applying them by hand:

```
cd third_party/rexglue-sdk
git apply --whitespace=nowarn ../patches/0001-host-thread-fpscr-init.patch   # ... through 0008
```

They were developed while porting Rumble Roses XX on an Intel UHD iGPU under Windows 11 (D3D12 backend).
The goal is to propose the generally useful ones to the ReXGlue project. Each entry says what it fixes, how
confident we are, and whether it looks upstreamable. All of them are BSD-3-Clause like the SDK itself.

| # | Patch | Kind | Upstream candidate |
|---|-------|------|--------------------|
| 0001 | `host-thread-fpscr-init` | **Bug fix** | Yes, as is |
| 0002 | `fail-open-suffixes` | Optional feature | Maybe (small cvar) |
| 0003 | `gpu-wait-reg-mem-diagnostics` | Diagnostics | No (debug aid) / maybe behind a cvar |
| 0004 | `fps-log-tdr-tracking-fetch-warn-once` | Diagnostics + log hygiene | Partly (warn-once, FPS log) |
| 0005 | `device-lost-log-flush-dred-always` | **Robustness** | Yes |
| 0006 | `input-record-script` | Dev feature | Maybe (useful for automated tests) |
| 0007 | `dred-allocations-resource-names` | Diagnostics | Yes, behind existing debug flags |
| 0008 | `view-overrun-diagnostics-page-guard-margin` | Diagnostics + **mitigation** | Discuss first (see below) |

## 0001 — host-thread-fpscr-init  (`src/system/xthread.cpp`)
**Problem:** `XHostThread::Execute` never calls `fpscr.InitHost()` (the regular guest-thread path does). On a
host-created thread running guest code (the audio worker, in our case) the SSE control word (MXCSR) is 0, so
the first `enableFlushMode/disableFlushMode` unmasks **all** SSE exceptions and the next `fsqrt`
(host `sqrt`) raises `0xC000008F` — a hard crash at startup.
**Fix:** initialise the host FPSCR/MXCSR state at the start of `XHostThread::Execute`.
**Risk:** minimal; it makes host threads behave like guest threads.

## 0002 — fail-open-suffixes  (`src/kernel/xboxkrnl/xboxkrnl_io.cpp`)
Adds the cvar `fail_open_suffixes` (comma-separated list). `NtCreateFile` returns "not found" for paths ending in
one of those suffixes. Used by the app option `skip_movies` to skip `*.wmv` intro videos while bringing the
port up. Harmless when empty (default). Mostly a debugging convenience.

## 0003 — gpu-wait-reg-mem-diagnostics  (`src/graphics/command_processor.cpp`)
Diagnostics for a stuck `PM4 WAIT_REG_MEM`: logs the polled address/mask/value and adds the cvar
`gpu_wait_timeout_ms` (default 0 = unchanged behaviour) to abandon a wait after N ms. This was written while
chasing a black screen that turned out to be a missing-function issue in the game port, not a GPU bug, so it is
only a debug aid.

## 0004 — fps-log-tdr-tracking-fetch-warn-once  (D3D12 command processor, texture cache)
* `gpu_log_fps=N`: logs swaps-per-second every N seconds (cheap performance telemetry for bug reports).
* `gpu_track_draws`: keeps a ring buffer of the last draws (shader hashes, primitive type, tessellation flag,
  index count...) and dumps it when the device is lost. Helped to rule out tessellation as the TDR cause.
* "Invalid fetch constant" warnings (texture and vertex) are now printed **once per kind** instead of every draw
  (they flooded the log and slowed the game on affected scenes). Works together with the existing
  `gpu_allow_invalid_fetch_constants` cvar.
Upstream: the warn-once and the FPS log are reasonable as is; the tracker could be kept behind its cvar.

## 0005 — device-lost-log-flush-dred-always  (`graphics_system.cpp`, `d3d12_provider.cpp`, `presenter.cpp`)
**Problem:** when D3D12 reports `DEVICE_REMOVED`/`DEVICE_HUNG` the SDK logged a message and called `abort()`
without flushing the log, so on a TDR the most useful lines were lost; both loss-detection paths (presenter and
graphics system) behaved differently; and DRED (Device Removed Extended Data) only worked with `--d3d12_debug`.
**Fix:** flush the log on both paths, print the device-lost reason, enable DRED auto-breadcrumbs and page-fault
reporting at device creation without needing the debug layer, and leave a stack dump + stderr on `abort()`.
**Risk:** DRED has a small runtime cost; it can be put behind a cvar if upstream prefers.

## 0006 — input-record-script  (`src/input/input_system.cpp`, `input_system.h`)
Adds `--input_record=<file>` (records the real controller as `time_ms BUTTON down|up` lines) and
`--input_script=<file>` (replays such a file as a virtual pad on slot 0). It made reproducible automated runs
(menu navigation without a human) possible. Optional; off by default.

## 0007 — dred-allocations-resource-names  (D3D12 backend, `d3d12_util.*`)
Diagnostics: `SetName` on large D3D12 resources (shared memory, EDRAM, index/upload pools, scratch, textures),
a log line with name/GPU virtual address/size at creation (`util::NameAndLog`), and DRED
`PAGE_FAULT_OUTPUT1` parsing that prints the EXISTING and RECENTLY FREED allocations covering the faulting
address. This is what made it possible to see that the fault address was always the end of a
`constant_page`. Valuable for anyone chasing GPU page faults; could be upstreamed behind a debug flag.

## 0008 — view-overrun-diagnostics-page-guard-margin  (D3D12 backend)
**Symptom:** on Intel UHD (also reproducible at idle after ~3 minutes) a `DrawIndexedInstanced` page-faults
and the driver resets the device (TDR). DRED showed the faulting VA was exactly the **end of the most
recently created `constant_page`** of the constant-buffer upload pool.
**Diagnostics:** named pools, `ReportViewOverrun` (one-time warnings when a CBV/SRV/index view or a
descriptor-index allocation would extend past its allocation), plus checks in the primitive processor and
command processor.
**Mitigation:** new cvar `d3d12_upload_page_guard_margin` (default `true`): every page of the upload pools
keeps an unused **64 KB** margin at its end. Result on the test machine: 6/10 TDRs in a 180 s idle test without
the margin, 0/15 with it, and 0 crashes in later hands-on play (two matches plus several visits to the
customisation menu).
**What is NOT known:** why the GPU/driver reads past the declared constant-buffer view. The CPU-side
bookkeeping found no view exceeding its allocation except `descriptor_indices` (the shader declares 16-byte
blocks, the allocation is `n*4` bytes, so ≤ 12 bytes beyond; always inside the page). So this is a
**workaround, not a root-cause fix**. Before proposing it upstream it should be tested on other GPUs
(NVIDIA/AMD) to find out whether it is Intel-specific; if so, enabling it only for Intel (or padding the
allocations to 16 bytes) may be preferable to a global 64 KB-per-page cost.

---
*Note: the patches in this folder were regenerated as one clean, LF-ended series that applies to a pristine
`c94f5eb`; the final result is byte-identical to the SDK tree the port was tested with.*
