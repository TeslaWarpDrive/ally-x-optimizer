You are building **Ally Tuner**, a native Windows app (WinUI 3, C#/.NET) for Bradley's ROG Xbox Ally X, Xbox edition (model RC73XA). Read `docs/brief.md` in this repo first: it is the full developer brief and the source of truth.

Your job: write ALL of the app code. You own a beautiful, easy-to-use UI/UX that works great on the Ally's 7-inch 1080p touchscreen and with a controller (big touch targets, full gamepad/keyboard focus navigation, works inside the Xbox Full Screen Experience).

Setup facts:
- You are on Bradley's Mac. There is no Windows PC. You cannot run WinUI here, so every build and test runs on GitHub Actions (`windows-latest`). The only real Windows machine is the Ally itself, where Bradley tests by hand.
- Repo: https://github.com/TeslaWarpDrive/ally-x-optimizer (public, already created, this folder is its clone). Bradley has never coded for Windows.

BEFORE WRITING ANY CODE, answer these two things in this terminal, briefly, then wait for Bradley to reply:
1. What effort level do you recommend for this job, and why?
2. Given there's no Windows PC (only the Ally), confirm or change the plan of: code on the Mac, build/test/package on GitHub Actions windows-latest, install and hardware-test on the Ally. Say what you'd change, if anything.
Then give a short phased plan (v1 priority: device detection + admin, power profiles, VRAM, Windows tweaks, one-click Recommended Plugged-In Setup; per-game profiles and overlay are a fast follow).

Hard rules:
- Safety bar: power limits, fan curves, VRAM and Battery Care use undocumented ASUS ACPI/WMI calls. Research real interfaces (ASUS WMI / ATKACPI, G-Helper's open-source code, AMD ADLX) and cite sources in code comments and docs. Never guess an ACPI method ID or register. Every write is confirmed, logged and reversible, values are clamped to known-safe ranges, and the first write of each control ships behind a dry-run mode Bradley watches on the Ally before it goes live. Include Reset to Safe Defaults.
- Unit tests for all non-hardware logic, and a GitHub Actions workflow that builds, tests and produces a signed MSIX or installer as a build artifact. Never push to main without a passing CI build (use branches + PRs). Never publish a GitHub Release unless Bradley OKs it.
- No secrets or tokens in the repo. Out of scope: Armoury Crate replacement (remapping, RGB, library), driver downloads, non-Ally support beyond detection, cloud sync.
- Tests and code review are also done separately by a Cursor test agent; you write code only.
