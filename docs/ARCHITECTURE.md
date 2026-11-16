# Ally Tuner architecture

```
src/AllyTuner.Core              net10.0, no Windows APIs. All decision logic and the unit tests' target.
                                Builds and tests on the Mac:  dotnet test tests/AllyTuner.Core.Tests
src/AllyTuner.Platform.Windows  net10.0-windows. Talks to Windows: WMI, power, DXGI, Full Screen Experience
                                and ASUS ATKACPI (DSTS reads only, so far). Thin: reads values, decides nothing.
src/AllyTuner.App               WinUI 3 (Windows App SDK 2.6), unpackaged, self-contained, requireAdministrator.
tests/AllyTuner.Core.Tests      xUnit tests for everything in Core (run on the Mac and in CI).
tests/AllyTuner.Platform.Windows.Tests
                                Smoke tests that run the real read-only Windows code (CI only).
installer/AllyTuner.iss         Inno Setup script; CI builds and signs the installer.
scripts/                        Signing-certificate tooling (Mac) and CI helpers.
signing/                        PUBLIC certificate only. The private key lives only in GitHub secrets.
```

## Rules the code follows

- **Logic lives in Core, behind interfaces, and is unit-tested.** Platform code is kept as small as possible because it can only be exercised on the Ally.
- **Nothing writes to hardware or system settings without the safety framework** (Phase 2): confirm, log, snapshot for undo, clamp to known-safe ranges, and dry-run first. Phase 0 contains no write paths at all.
- **Hardware only after it is identified.** Every hardware control checks `DeviceClassification.IsSupported` (RC73XA only).
- **Every ASUS/AMD interface is cited.** No ACPI method ID, WMI device ID or register is used unless it comes from a cited source and is confirmed on the device by the Probe (Phase 1).

## Hardware probe (Phase 1)

```
Core/Asus/AsusAcpiProtocol      builds DSTS (read) requests only; there is no write method ID in the code base
Core/Asus/AsusEndpointCatalog   every ASUS device ID used, each with pinned sources (docs/RESEARCH.md)
Core/Asus/AsusValueDecoder ...  decodes replies (VRAM option list, fan curves, core layout); tested with a
                                public RC73XA scan as fixtures
Core/Probe                      report model and plain-text formatter
Platform/AsusAcpiReader         opens \\.\ATKACPI and sends catalogued DSTS reads, nothing else
Platform/ProbeRunner            runs every read-only step; a failing step is reported, not fatal
App/Pages/ProbePage             Run probe -> report saved to Documents\Ally Tuner\probe-*.txt
```

`Core/Asus/BradleyAllyEvidence` records what Bradley's reviewed Probe reports showed for each endpoint
(`ConfirmedByProbe`, `Unconfirmed` or `AbsentOnDevice`, with the quoted evidence). Each new Probe compares itself
with that record and the BIOS version. Phase 4 writes only to `ConfirmedByProbe` endpoints.
`ReadOnlyGuardTests` scans `src/` and fails if a write path appears.

## Safety framework (Phase 2)

Every change, from any page, profile or reset, goes through one path in `Core/Safety` (all unit-tested):

```
TunableSettings      what can be changed: id, safe range per power source (or a fixed list), safe default,
                     risk, whether it can be read back, the ASUS ID, and pinned sources for every number
ValueGuard           clamps numbers into the range for the current power source; refuses values not on a list
WriteGate            dry-run or live. Live needs ALL of: BuildSafety.LiveWritesInThisBuild (false in this
                     build), dry-run off, the setting released after a watched dry-run, and its ASUS ID
                     ConfirmedByProbe. Anything else is a dry-run.
SafeChangeService    Plan -> ChangePlan (before, after, clamp note, gate) -> ConfirmationText for the dialog
                     -> plan.Confirm() -> ExecuteAsync. Only a ConfirmedChange can execute. Execution checks
                     again: a stricter gate makes it a dry-run; a power-source change that makes the confirmed
                     value unsafe refuses it. Undo goes back to the value before, or the safe default.
                     Reset to Safe Defaults is one confirmation for all steps.
ChangeJournal        changes.jsonl in %LOCALAPPDATA%\AllyTuner: every change, simulated or real, plus
                     last-applied.json so write-only settings keep their known value. Also in app.log.
OverrideMonitor      compares read-back values with what was applied; warns or re-applies (at most 3 times
                     in 10 minutes, then stops and warns). Write-only settings can only get a warning while
                     Armoury Crate is running.
OverrideWatcher      the check MainWindow runs every 30 s: reads back every setting changed live, then warns
                     (banner + log, once per distinct message) or re-applies through SafeChangeService, so a
                     re-apply in dry-run is only simulated. With nothing applied live it has nothing to check,
                     and Settings and the Safety page say so.
```

In this build the only `ISettingWriter` is `NoHardwareWriter`, which fails if ever reached; `NoLiveWriterGuardTests`
fails the build if another writer appears in `src/`. The app shows a dry-run banner that cannot be closed, a
**Safety and changes** page (write mode, warnings, Reset to Safe Defaults, a dry-run trial of the checks, and the
change log with Undo), and **Reset to Safe Defaults** in the menu footer. Undo is offered only when it can work
(`SafeChangeService.CheckUndo`); otherwise the log entry says why, and the confirmation says so before the change. Confirmation dialogs default to Cancel,
and the controller's B button cancels them.

