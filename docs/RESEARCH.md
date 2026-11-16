# Ally Tuner hardware research

**Status (2026-11-15):** research done, and Bradley's first two Probe reports reviewed (§13). **18 of the 26 ASUS IDs are confirmed on Bradley's Ally** (BIOS RC73XA.319), 3 are absent and 5 are held back. The Xbox Full Screen Experience check is still unverified. Phase 1 code only **reads**.

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
13. [Bradley's Probe results (2026-11-15)](#13-bradleys-probe-results-2026-11-15)
14. [Windows tweaks (Phase 3)](#14-windows-tweaks-phase-3)
15. [ASUS and power controls (Phase 4)](#15-asus-and-power-controls-phase-4)

---

## 1. Confidence levels

Each interface climbs this ladder. Phase 4 writes only to interfaces on the top rung.

| Level | Meaning |
|---|---|
| **Single source** | One open-source implementation uses it. |
| **Cross-checked** | Two or more independent projects (for example G-Helper and the Linux kernel) use the same ID for the same thing. |
| **Public RC73XA evidence** | Another owner's ROG Xbox Ally X returned a sensible value for it (the G-Helper issue #4843 scans, §2). Useful for planning; **not** confirmation. |
| **Confirmed on Bradley's Ally** | Bradley's Probe report matches. Recorded in code as `DeviceConfirmation.ConfirmedByProbe` (`BradleyAllyEvidence.cs`, which quotes the report). It proves the ID exists and reads as predicted, **not** that a write works. 18 endpoints are at this level (§13). |
| *Absent on Bradley's Ally* | The ID is missing or the driver rejected the call (`DeviceConfirmation.AbsentOnDevice`). Nothing uses it. |

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

The same list is in code: `src/AllyTuner.Core/Asus/AsusEndpointCatalog.cs` (a unit test keeps this table and the code in sync). "Public scan" is what another owner's RC73XA returned (§2). The last column is Bradley's own Ally (§13): **confirmed**, *held* (matched, but held back on purpose) or *absent*. Values are shown after clearing the presence bit.

| ID | What | Sources | Agreement | Public RC73XA scan | Planned use | Bradley's Ally |
|---|---|---|---|---|---|---|
| `0x00120075` | Performance mode (thermal policy) | [GH L72][gh-acpi-72], [LX L76][lx-h-76], [HC L72][hc-acpi-72] | cross-checked | Present; reads **7** every time, even while G-Helper had set mode 2 → **read is not the current mode** | Phase 4 mode switch | **confirmed** |
| `0x001200A3` | SPL, sustained limit (PL1) | [GH L115][gh-acpi-ppt], [LX L109][lx-h-ppt], [HC L60][hc-acpi-ppt] | cross-checked | Present, reads **0** while G-Helper applied 13 W → **write-only** | Phase 4 power limits | **confirmed** |
| `0x001200A0` | sPPT, slow boost (PL2) | [GH L112][gh-acpi-ppt], [LX L108][lx-h-ppt], [HC L65][hc-acpi-ppt] | cross-checked | Present, reads 0 → write-only | Phase 4 | **confirmed** |
| `0x001200C1` | fPPT, fast boost (PL3) | [GH L126][gh-acpi-ppt], [LX L112][lx-h-ppt], [HC L70][hc-acpi-ppt] | cross-checked | Present, reads 0 → write-only | Phase 4 | **confirmed** |
| `0x001200B0` | APU sPPT (2022 models) | [GH L117][gh-acpi-ppt], [LX L110][lx-h-ppt] | cross-checked | Present, reads 0 | context only | *held* (no Ally source) |
| `0x001200B1` | Platform sPPT (2022 models) | [GH L118][gh-acpi-ppt], [LX L111][lx-h-ppt] | cross-checked | Present, reads 0 | context only | *held* (no Ally source) |
| `0x00120094` | CPU temperature (°C) | [GH L109][gh-acpi-temps], [GH HardwareControl L458][gh-hw-458] | single source | Present; 48–51 °C | status, overlay | **confirmed** |
| `0x00120097` | GPU temperature (G-Helper) / "DGPU_POWER_STATE" (Linux) | [GH L110][gh-acpi-temps], [LX L147][lx-h-147] | **sources disagree** | Present; 43–47 °C (looks like a temperature) | display only | *held* (meaning disputed) |
| `0x00110013` | CPU fan speed (×100 rpm) | [GH L63][gh-acpi-fans], [GH ×100 L906-L907][gh-hw-906], [LX L100][lx-h-fans], [HC L53][hc-acpi-fans] | cross-checked | Present; 35–38 (≈3,500–3,800 rpm) | status | **confirmed** |
| `0x00110014` | GPU fan speed (×100 rpm) | [GH L64][gh-acpi-fans], [LX L101][lx-h-fans], [HC L54][hc-acpi-fans] | cross-checked | Present; 34–38 | status | **confirmed** |
| `0x00110031` | Mid fan speed | [GH L65][gh-acpi-fans], [LX L102][lx-h-fans], [HC L55][hc-acpi-fans] | cross-checked | absent | only if present | *absent* (error 1) |
| `0x00110022` | CPU fan range | [GH L97][gh-acpi-devsfan], [HC L44][hc-acpi-devsfan] | cross-checked | Present; 0x9A | Phase 4 context | **confirmed** |
| `0x00110023` | GPU fan range | [GH L98][gh-acpi-devsfan], [HC L45][hc-acpi-devsfan] | cross-checked | Present; 0x8D | Phase 4 context | **confirmed** |
| `0x00110024` | CPU fan curve (per mode) | [GH L100, L635-L666][gh-acpi-curve], [LX L103][lx-h-curves], [LX asus-wmi.c L3604-L3647][lx-wmi-curve], [HC L47][hc-acpi-curves] | cross-checked | mode 0: 48,54,59,62,62,62,62,62 °C → 1,9,18,22,22,22,22,22 % | Phase 4 fan curves | **confirmed** |
| `0x00110025` | GPU fan curve (per mode) | [GH L101][gh-acpi-curve], [LX L104][lx-h-curves], [HC L48][hc-acpi-curves] | cross-checked | mode 0: same temps → 1,9,13,13,13,13,13,13 % | Phase 4 | **confirmed** |
| `0x00110032` | Mid fan curve (per mode) | [GH L102][gh-acpi-curve], [LX L105][lx-h-curves], [HC L49][hc-acpi-curves] | cross-checked | absent | only if present | *absent* (error 1) |
| `0x00110026` | CPU fan floor (8 duty bytes, 30–100 °C) | [GH L104, L668-L676][gh-acpi-floor], [GH Fans.cs L905-L908][gh-fans-floor] | single source | not in the scan | context | *held* (8 floor bytes, §13) |
| `0x00110027` | GPU fan floor (8 duty bytes, 30–100 °C) | [GH L105][gh-acpi-floor], [GH Fans.cs L905-L908][gh-fans-floor] | single source | not in the scan | context | *held* (8 floor bytes, §13) |
| `0x00120057` | Battery charge limit | [GH L85][gh-acpi-85], [LX L122-L123][lx-h-rsoc], [HC L51][hc-acpi-51] | cross-checked | Present; reads 0x01880000 even after G-Helper set **80 % (accepted: "BatteryLimit = 80 : OK")** → **write-only** | Phase 4 Battery Care | **confirmed** |
| `0x0012006C` | Charger type (1 barrel, 2 USB-C) | [GH L58-L61][gh-acpi-charger], [LX L128-L129][lx-h-charge] | cross-checked | Present; 2 = USB-C | status | **confirmed** |
| `0x000600C4` | **VRAM allocation with option list** | [GH L137, L830-L857][gh-acpi-vram], [scan with VRAM][scan2] | single source (+ public RC73XA success) | Present; **Auto, 512 MB, 1–16 GB in 1 GB steps; current 4 GB**. The owner switched 4 GB ↔ 16 GB with G-Helper successfully | Phase 4 VRAM (8/10/12 GB) | **confirmed** |
| `0x000600C1` | APU memory (original Ally) | [GH L136, L813-L828][gh-acpi-apumem], [LX L145][lx-h-145], [LX asus-armoury.c L720-L760][lx-armoury-apumem] | cross-checked | absent | fallback only | *absent* (error 1) |
| `0x001200D2` | **CPU core layout**, current (`0x0[E]0[P]`) | [GH L129, L859-L878][gh-acpi-cores] | single source | Present; **0x0503 = 3 P-cores + 5 E-cores** (matches Ryzen Z2 Extreme: 3 Zen 5 + 5 Zen 5c) | Phase 4 core config | **confirmed** |
| `0x001200D3` | CPU core layout, maximum | [GH L130][gh-acpi-cores] | single source | Present; 0x0503 | Phase 4 bound | **confirmed** |
| `0x001200D4` | CPU core layout, minimum | [GH L131][gh-acpi-cores], [GH Extra.cs L606-L611][gh-extra-cores] | single source | Present; 0x0001 = 1 P + 0 E | Phase 4 bound | **confirmed** |
| `0x001200E2` | MCU power-save (Ally controller MCU) | [LX L131-L132][lx-h-mcu] | single source | Present; 0, later 1 | context only | **confirmed** |

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
- Fan floors (`0x00110026`/`0x00110027`) are 8 duty bytes with no status word, one for each of 30, 40, … 100 °C: G-Helper reads them as a buffer and plots `floors[i]` at `30 + 10 i` °C ([GetFanFloor L668-L676][gh-acpi-floor], [Fans.cs L905-L908][gh-fans-floor]). Build 0.2.0+ci.3 wrongly read them as a status word; fixed after Bradley's reports (§13).
- Phase 4 rule: a custom curve may never be lower than the factory curve for the same mode at any temperature, nor below the fan floor; temperatures must rise, and duty stays within 0–100 %.

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

**Does (read-only):** reads device identity; opens ATKACPI (read-only handle first, G-Helper's read/write *handle* flags only if refused; opening a handle changes nothing); sends **DSTS reads** for the 26 catalogued IDs above, and every fan curve (CPU, GPU and mid) for 3 modes each; lists the method signatures of ASUS WMI classes in `root\wmi` (definitions only); lists ASUS/AMD/Xbox service and process **names**; reads DXGI adapters, the video driver, ADLX file presence, power state and plan, the Full Screen Experience state, and the Windows tweak values (§14; startup apps as counts and markers only, never names). It then saves `Documents\Ally Tuner\probe-<date>-<time>.txt`.

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

After the report: matching endpoints move to `ConfirmedByProbe` in a PR that quotes the report. Anything that does not match is investigated before Phase 4. Answers from the first reports are in §13.

## 12. Safety rules derived from this research

Phase 2 (the safety framework) and Phase 4 (ASUS writes) build on these:

1. Write only to endpoints marked `ConfirmedByProbe`, and only while a fresh Probe still matches them on the same BIOS (the Probe's *Matches the reviewed results* line). That line checks the BIOS version and the presence of every recorded ID (18 confirmed present, 3 absent still absent, 5 held present); it does not compare values, and a missing read counts as a difference.
2. Clamp power limits to the Linux RC73XA ranges (§6.1), separately for AC and battery.
3. VRAM: only values from the device's own option list. Never 0x100 or 0x101 on `0x000600C1`.
4. Cores: only within the device's reported min and max.
5. Fan curves: never below the factory curve, temperatures rising, duty 0–100 %.
6. Many settings cannot be read back (power limits, mode, charge limit). The app records what it applied, and "Reset to Safe Defaults" returns to firmware defaults through a mode that is confirmed in dry-run.
7. Every first write of each control ships in dry-run first, and Bradley watches it on the Ally.

Phase 2 implements these as code (`src/AllyTuner.Core/Safety`, see ARCHITECTURE.md): rule 2 is `TunableSettings` + `ValueGuard`, rules 1 and 7 are `WriteGate`, and rule 6 is `ChangeJournal`'s last-applied values and Reset to Safe Defaults resetting the mode.

## 13. Bradley's Probe results (2026-11-15)

Two Probe reports from Bradley's ROG Xbox Ally X, build `0.2.0+ci.3`, both on the Windows desktop: `probe-20261115-175955-battery-desktop.txt` (on battery, 75 %) and `probe-20261115-180315-ac-desktop.txt` (plugged in). The reports stay outside the repository; the ASUS replies are copied into `tests/AllyTuner.Core.Tests/Asus/BradleyAllyProbe.cs` and tested against the catalog. Per-ID notes are in `src/AllyTuner.Core/Asus/BradleyAllyEvidence.cs` and appear in every new report as "seen on the reviewed Ally".

### 13.1 Answers to §11

| # | Question | Answer from the reports |
|---|---|---|
| 1 | Detection fields | `Model` = "ROG Xbox Ally X RC73XA_RC73XA", `BaseBoardProduct` = "RC73XA", manufacturer "ASUSTeK COMPUTER INC.", BIOS **RC73XA.319**, Windows 11 Home build 26200. Detection holds. |
| 2 | Read-only handle | Yes: "Driver handle: opened (read-only)". |
| 3 | Presence vs the public scan | All 21 IDs the public scan had are present, with the same values where the value is static. The 5 IDs the scan lacked: 3 fail with Windows error 1, and the 2 fan floors return data (below). |
| 4 | VRAM | Same option list (Auto, 512 MB, 1–16 GB). Current **8 GB** (the public owner had 4 GB). Windows agrees: DXGI dedicated video memory 7.83 GB, 15.62 GB of RAM visible. |
| 5 | Fan curves, modes 1 and 2 | Read for CPU and GPU. Mode 1 is cooler and quieter, mode 2 faster, which fits 1 = Silent, 2 = Turbo. CPU Turbo: 48, 55, 58, 62, 66, 73, 73, 94 °C → 9, 26, 39, 48, 56, 65, 65, 78 %. |
| 6 | Armoury Crate | Running: services `ArmouryCrateControlInterface`, `ArmouryCrateSEService` and the ASUS services listed in the report; processes include `ArmouryCrateSE`, `ArmouryCrateSE.Service` and `ArmouryCrateSE.UserSessionHelper`. |
| 7 | Full Screen Experience | **Unverified.** On the desktop: API set present, *Supported* yes, *Active* no. The run inside the Full Screen Experience has not been done. |
| 8 | Baseline | Battery: plan "Performance" `{27fa6203-…}`. AC: plan "Balanced" `{381b4222-…}`. CPU Boost Aggressive (2) on AC and battery. Windows power mode Balanced in both. |
| 9 | WMI | `AsusAtkWmi_WMNB` lists DSTS, DEVS, INIT, WDOG and 15 others. Nothing calls them. |

### 13.2 What moved to confirmed, held or absent

- **Confirmed (18):** performance mode, SPL / sPPT / fPPT, CPU temperature, CPU and GPU fan speed, CPU and GPU fan range, CPU and GPU fan curves, battery charge limit, charger type, VRAM, the three core-layout IDs and MCU power-save. "Confirmed" means present and reading as predicted. Several of these are **write-only** (performance mode reads 7, the power limits read 0, the charge limit reads 0x01880000), exactly as in the public scan, so a read never shows what is set.
- **Held (5):** `0x001200B0` and `0x001200B1` matched, but no source ties them to the Ally. `0x00120097` reads 43–44 °C and moves with the CPU temperature, but Linux calls the same ID DGPU_POWER_STATE, so it stays display-only. The two fan floors are decoded from a single source after the fact.
- **Absent (3):** mid fan speed `0x00110031`, mid fan curve `0x00110032` and original-Ally APU memory `0x000600C1`. The call fails with Windows error 1 (ERROR_INVALID_FUNCTION) on both runs. That build read the mid fan curve for mode 0 only; the Probe now reads all three modes.

### 13.3 Things that looked odd, and what they mean

1. **Performance mode reads 7**, and **SPL, sPPT and fPPT read 0.** Expected: the public scan showed the same. These are write-only, so Phase 4 must record what it applies.
2. **Charge limit reads 0x01880000** (reply `00008901`), identical on both runs and to the public scan, where G-Helper had just set 80 %. The format is still unknown and it is not a percentage. Battery Care stays write-only.
3. **`0x00120097` is plausible as a GPU temperature** (43 → 44 °C while the CPU went 49 → 51 °C), but one device's numbers cannot settle a naming dispute between sources. Display only.
4. **Fan floors were misread.** Build 0.2.0+ci.3 decoded `0x00110026`/`0x00110027` as a status word and called them "not present". The replies (`0000000027303841`, `000000001D263243`, the same on both runs) are G-Helper's 8-byte floor table: 0 % up to 60 °C, then 39–65 % (CPU) and 29–67 % (GPU) from 70 to 100 °C. Fixed; held unconfirmed until a new report shows the same.
5. **The power plan changed with the charger**: "Performance" (a non-standard plan GUID `27fa6203-3987-4dcc-918d-748559d549ec`) on battery, Windows "Balanced" on AC, 3 minutes apart. Probably Armoury Crate switching plans by power source; not confirmed. Phase 4 has to expect Armoury Crate to change settings underneath it.
6. **Charger type is live**: 0 on battery, 2 (USB-C) on AC. Useful for "apply plugged-in settings only on AC".
7. **App version `0.2.0+ci.3.32edec9`**: `32edec9` is the merge commit GitHub builds for a pull request, not a branch commit. Expected.

### 13.4 Still to do on the Ally

- Run the Probe **inside the Full Screen Experience** (FSE detection is unverified).
- Run the Probe once with the next build: it should say *Matches the reviewed results: yes*, show the fan floors decoded, and read the mid fan curve for all three modes.
- After any BIOS update, run the Probe again before trusting the confirmations.

## 14. Windows tweaks (Phase 3)

The brief's §5 list. Each tweak is read live (read-only registry, WMI SELECT, documented Get APIs) and changed only through the safety framework. In this build every change is a dry-run, and the change log records the exact write it *would* make. The code that performs those writes is not in this build: `ReadOnlyGuardTests` still forbids registry writes, deletes, key creation, writable opens, power-mode Set calls and DISM process starts.

| Tweak | Read from | Write it would make | Documented by | Restart |
|---|---|---|---|---|
| Memory integrity | `HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity` `Enabled` and `Locked`; `Win32_DeviceGuard.SecurityServicesRunning` contains 2 = running | `Enabled` = 0/1 (DWORD), the values Microsoft documents (never deleted). When `Enabled` is not stored, the current value is Windows' running state. `Locked` = 1 (UEFI lock) makes it unavailable. | Microsoft ([ms-hvci]) | yes |
| Virtual Machine Platform | `Win32_OptionalFeature` `Name='VirtualMachinePlatform'`, `InstallState` 1 enabled / 2 disabled / 3 absent ([ms-optfeature]); feature name from [ms-wsl] | `dism.exe /Online /Disable-Feature /FeatureName:VirtualMachinePlatform /NoRestart` (enable: `/Enable-Feature … /All`, as in [ms-wsl]; [ms-dism]) | Microsoft | yes |
| Game Mode | `HKCU\Software\Microsoft\GameBar` `AutoGameModeEnabled` (DWORD 0/1) | the same value, or delete it for "not set" | Microsoft ([ms-settings11]); "not set = on" from [winhance-gamemode] | no |
| Hardware-accelerated GPU scheduling | `HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers` `HwSchMode`: 2 = on, 1 = off | the same value, or delete it so the driver decides | community only ([winhance-hags]) | yes |
| Optimizations for windowed games | `HKCU\Software\Microsoft\DirectX\UserGpuPreferences` `DirectXUserGlobalSettings`, key `SwapEffectUpgradeEnable=0/1;` inside a shared string | change only its own entry in place (or append it, or remove only it); every other `Key=Value;` (Auto HDR, VRR) keeps its place and text | feature: Microsoft ([ms-windowed]); storage: community ([winhance-windowed]) | no (next game start) |
| Power mode (plugged in / on battery) | `PowerGetUserConfiguredACPowerMode` / `DCPowerMode` ([ms-pm-get]): `961cc777-…` best efficiency, `00000000-…` balanced, `ded574b5-…` best performance | `PowerSetUserConfiguredACPowerMode` / `DCPowerMode` ([ms-pm-set]) | Microsoft | no |
| Startup apps | Run keys (HKCU, HKLM, HKLM WOW6432Node) and both Startup folders ([ms-run]); on/off from `…\Explorer\StartupApproved\{Run, Run32, StartupFolder}`: no value or first byte 02 = on, 03 = off (both sources agree). Other markers are disputed: Greenshot reads even = on / odd = off, BCU reads anything but 02 as off. They show as unknown and are not changed. | the 12-byte marker `03 00 …` (off) or `02 00 …` (on). Undo puts back the exact original bytes, or deletes the marker if there was none. The Run entry itself is never touched. | Run keys: Microsoft. StartupApproved: community ([bcu-startup], [greenshot-startup]) | no |

Rules that follow:

- **Community-documented formats stay dry-run** (`SettingDefinition.NeedsDeviceCheck`, enforced by `WriteGate`): GPU scheduling, windowed games and every startup app. They become eligible for live writes only after the final Ally test confirms the format: toggle each one in Windows Settings or Task Manager, run the Probe, and check that its *Windows tweaks (read-only)* section shows the change.
- **"Not set" is a value** for Game Mode, GPU scheduling and windowed games: Windows can have no stored value, the app shows *Not set*, and Undo deletes the value again instead of writing a default. Memory integrity is the exception: deleting `Enabled` is not documented, so it only ever writes 0 or 1.
- **Unknown means hands off.** A setting whose current value cannot be read, or has a value Ally Tuner does not recognise, cannot be changed: Undo would have nothing to go back to.
- **Recommendations** (brief §5, for an always-plugged-in Ally): memory integrity off, VM Platform off, Game Mode on, GPU scheduling on, windowed games on, and best performance when plugged in. *Apply recommended* plans only what differs, with one confirmation.
- **Reversible:** every change can be undone from the change log because the value before is read from the device. *Restore original settings* returns each tweak to its value before Ally Tuner first changed it. These tweaks are not part of Reset to Safe Defaults, which stays about the ASUS power state.
- **Security trade-off:** memory integrity protects against malicious and vulnerable drivers ([ms-hvci]). The confirmation says so, and the app never touches a UEFI-locked setting.
- **Restart warnings** appear in each confirmation and on the page. After a live change (none in this build), the page lists the changes that still wait for a restart.
- **Not touched:** Windows Security's own startup entry (`SecurityHealthSystray.exe`). ASUS and AMD startup entries carry a caution.
- Microsoft's 2022 gaming-performance support article that recommended turning memory integrity and VM Platform off no longer has that text, so the recommendation is cited to the brief, not to Microsoft.

[ms-hvci]: https://learn.microsoft.com/windows/security/hardware-security/enable-virtualization-based-protection-of-code-integrity
[ms-optfeature]: https://learn.microsoft.com/windows/win32/cimwin32prov/win32-optionalfeature
[ms-wsl]: https://learn.microsoft.com/windows/wsl/install-manual
[ms-dism]: https://learn.microsoft.com/windows-hardware/manufacture/desktop/dism-operating-system-package-servicing-command-line-options
[ms-settings11]: https://learn.microsoft.com/windows/apps/develop/settings/settings-windows-11
[ms-windowed]: https://support.microsoft.com/topic/3f006843-2c7e-4ed0-9a5e-f9389e535952
[ms-pm-get]: https://learn.microsoft.com/windows/win32/api/powrprof/nf-powrprof-powergetuserconfiguredacpowermode
[ms-pm-set]: https://learn.microsoft.com/windows/win32/api/powrprof/nf-powrprof-powersetuserconfiguredacpowermode
[ms-run]: https://learn.microsoft.com/windows/win32/setupapi/run-and-runonce-registry-keys
[winhance-gamemode]: https://github.com/memstechtips/Winhance/blob/db80ecfcf6762a7b856860930d9f4ee17bf1e67c/src/Winhance.Core/Features/Optimize/Models/GamingAndPerformanceOptimizations.cs#L20-L37
[winhance-hags]: https://github.com/memstechtips/Winhance/blob/db80ecfcf6762a7b856860930d9f4ee17bf1e67c/src/Winhance.Core/Features/Optimize/Models/GamingAndPerformanceOptimizations.cs#L603-L621
[winhance-windowed]: https://github.com/memstechtips/Winhance/blob/db80ecfcf6762a7b856860930d9f4ee17bf1e67c/src/Winhance.Core/Features/Optimize/Models/GamingAndPerformanceOptimizations.cs#L626-L644
[bcu-startup]: https://github.com/BCUninstaller/Bulk-Crap-Uninstaller/blob/30da609384c98ba6e35c6530129541ea4ff3970a/source/UninstallTools/Startup/Normal/NewStartupDisable.cs#L18-L115
[greenshot-startup]: https://github.com/greenshot/greenshot/blob/4637cbc7dc0a22368191bc2df82c07e5926d657f/src/Greenshot/Helpers/StartupHelper.cs#L278-L305

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
[gh-fans-floor]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/Fans.cs#L905-L908
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

## 15. ASUS and power controls (Phase 4)

Every control below goes through the safety framework. Changes are clamped, confirmed, logged and reversible, and in this build every one is a **dry-run** (`LiveWritesInThisBuild` = false). The change log records the exact call each change *would* make, for example `Would send DEVS 0x001200A3 = 35 (35 W) through ATKACPI.` No code in this build sends DEVS or writes a power setting; the read-only guard tests still enforce that. The first live write of each control waits for Bradley to watch it on the Ally.

| Control | Read | Write it would make | Limits | Sources |
|---|---|---|---|---|
| Performance mode | write-only (reads 7) | DEVS `0x00120075` = 0 Balanced / 1 Turbo / 2 Silent | those three only | [GH AsusACPI L72, L169-L173][gh-p4-acpi], Linux asus-wmi L98-L100 |
| SPL / sPPT / fPPT | write-only (reads 0) | DEVS `0x001200A3`, then `0x001200A0`, then `0x001200C1` (G-Helper's order) | AC 7–35 / 14–45 / 19–55 W; battery 7–35 / 13–45 / 19–55 W | [Linux RC73XA table][lx-armoury-rc73xa], [GH SetPower L349-L405][gh-p4-setpower] |
| Fan curves (CPU, GPU) | factory curve per mode (DSTS arg 0/1/2) | DEVS `0x00110024` / `0x00110025` with 16 bytes (8 temperatures, then 8 duties). *Firmware curve* re-sends the mode, which is how G-Helper drops a custom curve. | never below **any** factory curve or the fan floor at any temperature; temperatures rise; 0–100 % | [GH SetFanCurve L605-L633][gh-p4-fan], [GH AutoFans L231-L262][gh-p4-autofans] |
| CPU Boost (AC / battery) | `PowerReadAC/DCValueIndex` PERFBOOSTMODE in the active plan | `PowerWriteAC/DCValueIndex`, then `PowerSetActiveScheme` | Microsoft's values 0–4 | [MS PERFBOOSTMODE][ms-boost], [MS PowerWriteACValueIndex][ms-p4-write], [MS PowerSetActiveScheme][ms-p4-setactive] |
| Battery Care | write-only (reads 0x01880000) | DEVS `0x00120057` = percent | 40–100 %; 100 removes the limit; recommended 80 | [GH BatteryControl L30-L34, L65-L80][gh-p4-battery] |
| VRAM | `0x000600C4`: current and option list | DEVS `0x000600C4` = the option's value (MB) | only values on the device's own list, and only while that list equals the reviewed one; restart needed | [GH GetVramOptions/SetVramMem L830-L857][gh-p4-vram], [GH Extra.cs L652-L712][gh-p4-vram-ui] |
| CPU cores | `0x001200D2` (current), `D3` (max), `D4` (min) | DEVS `0x001200D2` = `(E << 8) | P` | between the reviewed min (1 P + 0 E) and max (3 P + 5 E), at least one P-core, only while the device reports that range; restart needed | [GH GetCores/SetCores L859-L878][gh-p4-cores], [GH Extra.cs L590-L634][gh-p4-cores-ui] |

**Profiles.** Built-in: **Performance Max**, Balanced and Silent. Performance Max is Turbo, the firmware's Turbo fan curve, SPL 35 W, sPPT 45 W, fPPT 55 W, and CPU Boost Aggressive. Those limits are the values Linux asus-armoury starts the RC73XA at when plugged in: no AC defaults are listed, so it uses each maximum ([asus-armoury.c L1012-L1026][lx-p4-acinit]). A profile is applied as one confirmed batch, in G-Helper's order: mode, fans, then the limits ([ModeControl L120-L167][gh-p4-mode]). Saved profiles live in settings.json and export/import as JSON. Imports get fresh IDs and never replace anything.

**Auto-apply on AC.** Off by default, and turning it on asks first. When it is on, the plugged-in profile is applied on the change to AC through the same checks, logged with that reason, and is a dry-run in this build.

**Not changed by Reset to Safe Defaults:** CPU Boost, Battery Care, VRAM and cores. Their undo is in the change log; VRAM, cores and CPU Boost are read back, so the value before is known. Reset covers the mode, the fans (firmware curve) and the power limits (via the mode).

**Hypotheses still to check in the watched dry-run (§6.2):** that re-sending the mode restores the firmware's own power limits and fan curve, and that Battery Care holds the charge near the limit.

[gh-p4-acpi]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/AsusACPI.cs#L169-L173
[gh-p4-setpower]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/Mode/ModeControl.cs#L349-L405
[gh-p4-fan]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/AsusACPI.cs#L605-L633
[gh-p4-autofans]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/Mode/ModeControl.cs#L231-L262
[gh-p4-battery]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/Battery/BatteryControl.cs#L65-L80
[gh-p4-vram]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/AsusACPI.cs#L830-L857
[gh-p4-vram-ui]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/Extra.cs#L652-L712
[gh-p4-cores]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/AsusACPI.cs#L859-L878
[gh-p4-cores-ui]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/Extra.cs#L590-L634
[gh-p4-mode]: https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/Mode/ModeControl.cs#L120-L167
[lx-p4-acinit]: https://github.com/torvalds/linux/blob/fa2933aac170dd19c6a7f0d7b81a8f537e40915c/drivers/platform/x86/asus-armoury.c#L1012-L1026
[ms-p4-write]: https://learn.microsoft.com/windows/win32/api/powrprof/nf-powrprof-powerwriteacvalueindex
[ms-p4-setactive]: https://learn.microsoft.com/windows/win32/api/powrprof/nf-powrprof-powersetactivescheme

