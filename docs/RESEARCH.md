# Ally Tuner hardware research

**Status (2026-11-15):** research done. **Nothing is confirmed on Bradley's Ally yet.** Every ASUS interface below stays *unconfirmed* until a Probe report from Bradley's ROG Xbox Ally X (RC73XA) matches it. Phase 1 code only **reads**.

This document is the source of truth for every hardware ID, method, buffer layout and safe range Ally Tuner uses. Code comments link here and to the same pinned sources. No ACPI method ID, device ID or register is guessed: each one is cited, and anything not cited is not used.

## Contents

1. [Confidence levels](#1-confidence-levels)
2. [Sources](#2-sources)
3. [Device identity (RC73XA)](#3-device-identity-rc73xa)
4. [The ASUS ATKACPI interface](#4-the-asus-atkacpi-interface)
5. [Endpoint table](#5-endpoint-table)
6. [Findings that shape Phase 4 (writes)](#6-findings-that-shape-phase-4-writes)
7. [Xbox Full Screen Experience](#7-xbox-full-screen-experience)
8. [AMD graphics (Phase 5 preview)](#8-amd-graphics-phase-5-preview)
9. [Windows power, graphics memory and software](#9-windows-power-graphics-memory-and-software)
10. [What the Probe does and never does](#10-what-the-probe-does-and-never-does)
11. [Questions Bradley's Probe report answers](#11-questions-bradleys-probe-report-answers)
12. [Safety rules derived from this research](#12-safety-rules-derived-from-this-research)

---

## 1. Confidence levels

Each interface climbs this ladder. Phase 4 writes only to interfaces on the top rung.

| Level | Meaning |
|---|---|
| **Single source** | One open-source implementation uses it. |
| **Cross-checked** | Two or more independent projects (for example G-Helper and the Linux kernel) use the same ID for the same thing. |
| **Public RC73XA evidence** | Another owner's ROG Xbox Ally X returned a sensible value for it (the G-Helper issue #4843 scans, §2). Useful for planning; **not** confirmation. |
| **Confirmed on Bradley's Ally** | Bradley's Probe report matches. Recorded in code as `DeviceConfirmation.ConfirmedByProbe`, in a PR that quotes the report. **No endpoint is at this level yet.** |

## 2. Sources

All links are pinned to a commit, so line numbers stay valid.

| Source | What it gives | Pinned at | Licence |
|---|---|---|---|
| **G-Helper** (seerge/g-helper), the main open-source Armoury Crate alternative, with explicit ROG Ally / Xbox Ally support | ATKACPI protocol, device IDs, VRAM and core layouts, Ally power limits, ASUS service names | [`9d1b2a5`][gh] (2026-11-15) | GPL-3.0 |
| **Linux kernel** `asus-wmi` and `asus-armoury` drivers | Independent definitions of the same device IDs, DSTS decoding, **per-model power-limit ranges including RC73XA** | [`fa2933a`][lx] (2026-11-15) | GPL-2.0 |
| **Handheld Companion** (Valkirie/HandheldCompanion) | A third implementation: same ATKACPI IDs, device detection by board name, Armoury service names | [`ed6bc51`][hc] (2026-11-07) | CC BY-NC-SA 4.0 |
| **Public RC73XA scan**, G-Helper issue #4843 | A ROG Xbox Ally X owner's full DSTS endpoint scans (2026-08-17 and 08-18), and a log of G-Helper writing to it | [comment 1][scan1], [comment 2 + scan.txt][scan2] | n/a |
| **Microsoft Learn / Windows SDK headers** | Full Screen Experience API, power settings, DXGI, WMI classes | links inline | n/a |
| **AMD ADLX SDK** | Radeon feature control (Phase 5) | [`32b5a74`][adlx] (ADLX 2.0.0.125) | AMD ADLX SDK License Agreement (PDF in the repo) |

Ally Tuner **copies no code** from GPL or CC-licensed projects. It reimplements from interface facts (numbers, layouts, ranges), and each fact is cited.

## 3. Device identity (RC73XA)

| Evidence | Source |
|---|---|
| Linux matches the Xbox Ally X on **`DMI_BOARD_NAME` = `RC73XA`** (Windows: `Win32_BaseBoard.Product`) | [asus-armoury.h L2858-L2884][lx-armoury-rc73xa] |
| Handheld Companion switches on baseboard manufacturer **`ASUSTEK COMPUTER INC.`** and product **`RC73XA`** ("ROG XBOX Ally X"); `RC73YA` = ROG Xbox Ally; `RC74XA` = "ROG XBOX Ally X20 (2026)"; `RC72LA` = ROG Ally X (2024) | [IDevice.cs L1095-L1115][hc-device] |
| Linux matches the sibling Xbox Ally on board vendor `ASUSTeK COMPUTER INC.` + board name `RC73YA` | [linux commit c0ddc54][lx-pmc] |
| G-Helper treats `Win32_ComputerSystem.Model` containing `RC7` as an Ally and `RC73` as an Xbox Ally | [AppConfig.cs L377-L385][gh-config-ally] |
| HD-audio subsystem ID 0x1043:0x1384 is labelled "ASUS RC73XA" | [linux commit 18a4895][lx-realtek] |

**What Ally Tuner does:** `DeviceClassifier` (Phase 0) requires *both* an ASUS manufacturer *and* the whole token `RC73XA` in Model, BaseBoardProduct or SKU. That fits all the evidence above. Bradley's Phase 0 diagnostics report and the Probe report show the exact strings; if they differ, the classifier changes in a reviewed PR, never by loosening it blindly.

## 4. The ASUS ATKACPI interface

Armoury Crate, G-Helper and Handheld Companion all talk to the BIOS through the ASUS **ATKACPI** driver:

| Fact | Value | Sources |
|---|---|---|
| Device path | `\\.\ATKACPI` | [G-Helper L32][gh-acpi-32], [HC L32][hc-acpi-32] |
| IOCTL | `0x0022240C`: device type 0x22 (FILE_DEVICE_UNKNOWN), function 0x903, METHOD_BUFFERED, **FILE_ANY_ACCESS** (a read-only handle is enough) | [G-Helper L33][gh-acpi-32], [HC L33][hc-acpi-32] |
| Request | `[method u32 LE][argument byte count u32 LE][arguments]` | [G-Helper CallMethod L411-L426][gh-acpi-call] |
| **DSTS** (get device status, **read**) | `0x53545344` ("DSTS") | [G-Helper L35][gh-acpi-32], [Linux asus-wmi.h L25][lx-h-methods], [HC L35][hc-acpi-32] |
| DEVS (set device, **write**) | `0x53564544`. **Not present anywhere in Ally Tuner's Phase 1 code**, and a test fails if it appears. | [Linux asus-wmi.h L27][lx-h-methods] |
| Unsupported result | `0xFFFFFFFE` | [Linux asus-wmi.h L34][lx-h-methods] |
| Presence bit | `0x00010000` (bit 16): the BIOS implements this ID; the value is the result with this bit cleared | [Linux asus-wmi.h L175][lx-h-presence], [asus-armoury.c L141-L187][lx-armoury-get], [G-Helper DeviceGet L491-L499][gh-acpi-get] |
| Request shapes | *Standard*: 8 argument bytes `[device ID][argument]` ([DeviceGet/DeviceGetBuffer L491-L508][gh-acpi-getbuf]); *Extended*: 12 argument bytes `[device ID][0][0]` ([DeviceGetLarge L919-L930][gh-acpi-large]) | G-Helper |
| Reply buffer | 64 bytes. G-Helper had to raise it from 40 to 64 before the RC73XA's VRAM option list (42 bytes) came back | [G-Helper commit 2d9cafb][gh-2d9cafb] |
| Same BIOS methods over WMI | class `AsusAtkWmi_WMNB` in `root\wmi` (G-Helper uses its DEVS as a fallback). The Probe lists this class's method signatures but **invokes nothing**. | [G-Helper L474-L489][gh-acpi-wmi] |

## 5. Endpoint table

The same list is in code: `src/AllyTuner.Core/Asus/AsusEndpointCatalog.cs` (a unit test keeps this table and the code in sync). "Public scan" is what another owner's RC73XA returned (§2), **not** Bradley's device. Values are shown after clearing the presence bit.

| ID | What | Sources | Agreement | Public RC73XA scan | Planned use |
|---|---|---|---|---|---|
| `0x00120075` | Performance mode (thermal policy) | [GH L72][gh-acpi-72], [LX L76][lx-h-76], [HC L72][hc-acpi-72] | cross-checked | Present; reads **7** every time, even while G-Helper had set mode 2 → **read is not the current mode** | Phase 4 mode switch |
| `0x001200A3` | SPL, sustained limit (PL1) | [GH L115][gh-acpi-ppt], [LX L109][lx-h-ppt], [HC L60][hc-acpi-ppt] | cross-checked | Present, reads **0** while G-Helper applied 13 W → **write-only** | Phase 4 power limits |
| `0x001200A0` | sPPT, slow boost (PL2) | [GH L112][gh-acpi-ppt], [LX L108][lx-h-ppt], [HC L65][hc-acpi-ppt] | cross-checked | Present, reads 0 → write-only | Phase 4 |
| `0x001200C1` | fPPT, fast boost (PL3) | [GH L126][gh-acpi-ppt], [LX L112][lx-h-ppt], [HC L70][hc-acpi-ppt] | cross-checked | Present, reads 0 → write-only | Phase 4 |
| `0x001200B0` | APU sPPT (2022 models) | [GH L117][gh-acpi-ppt], [LX L110][lx-h-ppt] | cross-checked | Present, reads 0 | context only |
| `0x001200B1` | Platform sPPT (2022 models) | [GH L118][gh-acpi-ppt], [LX L111][lx-h-ppt] | cross-checked | Present, reads 0 | context only |
| `0x00120094` | CPU temperature (°C) | [GH L109][gh-acpi-temps], [GH HardwareControl L458][gh-hw-458] | single source | Present; 48–51 °C | status, overlay |
| `0x00120097` | GPU temperature (G-Helper) / "DGPU_POWER_STATE" (Linux) | [GH L110][gh-acpi-temps], [LX L147][lx-h-147] | **sources disagree** | Present; 43–47 °C (looks like a temperature) | display only |
| `0x00110013` | CPU fan speed (×100 rpm) | [GH L63][gh-acpi-fans], [GH ×100 L906-L907][gh-hw-906], [LX L100][lx-h-fans], [HC L53][hc-acpi-fans] | cross-checked | Present; 35–38 (≈3,500–3,800 rpm) | status |
| `0x00110014` | GPU fan speed (×100 rpm) | [GH L64][gh-acpi-fans], [LX L101][lx-h-fans], [HC L54][hc-acpi-fans] | cross-checked | Present; 34–38 | status |
| `0x00110031` | Mid fan speed | [GH L65][gh-acpi-fans], [LX L102][lx-h-fans], [HC L55][hc-acpi-fans] | cross-checked | absent | only if present |
| `0x00110022` | CPU fan range | [GH L97][gh-acpi-devsfan], [HC L44][hc-acpi-devsfan] | cross-checked | Present; 0x9A | Phase 4 context |
| `0x00110023` | GPU fan range | [GH L98][gh-acpi-devsfan], [HC L45][hc-acpi-devsfan] | cross-checked | Present; 0x8D | Phase 4 context |
| `0x00110024` | CPU fan curve (per mode) | [GH L100, L635-L666][gh-acpi-curve], [LX L103][lx-h-curves], [LX asus-wmi.c L3604-L3647][lx-wmi-curve], [HC L47][hc-acpi-curves] | cross-checked | mode 0: 48,54,59,62,62,62,62,62 °C → 1,9,18,22,22,22,22,22 % | Phase 4 fan curves |
| `0x00110025` | GPU fan curve (per mode) | [GH L101][gh-acpi-curve], [LX L104][lx-h-curves], [HC L48][hc-acpi-curves] | cross-checked | mode 0: same temps → 1,9,13,13,13,13,13,13 % | Phase 4 |
| `0x00110032` | Mid fan curve (per mode) | [GH L102][gh-acpi-curve], [LX L105][lx-h-curves], [HC L49][hc-acpi-curves] | cross-checked | absent | only if present |
| `0x00110026` | CPU fan floor | [GH L104, L668-L676][gh-acpi-floor] | single source | absent | context |
| `0x00110027` | GPU fan floor | [GH L105][gh-acpi-floor] | single source | absent | context |
| `0x00120057` | Battery charge limit | [GH L85][gh-acpi-85], [LX L122-L123][lx-h-rsoc], [HC L51][hc-acpi-51] | cross-checked | Present; reads 0x01880000 even after G-Helper set **80 % (accepted: "BatteryLimit = 80 : OK")** → **write-only** | Phase 4 Battery Care |
| `0x0012006C` | Charger type (1 barrel, 2 USB-C) | [GH L58-L61][gh-acpi-charger], [LX L128-L129][lx-h-charge] | cross-checked | Present; 2 = USB-C | status |
| `0x000600C4` | **VRAM allocation with option list** | [GH L137, L830-L857][gh-acpi-vram], [scan with VRAM][scan2] | single source (+ public RC73XA success) | Present; **Auto, 512 MB, 1–16 GB in 1 GB steps; current 4 GB**. The owner switched 4 GB ↔ 16 GB with G-Helper successfully | Phase 4 VRAM (8/10/12 GB) |
| `0x000600C1` | APU memory (original Ally) | [GH L136, L813-L828][gh-acpi-apumem], [LX L145][lx-h-145], [LX asus-armoury.c L720-L760][lx-armoury-apumem] | cross-checked | absent | fallback only |
| `0x001200D2` | **CPU core layout**, current (`0x0[E]0[P]`) | [GH L129, L859-L878][gh-acpi-cores] | single source | Present; **0x0503 = 3 P-cores + 5 E-cores** (matches Ryzen Z2 Extreme: 3 Zen 5 + 5 Zen 5c) | Phase 4 core config |
| `0x001200D3` | CPU core layout, maximum | [GH L130][gh-acpi-cores] | single source | Present; 0x0503 | Phase 4 bound |
| `0x001200D4` | CPU core layout, minimum | [GH L131][gh-acpi-cores], [GH Extra.cs L606-L611][gh-extra-cores] | single source | Present; 0x0001 = 1 P + 0 E | Phase 4 bound |
| `0x001200E2` | MCU power-save (Ally controller MCU) | [LX L131-L132][lx-h-mcu] | single source | Present; 0, later 1 | context only |

## 6. Findings that shape Phase 4 (writes)

These are hypotheses for Phase 4. Each one is tested in dry-run on Bradley's Ally before anything is applied.

### 6.1 Power limits (SPL / sPPT / fPPT)

- **Safe ranges for RC73XA**, from Linux `asus-armoury` ([L2858-L2884][lx-armoury-rc73xa]):

  | Limit | AC (plugged in) | Battery (min / default / max) |
  |---|---|---|
  | SPL (sustained) | **7–35 W** (default = max) | 7 / 17 / 35 W |
  | sPPT (slow boost) | **14–45 W** | 13 / 21 / 45 W |
  | fPPT (fast boost) | **19–55 W** | 19 / 26 / 55 W |

- G-Helper allows the Ally up to 50 W "total" (default 30 W) ([AsusACPI.cs L333-L337][gh-acpi-ally]). Linux's limits are tighter for sustained power, so **Ally Tuner clamps to the Linux ranges**, the more conservative of the two. The brief's recommended 30–35 W sustained fits inside SPL ≤ 35 W.
- G-Helper writes SPL (A3), then sPPT (A0), then fPPT (C1) on AMD models ([ModeControl.cs L349-L405][gh-mode-setpower]). The public RC73XA log shows exactly this sequence accepted at 13 W, after setting mode 2.
- **Reads return 0, so the current limits cannot be read back.** Ally Tuner must record what it applied and treat "unknown" honestly. A way to verify a limit took effect (for example, sustained package power under load) is an open question for Phase 4.
- **Not used: direct SMU writes** (ryzenadj-style). G-Helper falls back to them when ACPI power limits are missing ([ModeControl.cs L327-L347][gh-mode-ryzen]), which needs a kernel driver to touch the CPU directly. The RC73XA has the ACPI endpoints, so Ally Tuner never needs that path.

### 6.2 Performance mode

- Values written by G-Helper and Linux: 0 = Balanced (Armoury Crate "Performance"), 1 = Turbo, 2 = Silent ([GH L169-L173][gh-acpi-modes], [Linux asus-wmi.c L98-L100][lx-wmi-modes]). G-Helper's 3 and 4 are its own extensions.
- The public RC73XA scan reads 7 whatever the mode. It is probably a capability mask, but that is unconfirmed, so **the mode is write-only** too.
- G-Helper resets to a base mode when custom fans fail ([ModeControl.cs L261][gh-mode-reset]). Whether re-applying a base mode restores the firmware's own power limits is a **hypothesis to test in Phase 4 dry-run**. It is the likely core of "Reset to Safe Defaults".

### 6.3 Fan curves

- Reply layout: 8 temperatures (°C) followed by 8 duty percentages ([G-Helper L605-L633][gh-acpi-setcurve], [Linux L3604-L3616][lx-wmi-curve]).
- The read argument is the mode, **with 1 and 2 swapped**: 0 = Balanced, 1 = Silent, 2 = Turbo ([G-Helper L635-L666][gh-acpi-curve], [Linux L3618-L3647][lx-wmi-curve]). The Probe reads all three modes for every fan-curve ID (CPU, GPU and mid); the public scan has only mode 0.
- Phase 4 rule: a custom curve may never be lower than the factory curve for the same mode at any temperature, temperatures must rise, and duty stays within 0–100 %.

### 6.4 Battery Care (charge limit)

- `0x00120057`. The public log shows **DEVS 80 accepted** on an RC73XA. Reading it returns 0x01880000, not the limit. Linux also does not read it back.
- Verification plan: after applying it, watch the charge level stop near 80 % (`GetSystemPowerStatus`).

### 6.5 VRAM

- **RC73XA uses `0x000600C4`** (new layout with an option list), not the original Ally's `0x000600C1`, which is absent there.
- Layout, from G-Helper `GetVramOptions` ([L830-L852][gh-acpi-vram]): status word (bit 16 present, bit 19 error, bit 17 = 512 MB units, low 16 bits = option count), current setting at bytes 4–7, options as u16 from byte 6 (option 0 = Auto).
- The public RC73XA offers **Auto, 512 MB, 1–16 GB** in 1 GB steps, so the brief's **8, 10 and 12 GB are offered**. A restart is needed.
- Only G-Helper implements this endpoint (Linux does not yet), so **Phase 4 only ever writes a value taken from the device's own option list**, never an arbitrary number.
- For the old endpoint, Linux refuses to write 0x100 or 0x101 because they can leave a device needing a hard reset ([asus-armoury.c L239-L247][lx-armoury-unsafe]). Ally Tuner will never write those values.
- Cross-check: DXGI "dedicated video memory" (§9) should match the current setting after a restart.

### 6.6 CPU core configuration (P-cores / E-cores)

- `0x001200D2` (current), `0x001200D3` (max), `0x001200D4` (min), format `0x0[E]0[P]` ([G-Helper L129-L131, L859-L878][gh-acpi-cores]). G-Helper's comments say "Intel", but the public RC73XA scan shows the endpoints present, with 3 P + 5 E, which matches the Ryzen Z2 Extreme.
- G-Helper restarts after setting it. Phase 4: clamp to the device's own min/max, confirm, and offer the restart.
- This is single source, so it is the last control enabled and gets its own dry-run.

### 6.7 CPU Boost

No ASUS endpoint in any source. CPU Boost is the documented Windows power setting **PERFBOOSTMODE** (`be337238-0d82-4146-a960-4f3749d470c7` in processor sub-group `54533251-82be-4824-96c1-47b60b740d00`; values 0 disabled, 1 enabled, 2 aggressive, 3–4 efficient variants) ([Microsoft][ms-boost]). The Probe reads the AC and battery values; Phase 4 writes it through documented power APIs.

### 6.8 Armoury Crate coexistence

Armoury Crate can override settings. The service and process names come from G-Helper ([AsusService.cs L10-L55][gh-services]) and Handheld Companion ([RogAllySpaceWatcher.cs L10][hc-services]). Phase 1 only **detects** them and shows the result on Home. Any "suppress" option comes later, is confirmed and reversible, and is never automatic. The Armoury Crate **SE app's own process name** on the RC73XA is not in either source; the Probe lists ASUS-looking process names so we learn it.

## 7. Xbox Full Screen Experience

- Microsoft documents a supported API ([GDK: Game launchers and handhelds][ms-fse]): gate on `IsApiSetImplemented("api-ms-win-gaming-experience-l1-1-0")` ([Microsoft][ms-apiset]), then call `IsGamingFullScreenExperienceActive()`. It also documents change notifications (`RegisterGamingFullScreenExperienceChangeNotification`).
- Exact signatures, from Microsoft's Windows SDK header as recompiled in win32metadata ([gamingexperience.h][ms-fse-h]): `BOOL WINAPI IsGamingFullScreenExperienceSupported(void)`, `BOOL WINAPI IsGamingFullScreenExperienceActive(void)`. The header also has `SetGamingFullScreenExperience`; **Ally Tuner never calls it**, because the brief says not to force the user out, and a test fails if it is ever imported.
- Ally Tuner resolves these functions at run time, as Microsoft recommends for optional API sets, so the app still starts on Windows builds without them.
- In the Full Screen Experience, Windows has exclusive use of the Guide button, and View+Menu sends a Guide press to the app ([GDK][ms-fse]). Ally Tuner uses neither button.
- The Probe also reads the community-reported `HKLM\…\OEM\DeviceForm` value, for information only. It is not a Microsoft API and is not used for decisions.

## 8. AMD graphics (Phase 5 preview)

- **ADLX** is AMD's supported API. Its runtime `amdadlx64.dll` ships with the Adrenalin driver ([ADLX.h L25][adlx-dll]). The Probe only checks that the file exists and reads its version; it does not load it.
- Available in ADLX 2.0 ([headers][adlx]): Anti-Lag and Anti-Lag Next (`IADLX3DAntiLag`, `IADLX3DAntiLag1`), Radeon Super Resolution (global, via `IADLX3DSettingsServices1`), Radeon Boost, Radeon Chill, AMD Fluid Motion Frames (`IADLX3DSettingsServices2`), FSR (`IADLX3DSettingsServices3`).
- **HYPR-RX has no ADLX interface** (none of the ADLX 2.0 headers mention it). The brief says "where controllable"; the honest options are (a) apply its parts one by one (Anti-Lag, RSR, Boost), clearly labelled as *not* HYPR-RX, or (b) guide Bradley to the toggle in AMD Software. Decision for Phase 5.
- Documented conflicts to respect: Anti-Lag cannot run with Chill; RSR needs GPU scaling; on some GPUs RSR cannot run with Boost or Image Sharpening ([I3DSettings.h][adlx-3d]).
- Auto SR is a Microsoft feature; researched in Phase 5.

## 9. Windows power, graphics memory and software

All read-only in Phase 1, using documented APIs:

| What | API | Docs |
|---|---|---|
| AC or battery, battery % | `GetSystemPowerStatus` (ACLineStatus 0/1/255; BatteryLifePercent 255 = unknown) | [Microsoft][ms-sps] |
| Active power plan and name | `PowerGetActiveScheme`, `PowerReadFriendlyName` | [Microsoft][ms-scheme] |
| CPU Boost AC / battery | `PowerReadACValueIndex` / `PowerReadDCValueIndex` with PERFBOOSTMODE | [Microsoft][ms-acvalue] |
| Windows power mode, Game Mode | `PowerRegisterForEffectivePowerModeNotifications` (Windows calls back once, straight away, with the current mode; V2 includes Game Mode) | [Microsoft][ms-epm] |
| GPU memory as Windows sees it | DXGI `IDXGIAdapter1::GetDesc1` → `DedicatedVideoMemory` (what Task Manager shows as dedicated GPU memory). Vtable slots checked against the SDK's dxgi.h. | [Microsoft][ms-dxgi] |
| GPU driver version | `Win32_VideoController` (PnP ID trimmed to vendor/device/subsystem/revision) | [Microsoft][ms-vc] |
| ASUS / AMD / Xbox services | `Win32_Service` (name, state, start mode) | [Microsoft][ms-svc] |

## 10. What the Probe does and never does

**Does (read-only):** reads device identity; opens ATKACPI (read-only handle first, G-Helper's read/write *handle* flags only if refused; opening a handle changes nothing); sends **DSTS reads** for the 26 catalogued IDs above, and every fan curve (CPU, GPU and mid) for 3 modes each; lists the method signatures of ASUS WMI classes in `root\wmi` (definitions only); lists ASUS/AMD/Xbox service and process **names**; reads DXGI adapters, the video driver, ADLX file presence, power state and plan, and the Full Screen Experience state. It then saves `Documents\Ally Tuner\probe-<date>-<time>.txt`.

**Never:** sends DEVS/INIT, invokes a WMI method, writes the registry or a power setting, stops a service, or sends ASUS calls on a machine whose manufacturer is not ASUS. Reports contain no user name, PC name, serial number or file path.

`ReadOnlyGuardTests` scans all of `src/` and fails the build if a write path appears: the DEVS/INIT/WDOG IDs, `SetGamingFullScreenExperience`, power-plan writes, registry writes, or WMI method invocation.

## 11. Questions Bradley's Probe report answers

1. Do `Model` and `BaseBoardProduct` contain `RC73XA`? (Settles detection.)
2. Does ATKACPI open with a read-only handle?
3. Do presence and values match the public RC73XA scan for all 26 IDs?
4. VRAM: the same option list? Is the current value consistent with DXGI dedicated memory and with Armoury Crate's setting?
5. Fan curves for modes 1 (Silent) and 2 (Turbo), which the public scan lacks.
6. What are Armoury Crate SE's process and service names on the RC73XA, and are they running?
7. Does `IsGamingFullScreenExperienceActive` read *yes* inside the Full Screen Experience and *no* on the desktop? (Run the Probe once in each.)
8. Baseline: power plan, CPU Boost value, Windows power mode.
9. The `AsusAtkWmi_WMNB` method signatures, in case a WMI path is ever needed.

After the report: matching endpoints move to `ConfirmedByProbe` in a PR that quotes the report. Anything that does not match is investigated before Phase 4.

## 12. Safety rules derived from this research

Phase 2 (the safety framework) and Phase 4 (ASUS writes) build on these:

1. Write only to endpoints marked `ConfirmedByProbe`.
2. Clamp power limits to the Linux RC73XA ranges (§6.1), separately for AC and battery.
3. VRAM: only values from the device's own option list. Never 0x100 or 0x101 on `0x000600C1`.
4. Cores: only within the device's reported min and max.
5. Fan curves: never below the factory curve, temperatures rising, duty 0–100 %.
6. Many settings cannot be read back (power limits, mode, charge limit). The app records what it applied, and "Reset to Safe Defaults" returns to firmware defaults through a mode that is confirmed in dry-run.
7. Every first write of each control ships in dry-run first, and Bradley watches it on the Ally.

[gh]: https://github.com/seerge/g-helper/tree/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec
[gh-acpi-32]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/AsusACPI.cs#L32-L37
[gh-acpi-call]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/AsusACPI.cs#L411-L426
[gh-acpi-get]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/AsusACPI.cs#L491-L499
[gh-acpi-getbuf]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/AsusACPI.cs#L491-L508
[gh-acpi-large]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/AsusACPI.cs#L919-L930
[gh-acpi-wmi]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/AsusACPI.cs#L474-L489
[gh-acpi-72]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/AsusACPI.cs#L72
[gh-acpi-modes]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/AsusACPI.cs#L169-L173
[gh-acpi-ppt]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/AsusACPI.cs#L112-L127
[gh-acpi-temps]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/AsusACPI.cs#L109-L110
[gh-acpi-fans]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/AsusACPI.cs#L63-L65
[gh-acpi-devsfan]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/AsusACPI.cs#L97-L98
[gh-acpi-curve]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/AsusACPI.cs#L635-L666
[gh-acpi-setcurve]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/AsusACPI.cs#L605-L633
[gh-acpi-floor]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/AsusACPI.cs#L668-L676
[gh-acpi-85]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/AsusACPI.cs#L85
[gh-acpi-charger]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/AsusACPI.cs#L58-L61
[gh-acpi-vram]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/AsusACPI.cs#L830-L857
[gh-acpi-apumem]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/AsusACPI.cs#L813-L828
[gh-acpi-cores]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/AsusACPI.cs#L859-L878
[gh-acpi-ally]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/AsusACPI.cs#L333-L337
[gh-extra-cores]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/Extra.cs#L606-L611
[gh-hw-458]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/HardwareControl.cs#L458
[gh-hw-906]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/HardwareControl.cs#L906-L907
[gh-config-ally]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/AppConfig.cs#L377-L385
[gh-services]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/Helpers/AsusService.cs#L10-L55
[gh-mode-setpower]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/Mode/ModeControl.cs#L349-L405
[gh-mode-ryzen]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/Mode/ModeControl.cs#L327-L347
[gh-mode-reset]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/Mode/ModeControl.cs#L261
[gh-2d9cafb]: https://github.com/seerge/g-helper/commit/2d9cafb2d
[scan1]: https://github.com/seerge/g-helper/issues/4843#issuecomment-5311016414
[scan2]: https://github.com/seerge/g-helper/issues/4843#issuecomment-5317710002
[lx]: https://github.com/torvalds/linux/tree/fa2933aac170dd19c6a7f0d7b81a8f537e40915c
[lx-h-methods]: https://github.com/torvalds/linux/blob/fa2933aac170dd19c6a7f0d7b81a8f537e40915c/include/linux/platform_data/x86/asus-wmi.h#L25-L34
[lx-h-presence]: https://github.com/torvalds/linux/blob/fa2933aac170dd19c6a7f0d7b81a8f537e40915c/include/linux/platform_data/x86/asus-wmi.h#L175
[lx-h-76]: https://github.com/torvalds/linux/blob/fa2933aac170dd19c6a7f0d7b81a8f537e40915c/include/linux/platform_data/x86/asus-wmi.h#L76
[lx-h-fans]: https://github.com/torvalds/linux/blob/fa2933aac170dd19c6a7f0d7b81a8f537e40915c/include/linux/platform_data/x86/asus-wmi.h#L100-L102
[lx-h-curves]: https://github.com/torvalds/linux/blob/fa2933aac170dd19c6a7f0d7b81a8f537e40915c/include/linux/platform_data/x86/asus-wmi.h#L103-L105
[lx-h-ppt]: https://github.com/torvalds/linux/blob/fa2933aac170dd19c6a7f0d7b81a8f537e40915c/include/linux/platform_data/x86/asus-wmi.h#L107-L112
[lx-h-rsoc]: https://github.com/torvalds/linux/blob/fa2933aac170dd19c6a7f0d7b81a8f537e40915c/include/linux/platform_data/x86/asus-wmi.h#L122-L123
[lx-h-charge]: https://github.com/torvalds/linux/blob/fa2933aac170dd19c6a7f0d7b81a8f537e40915c/include/linux/platform_data/x86/asus-wmi.h#L128-L129
[lx-h-mcu]: https://github.com/torvalds/linux/blob/fa2933aac170dd19c6a7f0d7b81a8f537e40915c/include/linux/platform_data/x86/asus-wmi.h#L131-L132
[lx-h-145]: https://github.com/torvalds/linux/blob/fa2933aac170dd19c6a7f0d7b81a8f537e40915c/include/linux/platform_data/x86/asus-wmi.h#L145
[lx-h-147]: https://github.com/torvalds/linux/blob/fa2933aac170dd19c6a7f0d7b81a8f537e40915c/include/linux/platform_data/x86/asus-wmi.h#L147
[lx-wmi-modes]: https://github.com/torvalds/linux/blob/fa2933aac170dd19c6a7f0d7b81a8f537e40915c/drivers/platform/x86/asus-wmi.c#L98-L100
[lx-wmi-curve]: https://github.com/torvalds/linux/blob/fa2933aac170dd19c6a7f0d7b81a8f537e40915c/drivers/platform/x86/asus-wmi.c#L3604-L3647
[lx-armoury-rc73xa]: https://github.com/torvalds/linux/blob/fa2933aac170dd19c6a7f0d7b81a8f537e40915c/drivers/platform/x86/asus-armoury.h#L2858-L2884
[lx-armoury-get]: https://github.com/torvalds/linux/blob/fa2933aac170dd19c6a7f0d7b81a8f537e40915c/drivers/platform/x86/asus-armoury.c#L141-L187
[lx-armoury-unsafe]: https://github.com/torvalds/linux/blob/fa2933aac170dd19c6a7f0d7b81a8f537e40915c/drivers/platform/x86/asus-armoury.c#L239-L247
[lx-armoury-apumem]: https://github.com/torvalds/linux/blob/fa2933aac170dd19c6a7f0d7b81a8f537e40915c/drivers/platform/x86/asus-armoury.c#L720-L760
[lx-pmc]: https://github.com/torvalds/linux/commit/c0ddc5401663
[lx-realtek]: https://github.com/torvalds/linux/commit/18a4895370a7
[hc]: https://github.com/Valkirie/HandheldCompanion/tree/ed6bc5153f6f316f9545ec176e739e61e359244b
[hc-acpi-32]: https://github.com/Valkirie/HandheldCompanion/blob/ed6bc5153f6f316f9545ec176e739e61e359244b/HandheldCompanion/Devices/ASUS/AsusACPI.cs#L32-L36
[hc-acpi-72]: https://github.com/Valkirie/HandheldCompanion/blob/ed6bc5153f6f316f9545ec176e739e61e359244b/HandheldCompanion/Devices/ASUS/AsusACPI.cs#L72
[hc-acpi-ppt]: https://github.com/Valkirie/HandheldCompanion/blob/ed6bc5153f6f316f9545ec176e739e61e359244b/HandheldCompanion/Devices/ASUS/AsusACPI.cs#L57-L70
[hc-acpi-fans]: https://github.com/Valkirie/HandheldCompanion/blob/ed6bc5153f6f316f9545ec176e739e61e359244b/HandheldCompanion/Devices/ASUS/AsusACPI.cs#L53-L55
[hc-acpi-devsfan]: https://github.com/Valkirie/HandheldCompanion/blob/ed6bc5153f6f316f9545ec176e739e61e359244b/HandheldCompanion/Devices/ASUS/AsusACPI.cs#L44-L45
[hc-acpi-curves]: https://github.com/Valkirie/HandheldCompanion/blob/ed6bc5153f6f316f9545ec176e739e61e359244b/HandheldCompanion/Devices/ASUS/AsusACPI.cs#L47-L49
[hc-acpi-51]: https://github.com/Valkirie/HandheldCompanion/blob/ed6bc5153f6f316f9545ec176e739e61e359244b/HandheldCompanion/Devices/ASUS/AsusACPI.cs#L51
[hc-device]: https://github.com/Valkirie/HandheldCompanion/blob/ed6bc5153f6f316f9545ec176e739e61e359244b/HandheldCompanion/Devices/IDevice.cs#L1095-L1115
[hc-services]: https://github.com/Valkirie/HandheldCompanion/blob/ed6bc5153f6f316f9545ec176e739e61e359244b/HandheldCompanion/Watchers/RogAllySpaceWatcher.cs#L10
[adlx]: https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/tree/32b5a740d42295c5dfe9026b9f52683da0f3af91/SDK/Include
[adlx-dll]: https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/32b5a740d42295c5dfe9026b9f52683da0f3af91/SDK/Include/ADLX.h#L25
[adlx-3d]: https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/32b5a740d42295c5dfe9026b9f52683da0f3af91/SDK/Include/I3DSettings.h
[ms-fse]: https://learn.microsoft.com/gaming/gdk/docs/gdk-dev/pc-dev/handheld/handheld-launchers
[ms-fse-h]: https://github.com/microsoft/win32metadata/blob/71033001b4479c6566546b68d32276e70a8d68b9/generation/WinSDK/RecompiledIdlHeaders/um/gamingexperience.h
[ms-apiset]: https://learn.microsoft.com/windows/win32/api/apiquery2/nf-apiquery2-isapisetimplemented
[ms-boost]: https://learn.microsoft.com/windows-hardware/customize/power-settings/options-for-perf-state-engine-perfboostmode
[ms-sps]: https://learn.microsoft.com/windows/win32/api/winbase/ns-winbase-system_power_status
[ms-scheme]: https://learn.microsoft.com/windows/win32/api/powersetting/nf-powersetting-powergetactivescheme
[ms-acvalue]: https://learn.microsoft.com/windows/win32/api/powrprof/nf-powrprof-powerreadacvalueindex
[ms-epm]: https://learn.microsoft.com/windows/win32/api/powersetting/nf-powersetting-powerregisterforeffectivepowermodenotifications
[ms-dxgi]: https://learn.microsoft.com/windows/win32/api/dxgi/ns-dxgi-dxgi_adapter_desc1
[ms-vc]: https://learn.microsoft.com/windows/win32/cimwin32prov/win32-videocontroller
[ms-svc]: https://learn.microsoft.com/windows/win32/cimwin32prov/win32-service
