# Ally Tuner

A native Windows (WinUI 3) app that tunes the **ROG Xbox Ally X (RC73XA)** for maximum plugged-in performance, safely and reversibly.

- **Status:** Phase 1 (detection and hardware probe). Everything is read-only; nothing is changed on the device yet.
- **Brief:** [docs/brief.md](docs/brief.md) · **Architecture:** [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) · **Hardware research and sources:** [docs/RESEARCH.md](docs/RESEARCH.md) · **Testing on the Ally:** [docs/TESTING-ON-ALLY.md](docs/TESTING-ON-ALLY.md)

## Getting a build

Every PR and every push to `main` runs CI on `windows-latest`. Open the run under **Actions** and download the `AllyTuner-<version>` artifact. It contains the signed installer and the instructions. There are no GitHub Releases yet.

## Developing (macOS or Windows)

```sh
dotnet test tests/AllyTuner.Core.Tests     # Core logic: runs anywhere with the .NET 10 SDK
dotnet build src/AllyTuner.Platform.Windows # compiles on macOS too (Windows targeting enabled)
dotnet build tests/AllyTuner.Platform.Windows.Tests  # compiles anywhere; the smoke tests RUN only on Windows (CI)
```

The WinUI app (`src/AllyTuner.App`) builds only on Windows, which in practice means CI.
