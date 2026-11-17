# Ally Tuner design: Aurora

Ally Tuner is a native WinUI 3 / Fluent app. Aurora is the look on top of it, defined in
`src/AllyTuner.App/Themes/Aurora.xaml` with Fluent overrides in `App.xaml`.

## Where it comes from

The palette and the "glow held to one place" rule come from the Aurora theme in Bradley's
[design-arsenal](https://github.com/TeslaWarpDrive/design-arsenal), which was read but not changed. That collection is
mostly web tooling (shaders, React kits, galleries), so only ideas carry over to WinUI. Nothing was copied:

| From design-arsenal | In Ally Tuner |
|---|---|
| Aurora palette (ink, surface, raised, text, accent, teal, violet, pink) | Same hex values, as XAML colours and brushes |
| Paper Shaders mesh orb with grain | The title bar and Home header use the Handheld Boost tile. The hero card edge keeps a static teal-to-pink gradient. No shader and no per-frame cost. |
| Design Taste "60-30-10" and contrast checks | Ink 60 %, raised surfaces 30 %, accent and status colours 10 %. Text on ink is #E6EAFB and muted text is #A4A9C8, both well above 4.5:1. |
| Hallmark's warnings about machine-made tells | No eyebrow labels over every card, and no single coloured word in a headline. The gradient is on the hero card edge only. |
| 60fps.design / Motion Primitives (motion references) | Fluent theme transitions only, with a staggered card entrance on each page |

## Rules

1. **Colour has a job.** Amber means dry-run or attention, teal means good or applied, and coral means failed. These status colours never decorate anything. The teal-to-pink gradient appears only on the hero card edge. The title bar and the Home header use the Handheld Boost tile.
2. **Safety stays loud.**
   - The dry-run banner is amber, sits on every page and has no close button.
   - Confirmation dialogs default to Cancel, show the from → to change, the safe range, any clamping and the undo path, and put the dry-run line in amber.
   - The change log tags each entry with a coloured outcome chip.
   - Reset to Safe Defaults is always in the menu footer.
3. **Readable on a 7-inch 1080p screen** (about 1280 × 720 effective pixels at 150 % scale):
   - Body text is 15–17 px, card values 24 px, page titles 34 px and metrics 48 px.
   - Buttons and menu items are 56 px tall.
   - Page content is at most 1120 px wide.
4. **Controller first.**
   - Every action is a button or slider that the D-pad reaches.
   - The focus ring is a 3 px periwinkle line with an ink edge.
   - B cancels a dialog and LB/RB switch sections.
5. **Low overhead.**
   - The background is an ink fill with two static radial glows, and there is no Mica, acrylic or shader.
   - The only looping animation is the dry-run dot. It runs only while dry-run is on and Windows' *Animation effects* setting is on.
   - Theme transitions follow that setting automatically.
6. **Dark only.** The app sets `RequestedTheme="Dark"`. High-contrast themes keep the system colours, because the overrides live in the dark theme dictionary only.

## Type

| Use | Font | Style key |
|---|---|---|
| Page titles, card values, metrics, brand | Bahnschrift SemiBold (ships with Windows) | `PageTitleTextStyle`, `CardValueTextStyle`, `MetricTextStyle`, `DisplayTextStyle` |
| Body and labels | Segoe UI Variable (Fluent default) | `BodyLargeTextStyle`, `CardLabelTextStyle`, `CardDetailTextStyle` |
| IDs, versions, the raw Probe report | Cascadia Mono, falling back to Consolas | `MonoDetailTextStyle`, `MonoFontFamily` |
