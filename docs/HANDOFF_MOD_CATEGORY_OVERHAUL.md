# Handoff: mod-category overhaul (NPC category + reliable classification)

_Started: 2026-07-08. Status: blocked on real mod examples — design not yet written, do not implement._

## Why this work is happening

A user asked for a separate category for mods that reskin/reshape NPCs, distinct from
player-character mods. Looking into it surfaced a bigger issue: today's mod-type
classification (`WorkbookCategoryCatalog.Detect` in
`PenumbraOrganizer.Core/Models/WorkbookWorkflowModels.cs`) is pure keyword matching against
the mod's `Name`/`Author`/`Description`/`Tags`/`ContentSignalSummary` — it guesses from
words like "dress" or "skin" in the mod's *name*, not from what the mod's files actually touch.
That's unreliable and doesn't scale to a finer category list.

There is a second, mostly-unused classifier already in the codebase:
`PenumbraScanService.ClassifySignal` (`PenumbraOrganizer.Infrastructure/Scanning/PenumbraScanService.cs`),
which looks at the actual file paths / `Manipulations[].Slot` values from each mod's
`meta.json`/`group_*.json` (`chara/equipment/...`, `_top`, `_dwn`, `_sho`, `/obj/hair/`, etc.).
That's structurally reliable but currently only feeds a diagnostic summary string, not the
real mod-type classification.

## What is DECIDED so far

- **Full overhaul, not additive.** Replace the current 8-category list (Clothing, Accessories,
  Bodies, Skin, VFX and Animation, Minions, Review, Others) with a set matching Penumbra's own
  category list — Gear, Face, Hair, Body, Skin, Minion, Mount, Furniture, VFX, Sound,
  Animation — plus the new **NPC** category. Only the "green checked" categories from
  Penumbra's own Mod Type Selection screen count (Racial Scaling, Pose, Other, Reshade Preset
  are excluded — user confirmed these don't matter here).
- **Hard rule, always wins:** if a mod has *any* content signal in the head, body, hand, legs,
  or feet equipment slots, it is Clothing/Gear — full stop, regardless of any other paths in
  the mod (e.g. one `feet` item + ten accessory rings is still Clothing). This should be
  encoded as the first, highest-priority check in whatever replaces `Detect()`, mirroring the
  existing priority-ordering comment in `WorkbookCategoryCatalog.Detect` ("Clothing → Accessories
  → Bodies → Skin → VFX/Animation → Minions") but driven by real slot signals, not keywords.
  Note `ClassifySignal`'s existing "Clothing" check is close but incomplete — it matches
  `_top`/`_dwn`/`_sho` but not `_met` (head) or `_glv` (hands).

## What is BLOCKED

**NPC detection heuristic — no reliable signal identified yet.** Slot/equipment-path signals
tell you *what body part* a mod touches, not *who* it's for (a specific NPC vs. any player
character). Two candidate approaches were raised and neither is confirmed:
1. Specific race/model-set ID codes known to be NPC-only (not selectable by players) —
   would need a maintained ID list.
2. Mod author metadata / naming convention (`meta.json` Name/Description/ModTags mentioning
   an NPC name) — less structurally reliable, no ID list to maintain.

Asked the user for 1–2 real example mods (an NPC-targeting mod, ideally with a contrasting
normal player-character mod of the same slot type) to inspect the actual file structure and
find the real distinguishing signal, rather than guessing. **Waiting on that before a design
or plan can be written.**

## Next steps for whoever picks this up

1. Get example mod folders (or `.pmp` files — those are just zip archives) from the user.
   Read their raw `meta.json` / `group_*.json` / file paths directly (same fixtures pattern as
   `PenumbraOrganizer.Tests/Fixtures/TemporaryPenumbraFixture.cs`) and diff an NPC mod against a
   normal one of the same slot type to find the real signal.
2. Resume the `superpowers:brainstorming` flow already in progress for this feature (scope and
   the hard rule are already agreed — pick back up at the NPC-detection question) and write a
   design doc once NPC detection is settled.
3. Design should cover: the new category list and priority order, whether classification moves
   from `WorkbookCategoryCatalog.Detect` to something driven by `ClassifySignal`'s signals (or a
   merge of both), and where else the classification is used besides the workbook — it also
   drives `DetectedType` in `ModRowViewModel` / the Mods grid and the "By mod type" / "Type then
   creator" / "Creator then type" organize strategies (`MainViewModel.cs`), so all of those need
   to move together.
4. Only after a design doc is written and approved should `superpowers:writing-plans` produce an
   implementation plan — this is explicitly marked "do not execute yet" per the user.

## Key files (orientation)

| Concern | File |
|---|---|
| Current keyword-based mod-type classifier (to be replaced) | `PenumbraOrganizer.Core/Models/WorkbookWorkflowModels.cs` (`WorkbookCategoryCatalog.Detect`) |
| Existing path/slot signal classifier (diagnostic only today) | `PenumbraOrganizer.Infrastructure/Scanning/PenumbraScanService.cs` (`ClassifySignal`, `ExtractContentSignals`) |
| Where `DetectedType` is shown/used | `PenumbraOrganizer.App/ViewModels/ModRowViewModel.cs`, `MainWindow.xaml` Mods grid |
| "By mod type" / combined strategies | `PenumbraOrganizer.App/ViewModels/MainViewModel.cs` |
| Existing classification lessons (Clothing/Accessory/Animation priority) | `docs/PROJECT_CONTEXT.md` → "Classification lessons from the successful reorganization" |