## Windows tweaks (Phase 3)

```
Core/Tweaks/WindowsTweaks       the brief's Windows tweaks as SettingDefinitions (choices, recommendation, restart,
                                warning, sources), Interpret(facts) -> value/availability/note, Recipe(value) -> the
                                exact registry/DISM/API write. "Not set" is a value, so Undo can delete a value again.
Core/Tweaks/StartupApps         startup apps as dynamic settings whose ID encodes where they live ("startup:UserRun:App"),
                                StartupApproved on/off markers, and Windows Security protected
Core/Tweaks/TweakSettingReader  ISettingReader + IWriteDescriber for SafeChangeService (facts cached 3 s)
Core/Tweaks/WindowsTweakReport  the Probe's "Windows tweaks (read-only)" lines (no startup app names)
Platform/WindowsTweakFactsReader  read-only registry / WMI / PowerGetUserConfigured*PowerMode
App/Pages/WindowsTweaksPage     live state, Apply recommended…, Restore original settings…, one card per tweak and
                                per startup app; every button plans a change and shows its confirmation
```

SafeChangeService gained what the tweaks need: a resolver for dynamic settings, write descriptions in the change
log ("Would set HKCU\...\AutoGameModeEnabled to 1 (REG_DWORD)."), refusal of unavailable settings (UEFI-locked memory
integrity, a missing feature), `PlanRecommended`, `PlanRestoreOriginals` and one confirmation per batch.
`WriteGate` keeps community-documented formats (GPU scheduling, windowed games, startup apps) dry-run until a Probe
on the Ally confirms them. `RestartTracker` lists live changes since boot that wait for a restart.
Sources and rules: RESEARCH.md §14.

## ASUS and power controls (Phase 4)

```
Core/Power/PowerControls       fan curves, CPU Boost (AC/DC), Battery Care, VRAM and cores as SettingDefinitions,
                               on top of TunableSettings (mode, SPL/sPPT/fPPT); PowerControls.All is profile order
Core/Power/DeviceControlFacts  decodes DSTS reads (VRAM, cores, factory fan curves, fan floors) + CPU Boost values
Core/Power/PowerControlReader  ISettingReader + IWriteDescriber: values, availability (ASUS only on an RC73XA, VRAM
                               list and core range must equal the reviewed Ally's), per-value refusal (unsafe fan
                               curves) and the exact DEVS / power-API call each change would make
Core/Power/FanCurveRules       custom curves never below any factory curve or the fan floor; the editor's start
Core/Power/PowerProfiles       built-in Performance Max / Balanced / Silent, saved profiles, JSON, apply order, AutoApply
Core/Safety/SettingRouter      one reader for the safety service: power controls vs Windows tweaks
Platform/DeviceControlFactsReader  DSTS reads (only on a supported device) + PowerReader.ReadScheme
App/Pages/Power|Graphics|Battery|ProfilesPage  cards built from the definitions; fan-curve and profile editors
```

Auto-apply: MainWindow checks the power source every 5 s and, on the change to AC with auto-apply on, applies the
plugged-in profile through `ConfirmationText.ConfirmAutoApply` (allowed only while the person has it turned on).
Sources and limits: RESEARCH.md §15.

## AMD graphics features (Phase 5)

```
Core/Amd/AmdFeatures        Anti-Lag, Radeon Super Resolution, Boost, Chill as SettingDefinitions; Interpret,
                            conflict refusals, ADLX call descriptions; AmdSettingReader (reads only on request)
Platform/AdlxReader         ADLX C vtable interop, IsSupported / IsEnabled only; AdlxSlots lists every slot used
App/Pages/GraphicsPage      "Read AMD settings", one card per feature, guidance for HYPR-RX, FSR and Auto SR
```

Sources: RESEARCH.md §16.

## Controller navigation

`Core/Input/GamepadNavigator` turns controller readings into commands: one step per press, auto-repeat, and stick hysteresis. Buttons held at startup are ignored. It is fully unit-tested. `App/Services/GamepadService` polls `Windows.Gaming.Input` while the window is active and maps the commands to XAML focus moves and UI Automation "invoke".

A press can also arrive as a key event: arrow keys or Enter from a controller mode that emulates a keyboard, or the `Gamepad*` keys XAML's own focus navigation reacts to. `Core/Input/InputArbiter` (unit-tested) is shared by `GamepadService` and the window's root `PreviewKeyDown`/`PreviewKeyUp`. A key that echoes a controller press (same direction or A, while it is held or within 150 ms) is swallowed, and so is a controller press that its own key echo already handled. A keyboard used on its own is unaffected.

A failed controller read, focus move or button press is logged (at most once per 30 s, with a count) and skipped; it never crashes the app.

## CI

`.github/workflows/ci.yml` (windows-latest) runs the Core tests, publishes the app, signs our binaries, builds the Inno Setup installer, signs it, verifies every signature against the committed certificate's thumbprint, and uploads the installer as a build artifact. It never creates a GitHub Release. `main` is protected: changes arrive only by PR, with a green `build` check.
