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

Every catalogued endpoint is `Unconfirmed` until Bradley's Probe report matches it; Phase 4 writes only to
`ConfirmedByProbe` endpoints. `ReadOnlyGuardTests` scans `src/` and fails if a write path appears.

## Controller navigation

`Core/Input/GamepadNavigator` turns controller readings into commands: one step per press, auto-repeat, and stick hysteresis. Buttons held at startup are ignored. It is fully unit-tested. `App/Services/GamepadService` polls `Windows.Gaming.Input` while the window is active and maps the commands to XAML focus moves and UI Automation "invoke".

## CI

`.github/workflows/ci.yml` (windows-latest) runs the Core tests, publishes the app, signs our binaries, builds the Inno Setup installer, signs it, verifies every signature against the committed certificate's thumbprint, and uploads the installer as a build artifact. It never creates a GitHub Release. `main` is protected: changes arrive only by PR, with a green `build` check.
