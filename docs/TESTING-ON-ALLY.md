# Testing Ally Tuner on the Ally

This build **only reads information**. It changes nothing on the device: no power, fan, VRAM, battery or Windows settings. The hardware probe (Phase 1) also only reads.

## What's in the download

| File | What it is |
|---|---|
| `AllyTuner-Setup-<version>.exe` | The installer (signed) |
| `AllyTuner-dev-signing.cer` | The public half of the signing certificate |
| `Install-SigningCert.ps1` | Trusts that certificate on this PC (one time) |
| `SHA256SUMS.txt` | Checksums, if you want to verify the download |

## One-time setup: trust the signing certificate

1. Unzip the download, for example to `Downloads\AllyTuner`.
2. Open **PowerShell as administrator**: Start, type `powershell`, then choose **Run as administrator**.
3. Run:
   ```powershell
   cd $HOME\Downloads\AllyTuner
   powershell -ExecutionPolicy Bypass -File .\Install-SigningCert.ps1
   ```
4. The thumbprint it prints should be `294D0BF609D8DDB65DE829842210A5646ACBE672`.

To undo this later, run the same command with `-Remove`.

> If **Smart App Control** is on (Windows Security → App & browser control), Windows may still block the app because it is not from a public publisher. Tell me if that happens; don't turn Smart App Control off just for this without talking it through.

## Install

Double-click `AllyTuner-Setup-<version>.exe`. The admin prompt should say **Verified publisher: Ally Tuner Dev Signing**. Leave "Launch Ally Tuner" ticked at the end.

Upgrading: run a newer installer over the old one. Uninstall: Settings → Apps → Ally Tuner. Your logs are kept in `%LOCALAPPDATA%\AllyTuner`.

## Phase 1 follow-up: re-check after the first reports

The first two reports (2026-11-15) are reviewed; the results are in docs/RESEARCH.md §13. With the next build:

1. **Run the probe on the desktop**, plugged in. *Key results* should say **Matches the reviewed results: yes**, followed by exactly what was checked (BIOS RC73XA.319; 18 confirmed IDs present, 3 absent IDs still absent, 5 held IDs present; presence only, values are not compared). If it says *no*, the lines under it say what changed (for example a new BIOS).
2. **Run it inside the Xbox Full Screen Experience.** This is still unverified: *Xbox Full Screen Experience* should say it is on.
3. **Send both reports** as before.

## Phase 1 checklist: hardware probe

Plug the Ally in for this. Each run takes a few seconds and saves a report to `Documents\Ally Tuner`.

1. **Home → System status.** Note what the four cards say: *ASUS control interface*, *Armoury Crate*, *Xbox Full Screen Experience*, *Power*. They refresh every 10 seconds; unplug the charger for a moment and check *Power* changes.
2. **Run the probe on the desktop.** Open **Hardware probe** (left menu, or the button on Home) and press **Run probe**. It should finish with "Probe finished" and show *Key results*. Note anything shown in orange.
3. **Run it again inside the Xbox Full Screen Experience.** Start Ally Tuner from the Full Screen Experience and run the probe once more. (This tells us whether Windows reports the Full Screen Experience correctly.)
4. **If Armoury Crate is open,** note which mode it shows (Silent / Performance / Turbo) and its VRAM setting, so they can be compared with the report.
5. **Controller, again.** Move around with the D-pad and press A on a few buttons. Each press should still move **one step** or press **once**, in the desktop and in the Full Screen Experience. (This build ignores the extra key presses some controller modes send.)
6. **Send both reports.** Copy the two newest `probe-<date>-<time>.txt` files from `Documents\Ally Tuner` to the Mac and paste them in. Also send `app.log` (Settings → **Open logs folder**) if anything failed.

Nothing in these reports is treated as confirmed until it has been reviewed. They contain no user name, PC name or serial numbers.

## Phase 0 checklist

Please note pass/fail for each item, plus anything that looks wrong:

1. **Starts elevated.** Windows shows an admin prompt each time it launches, and Home → Administrator says **Yes**.
2. **Device detection.** The green bar on Home says **ROG Xbox Ally X detected**. If it doesn't, the diagnostics report (step 8) is the most important thing to send me.
3. **Fills the screen**, and the text is easy to read at arm's length.
4. **Touch.** Tap each item in the left menu; each page opens. Buttons are easy to hit.
5. **Controller, using the Ally's built-in controls:**
   - D-pad and left stick move the highlight **one step per press**. Holding repeats after a short pause. *Watch for moves of two steps per press; it means the app and Windows are both handling the input.*
   - **A** opens the highlighted menu item or presses the highlighted button.
   - **B** jumps back to the left menu from anywhere.
   - **LB / RB** go to the previous / next section.
   - **Menu (≡)** opens Settings.
   - The highlight is always visible, and you never get stuck somewhere you can't leave with B.
