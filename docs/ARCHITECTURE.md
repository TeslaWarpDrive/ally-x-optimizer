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
```

In this build the only `ISettingWriter` is `NoHardwareWriter`, which fails if ever reached; `NoLiveWriterGuardTests`
fails the build if another writer appears in `src/`. The app shows a dry-run banner that cannot be closed, a
**Safety and changes** page (write mode, warnings, Reset to Safe Defaults, a dry-run trial of the checks, and the
change log with Undo), and **Reset to Safe Defaults** in the menu footer. Confirmation dialogs default to Cancel,
and the controller's B button cancels them.

## Controller navigation

`Core/Input/GamepadNavigator` turns controller readings into commands: one step per press, auto-repeat, and stick hysteresis. Buttons held at startup are ignored. It is fully unit-tested. `App/Services/GamepadService` polls `Windows.Gaming.Input` while the window is active and maps the commands to XAML focus moves and UI Automation "invoke".

A press can also arrive as a key event: arrow keys or Enter from a controller mode that emulates a keyboard, or the `Gamepad*` keys XAML's own focus navigation reacts to. `Core/Input/InputArbiter` (unit-tested) is shared by `GamepadService` and the window's root `PreviewKeyDown`/`PreviewKeyUp`. A key that echoes a controller press (same direction or A, while it is held or within 150 ms) is swallowed, and so is a controller press that its own key echo already handled. A keyboard used on its own is unaffected.

A failed controller read, focus move or button press is logged (at most once per 30 s, with a count) and skipped; it never crashes the app.

## CI

`.github/workflows/ci.yml` (windows-latest) runs the Core tests, publishes the app, signs our binaries, builds the Inno Setup installer, signs it, verifies every signature against the committed certificate's thumbprint, and uploads the installer as a build artifact. It never creates a GitHub Release. `main` is protected: changes arrive only by PR, with a green `build` check.
