# Ally Tuner architecture

```
src/AllyTuner.Core              net10.0, no Windows APIs. All decision logic and the unit tests' target.
                                Builds and tests on the Mac:  dotnet test tests/AllyTuner.Core.Tests
src/AllyTuner.Platform.Windows  net10.0-windows. Talks to Windows (WMI today; later registry, power
                                plans, ASUS ACPI, AMD ADLX). Thin: reads/writes values, decides nothing.
src/AllyTuner.App               WinUI 3 (Windows App SDK 2.6), unpackaged, self-contained, requireAdministrator.
tests/AllyTuner.Core.Tests      xUnit tests for everything in Core.
installer/AllyTuner.iss         Inno Setup script; CI builds and signs the installer.
scripts/                        Signing-certificate tooling (Mac) and CI helpers.
signing/                        PUBLIC certificate only. The private key lives only in GitHub secrets.
```

## Rules the code follows

- **Logic lives in Core, behind interfaces, and is unit-tested.** Platform code is kept as small as possible because it can only be exercised on the Ally.
- **Nothing writes to hardware or system settings without the safety framework** (Phase 2): confirm, log, snapshot for undo, clamp to known-safe ranges, and dry-run first. Phase 0 contains no write paths at all.
- **Hardware only after it is identified.** Every hardware control checks `DeviceClassification.IsSupported` (RC73XA only).
- **Every ASUS/AMD interface is cited.** No ACPI method ID, WMI device ID or register is used unless it comes from a cited source and is confirmed on the device by the Probe (Phase 1).

## Controller navigation

`Core/Input/GamepadNavigator` turns controller readings into commands: one step per press, auto-repeat, and stick hysteresis. Buttons held at startup are ignored. It is fully unit-tested. `App/Services/GamepadService` polls `Windows.Gaming.Input` while the window is active and maps the commands to XAML focus moves and UI Automation "invoke".

## CI

`.github/workflows/ci.yml` (windows-latest) runs the Core tests, publishes the app, signs our binaries, builds the Inno Setup installer, signs it, verifies every signature against the committed certificate's thumbprint, and uploads the installer as a build artifact. It never creates a GitHub Release. `main` is protected: changes arrive only by PR, with a green `build` check.