6. **Xbox Full Screen Experience.** If you can, launch Ally Tuner while in the Full Screen Experience (for example from the library or Task View) and repeat steps 4–5. Note whether the admin prompt appears and whether the controller works.
7. **Keyboard (optional):** arrow keys move the highlight, and Enter/Space presses.
8. **Diagnostics report.** On Home, press **Save diagnostics report**. It saves a `.txt` to `Documents\Ally Tuner`. Copy that file to the Mac and paste its contents to me. It has no user name, PC name or serial numbers.
9. **Log.** Settings → **Open logs folder** opens `%LOCALAPPDATA%\AllyTuner\logs`. Copy `app.log` too if anything went wrong.

## Phase 2 (safety framework), for the final Ally test

On-device testing waits until the whole app is finished. Then, for this part:

1. A yellow **Dry-run** banner shows at the top of every page and has no close button.
2. **Safety and changes**: pick *Sustained power limit (SPL)*, move the slider to 50 W, press *Preview change…*. The dialog says 50 W was clamped to 35 W, and *Cancel* is selected first. Press **B**: the dialog closes and nothing is logged as changed.
3. Repeat and choose *Simulate change*. Before you confirm, the dialog's last line says this change has no undo (the device cannot report SPL and Ally Tuner had not set it before). A "Dry-run: would set…" entry appears in the change log **without an Undo button**, with the same explanation pointing to Reset to Safe Defaults.
4. Simulate a *Performance mode* change: its log entry **does** have *Undo…* (it returns to the safe default, Balanced).
5. **Reset to Safe Defaults** (bottom of the menu): the dialog lists the mode reset and explains the power limits. Simulate it; it appears in the log.
6. With Armoury Crate running, the Safety page warns that it can change power settings.
7. Settings: switch "When another app changes a setting back" and restart the app; the choice is kept. The card says the override check has nothing to check yet (nothing is applied live in dry-run).

## Phase 3 (Windows tweaks), for the final Ally test

1. **Windows tweaks** page: each card shows *Now:* with the same value as Windows Settings (Gaming > Game Mode; System > Display > Graphics; Windows Security > Device security > Core isolation; System > Power & battery > Power mode).
2. Press *Apply recommended…*: the dialog lists only what differs. Memory integrity, VM Platform and GPU scheduling say they need a restart. Memory integrity shows its security warning, and Cancel is selected first. Simulate it: each step appears in the change log with a "Would set …" or "Would run dism.exe …" line, and **nothing changes in Windows Settings**.
3. On one startup app, press *Turn off…* and simulate it. The log shows the StartupApproved write. Task Manager > Startup apps still shows it enabled.
4. **Device check for the community-documented formats.** In Windows Settings, toggle *Hardware-accelerated GPU scheduling*, then restart. Toggle *Optimizations for windowed games*. In Task Manager, disable and re-enable one startup app. After each, run the Probe: the *Windows tweaks (read-only)* section must show the matching raw value (HwSchMode 2/1, `SwapEffectUpgradeEnable=1/0`, StartupApproved first byte 03/02). Send the reports; only then can those three stay eligible for live writes.

## Phase 4 (ASUS controls), for the final Ally test

Everything here is a dry-run. Nothing on the device should change, and Armoury Crate should show no change.

1. **Power**: *Apply Performance Max…* lists, in order, mode → Turbo, both fans → Turbo curve, SPL 35 W, sPPT 45 W, fPPT 55 W and CPU Boost Aggressive. The last line says the power limits cannot be undone (no earlier value). Simulate it. The change log shows one "Would send DEVS …" line per step, and the fan steps list the 16 curve bytes.
2. Move SPL to 7 W and *Apply power limits…*: it is accepted (7–35 W). Unplug and try sPPT 13 W: accepted on battery; plugged in it is clamped to 14 W.
3. *Edit custom curve…* (CPU): the sliders cannot go below the firmware's values. Save, then *Custom curve…*: the confirmation shows your curve.
4. **CPU cores**: *Apply core layout…* with 2 P + 4 E. The dialog warns about a restart and lower performance. The log shows `DEVS 0x001200D2 = 0x0402`.
5. **Graphics**: *10 GB…* shows 8 GB → 10 GB with a restart warning. The log shows `DEVS 0x000600C4 = 10240`.
6. **Battery**: *Turn on Battery Care (80 %)…* logs `DEVS 0x00120057 = 80`.
7. **Profiles**: make a new profile, set it as the plugged-in profile and turn on auto-apply (it asks first). Unplug, then plug in: within about 5 s a banner says it was applied as a dry-run, and the change log shows the steps marked as applied automatically.
8. Watched first live writes come later, one control at a time, in a separate build.

## Phase 5 (AMD features), for the final Ally test

1. **Graphics**: press *Read AMD settings*. This is the first time Ally Tuner calls AMD's driver interface. If the app closes here, send the app log; that points at the ADLX interop.
2. The four cards (Anti-Lag, Radeon Super Resolution, Boost, Chill) show *Now:* values that match AMD Software: Adrenalin Edition's Gaming > Graphics tab.
3. If Chill is on, *On…* for Anti-Lag is refused and the dialog asks to turn Chill off first.
4. Simulate Anti-Lag on. The log shows `Would call IADLX3DAntiLag::SetEnabled(true)`, and AMD Software still shows it off.
5. The guidance card explains HYPR-RX, FSR and Auto SR.

