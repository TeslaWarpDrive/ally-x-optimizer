# Developer Handoff: ROG Xbox Ally X Performance Optimization Application
(Bradley's brief, relayed verbatim via Grok Bot 2026-11-15)

Application Type: Native Windows desktop application (WinUI 3 / WPF recommended) installed on the user's ROG Xbox Ally X.

Primary User Context: Device is always plugged in. Prioritize maximum sustained performance, thermal headroom, and automated application of high-power settings. Battery runtime is irrelevant; battery health protection is still desired.

Core Goal: Provide a single, reliable application that detects the ROG Xbox Ally X, applies and manages the optimizations listed below, offers global + per-game profiles, and minimizes the need for the user to manually touch Armoury Crate, AMD Software, or Windows Settings.

1. Device Detection & Privilege Handling
- Detect ROG Xbox Ally X (RC73XA) on launch and continuously.
- Require administrator elevation for system-level changes (power plans, registry, service control, VRAM, etc.).
- Gracefully handle non-Ally devices or when required ASUS/AMD services are missing.
- Detect whether Armoury Crate SE is running and offer to suppress or coexist with it.

2. Power Management & Manual Profiles
- Create, edit, save, and apply custom power profiles that set:
  - SPL (sustained), SPPT (short), and FPPT (peak) power limits.
  - CPU Boost on/off.
  - Custom fan curves (temperature-based).
- Default recommended plugged-in profile: aggressive sustained ~30–35 W with elevated short/peak boosts and a more aggressive fan curve.
- One-click apply of "Performance Max" (always-plugged-in preset).
- Ability to set a profile as the automatic default on AC power.
- Expose or integrate P-core / E-core configuration (number of active Zen 5 P-cores and Zen 5c E-cores). Note that changes require a restart — surface this clearly to the user and optionally trigger the restart.

3. VRAM Allocation
- Read current GPU memory assignment.
- Allow user to set VRAM allocation (recommended defaults: 8 GB, 10 GB, 12 GB).
- Apply the change via the same mechanism Armoury Crate uses (or equivalent registry/driver call).
- Warn that a restart is required and offer to restart.

4. AMD Graphics Feature Control
- Toggle or set defaults for:
  - HYPR-RX
  - Radeon Anti-Lag
  - Radeon Super Resolution (RSR)
  - Relevant AMD Adrenalin settings that can be controlled programmatically.
- Surface guidance or one-click presets for FSR / Auto SR usage (note: Auto SR is primarily controlled via Game Bar / Microsoft components; detect and guide or integrate where possible).
- Ability to apply these settings globally or per-game.

5. Windows System Optimizations
Implement one-click (and reversible) application of:
- Disable Memory Integrity (Core Isolation).
- Disable Virtual Machine Platform.
- Enable Game Mode.
- Enable Hardware-accelerated GPU scheduling.
- Enable Optimizations for windowed games.
- Manage startup applications (list, enable/disable non-essential items).
- Set Windows power mode appropriately.
- All changes must be reversible from within the app.

6. Battery Care
- Enable / disable Battery Care Mode (≈80% charge limit) via ASUS interfaces or equivalent.
- Recommend enabling it by default for always-plugged-in users.

7. Xbox Full Screen Experience Awareness
- Detect whether the user is in Xbox Full Screen Experience.
- Surface status and basic guidance (do not force the user out of it).
- Optionally prioritize or simplify certain controls when running under XFSE.

8. Profiles System
- Global profiles (e.g., "Max Performance Plugged-In").
- Per-game profiles that can automatically apply when a specific executable is detected:
  - Power limits / fan curve
  - VRAM (if different)
  - AMD feature toggles
  - CPU Boost state
  - Core configuration (with restart warning)
- Profile import/export (JSON or similar).

9. Monitoring Overlay (Optional but Recommended)
- Lightweight real-time overlay showing:
  - Current TDP / power draw
  - CPU/GPU clocks and utilization
  - Temperatures
  - FPS (if possible via existing hooks or PresentMon-style)
  - Active profile
- Must be low-overhead and easily toggleable.

10. Safety, Validation & UX Requirements
- Confirm dangerous or restart-required changes before applying.
- Log all changes for troubleshooting.
- Provide a "Reset to Safe Defaults" option.
- Clear status indicators for which optimizations are currently active.
- Handle cases where Armoury Crate or AMD Software overrides settings (detect and re-apply or warn).
- Support for future ASUS firmware/driver changes where possible.

11. Recommended Default Configuration on First Run (Always Plugged-In)
The application should offer a one-click "Apply Recommended Plugged-In Setup" that:
- Applies a high sustained Manual power profile (~30–35 W).
- Sets VRAM to 10 GB (with restart prompt).
- Enables Battery Care Mode.
- Disables Memory Integrity and Virtual Machine Platform.
- Enables HYPR-RX + Anti-Lag where controllable.
- Enables Game Mode and related Windows gaming settings.
- Sets aggressive fan curve.
- Leaves CPU Boost enabled.

Out of Scope (Unless Explicitly Requested Later)
- Full Armoury Crate replacement (controller remapping, RGB, game library).
- Automatic driver downloads.
- Non-Ally device support beyond basic detection.
- Cloud sync of profiles.

Delivery Expectation: The development team should treat the above as the feature set for v1. Prioritize reliable power profile application, VRAM control, the Windows tweaks, and the one-click recommended setup. Per-game profiles and monitoring can follow in a fast follow-up if needed.
