# Mod-category overhaul: path-driven classification + NPC detection

Status: design approved 2026-07-08, not yet implemented.
Supersedes: `docs/HANDOFF_MOD_CATEGORY_OVERHAUL.md` (that doc's NPC blocker is resolved by this design).

## Why

Today's mod-type classifier (`WorkbookCategoryCatalog.Detect` in
`PenumbraOrganizer.Core/Models/WorkbookWorkflowModels.cs`) guesses a mod's category from
keywords in its `Name`/`Author`/`Description`/`Tags`/`ContentSignalSummary` — words like
"dress" or "skin" in the mod's *name*. That's unreliable and doesn't scale. A user request for
a separate category for NPC-reskin mods (distinct from player-character mods) surfaced this as
a bigger problem worth a full overhaul: classify mods from what their files actually touch
(game paths, slot manipulations), not from name-guessing.

There's already a structurally-sound signal source, `PenumbraScanService.ClassifySignal` /
`ExtractContentSignals` (`PenumbraOrganizer.Infrastructure/Scanning/PenumbraScanService.cs`),
which reads real file paths and `Manipulations[].Slot` values — but today it only feeds a
diagnostic summary string, is missing several slot suffixes, and doesn't attempt NPC, Skin,
Minion/Mount/Pet/Ornament, or Furniture/Sound detection at all.

## Decided category list

15 primary categories, replacing the old 8 (Clothing, Accessories, Bodies, Skin, VFX and
Animation, Minions, Review, Others), plus an `Others` fallback — 16 `ModCategory` enum values
total:

| Category | Example path (from real mods) |
|---|---|
| Gear | `chara/equipment/e0755/model/c0101e0755_top.mdl` (also accessories: `chara/accessory/a0145/model/c0101a0145_ear.mdl`) |
| Weapon | `chara/weapon/w0101/obj/body/b0117/model/w0101b0117.mdl` |
| Face | `chara/human/c0101/obj/face/f0001/model/c0101f0001_fac.mdl` |
| Hair | `chara/human/c0101/obj/hair/h0001/model/c0101h0001_hir.mdl` |
| Body | `chara/human/c0101/obj/body/b0001/model/c0101b0001_top.mdl` (also tail: `.../c0701/obj/tail/...`, ears: `.../c1701/obj/zear/...`) |
| Skin | Same as Body but texture/material only, no `.mdl` |
| NPC | `chara/human/c1304/obj/body/b0001/model/c1304b0001_top.mdl` (race code ends `04`, or `9104`/`9204`) |
| Minion | `chara/monster/m8355/obj/body/b0001/model/m8355b0001.mdl` (2B automaton) |
| Mount | `chara/monster/m0466/.../m0466b0001...` or `chara/demihuman/d####/...` for demihuman-skeleton mounts |
| Pet | `chara/monster/m7102/obj/body/b0001/model/m7102b0001.mdl` |
| Ornament | `chara/monster/m6002/obj/body/b0001/model/m6002b0001.mdl` (Angel Wings) |
| Furniture | `bgcommon/hou/indoor/general/0424/texture/fun_b0_m0424_0a_d.tex` |
| VFX | `/vfx/` paths, `.avfx` |
| Sound | `.scd` |
| Animation | `.pap`, `/animation/` |
| Others | Fallback — nothing else matched, or evidence is too thin (mirrors today's Review/Others behavior) |

## Classification pipeline

Evaluated in priority order per mod, first match wins. A mod is scanned for every distinct
content path / `Manipulations[].Slot` entry it touches (via `ExtractContentSignals`), each path
produces one `ModTargetClassification`, and the mod's overall category is the highest-priority
category found across all its targets.

**1. Gear / Weapon hard rule — always wins.**
If any target has an equipment-slot suffix (`_met` head, `_top` body, `_glv` hands, `_dwn`
legs, `_sho` feet) under `chara/equipment/`, or an accessory-slot suffix (`_ear`, `_nek`,
`_wrs`, `_ril`/`_rir` rings) under `chara/accessory/` → **Gear**, full stop, regardless of any
other paths in the mod (e.g. one `_sho` item + ten accessory rings is still Gear). This is
today's rule from `WorkbookCategoryCatalog.Detect`'s doc comment, now driven by the real slot
suffixes instead of keywords — and fixes a live bug: today's `ClassifySignal` checks `_top`/
`_dwn`/`_sho` but is missing `_met`, `_glv`, `_ril`, and `_rir` entirely.

If no Gear signal is found but any target is under `chara/weapon/` → **Weapon**, same hard-rule
priority, checked second so Gear wins if a mod somehow touches both.

**2. Character-model branch** — targets under `chara/human/c{race}/obj/{slot}/...`. Checked in
this exact order, per target:

```
if IsNpcRaceCode(raceCode)          -> NPC          // checked first, wins regardless of subfolder
else if slot == "face"              -> Face
else if slot == "hair"              -> Hair
else if slot == "body" and textureOrMaterialOnly(target)  -> Skin
else if slot in {"body", "tail", "zear"}                  -> Body
```

- **Race code check (first, always):** FFXIV's playable race codes all end `01` (e.g. `0101`
  Hyur Midlander Male); every race has a matching NPC-only code ending `04` (e.g. `0104`). Plus
  two generic NPC buckets: `9104` (NPC_Male), `9204` (NPC_Female). If the code ends `04`, or is
  `9104`/`9204` → **NPC**, regardless of which subfolder — including `obj/face` and `obj/hair`,
  which never independently produce Face/Hair for an NPC race code.
  (Verified against `xivModdingFramework`'s `XivRace` enum: `c1304` = AuRa_Male_NPC, `c0804` =
  Miqote_Female_NPC — both confirmed NPC-only codes, contrasted against the playable `c0101`
  Body example which has the same file shape but code `0101`.)
- **Otherwise**, dispatch by `BodySlot` (confirmed against Penumbra's own `BodySlot.cs`, which
  has exactly six slots: `hair`, `face`, `tail`, `body`, `zear` (ear), `met` (head, character-
  customization sense only, not equipment)): `obj/face` → **Face**; `obj/hair` → **Hair**;
  `obj/tail`, `obj/zear` → **Body** always. Penumbra's own category list only promotes Hair and
  Face out of `BodySlot` — Tail and Ear fold into Body, matching that precedent. The Skin carve-
  out below applies **only** to `obj/body`; a texture-only Face or Hair mod stays Face/Hair.
- **Within `obj/body` only:** if the mod's targets under that path are texture/material only (no
  `.mdl`) → **Skin** instead of Body (retexture vs. full mesh replacement).

**3. Creature-model branch** — targets under `chara/monster/m####/...` or
`chara/demihuman/d####/...`: look up the model ID against the bundled ID table (below) →
**Minion** / **Mount** / **Pet** / **Ornament**. If the ID is not found in the table → **Others**,
with a note recording the unresolved ID (e.g. `"m9999 not found in bundled monster ID table"`).
Do not guess — an unmapped ID is most often new-patch content the table hasn't been refreshed
for, not a new category.

**4. Remaining path patterns:** `bgcommon/hou/` → **Furniture**; `/vfx/` or `.avfx` →
**VFX**; `.scd` → **Sound**; `.pap` or `/animation/` → **Animation**.

**5. Others** — nothing matched, or (mirroring today's Review trigger) the mod has scan
warnings or no author.

## Bundled monster/mount/pet/ornament ID table

Minion, Mount, and Ornament are indistinguishable by path alone — all three (and Pet) share the
identical shape `chara/monster/m####/obj/body/...`. Verified this is a real, unavoidable
limitation: even `xivModdingFramework` (built by the TexTools team with full game-data access)
doesn't derive these from paths — it reads two official game Excel sheets and resolves through
a third:

- `Companion.csv` (Minion) and `Mount.csv` (Mount) each have a `Model`/`ModelChara` column that
  is a `ModelChara.csv` row index (**not** the file-path number directly).
- `ModelChara.csv[index].Model` gives the actual `m####`/`d####` number used in file paths.
- `Ornament.csv` resolves the same way (confirmed: "Parasol" → `Model=2939` →
  `ModelChara[2939].Model=6001` → `chara/monster/m6001/...`, one ID off the user's own
  `m6002` Angel Wings example, same family).
- Pets have no dedicated sheet at all — `xivModdingFramework`'s own code comment says pet data
  is "hardcoded until a better way of obtaining it is found." This design does the same: a
  small hardcoded ID list (historically a few dozen: Scholar fairies, Summoner egis, Machinist
  turret, beast-tribe pets).
- `ModelChara.Type` distinguishes monster-skeleton (3, path root `chara/monster/`) from
  demihuman-skeleton (2, path root `chara/demihuman/`) mounts — both need to be recognized as
  Mount; company chocobo is an example of the demihuman-skeleton case.

Implementation: a one-time extraction from `xivapi/ffxiv-datamining`'s `Companion.csv` +
`Mount.csv` + `Ornament.csv` + `ModelChara.csv`, resolved into a flat `m####`/`d####` →
category static JSON resource checked into the repo, plus the hardcoded pet ID list.

**Accepted limitation:** this table goes stale as new game patches add IDs. No auto-update
mechanism is in scope — revisit only if it turns out to matter for real collections.

**Self-contained at runtime — no dependency on external tools being installed.** The research
that produced this design (reading `xivModdingFramework`, `Penumbra.GameData`, and
`ffxiv-datamining` source) is a **dev-time-only** activity — a one-time extraction the developer
runs once to produce the static JSON, the same way the extraction script pulled the CSVs above.
None of TexTools, `xivModdingFramework`, Lumina, SaintCoinach, or internet access are ever
required by the shipped app. The generated table ships embedded as a resource in the app itself
(same pattern as the rest of the app's data), and the classification pipeline reads only that
embedded resource plus the mod's own files at runtime. This applies to the whole pipeline, not
just this table — nothing in the design reads from a TexTools install, a game install's raw
sqpack data, or any path the user hasn't already configured (Penumbra's own mod/config folders,
which the app already depends on today).

## Data model

Replaces `WorkbookCategoryCatalog`'s flat `WorkbookCategoryDefinition` list and keyword
`Detect` method.

```csharp
public enum ModCategory
{
    Gear, Weapon,
    Face, Hair, Body, Skin, NPC,
    Minion, Mount, Pet, Ornament,
    Furniture, VFX, Sound, Animation,
    Others
}

public enum CanonicalTargetKind { GameFile, MetaManipulation }

public sealed record CanonicalGameTarget(
    string GamePath,
    string Root,          // e.g. "chara/equipment"
    string? Suffix,        // slot suffix, e.g. "top", "met", "zear"
    string? PrimaryId,      // e.g. "e0755", "c1304", "m8355"
    string? SecondaryId);   // e.g. "b0001"

public sealed record ModTargetClassification(
    CanonicalTargetKind TargetKind,
    ModCategory Category,
    string? DerivedSlotName,
    CanonicalGameTarget? GameTarget,   // null for MetaManipulation entries (a bare "slot:Head"
    string? Notes);                    // has no resolvable game path to parse Root/Suffix/Ids from)
```

`ModScanResult` gains `IReadOnlyList<ModTargetClassification> Targets` (one per distinct
content path/Manipulation) and a rolled-up `ModCategory DetectedCategory` computed from
`Targets` via the priority order above. Every entry in `Targets` is produced purely from
scanning the mod's own files/manipulations — there is no `ClassificationSource` enum, because
every target-level classification has exactly one source (the mod's own paths). `Targets` never
contains a manual-override entry.

**Manual override stays out of `Targets`/`ModScanResult` and lives where it already lives
today.** Checked `MainViewModel.cs:909` (`mod.DetectedType = row.ResolvedModType;`): overriding
the detected category via the workbook is already a post-scan merge applied directly to
`ModRowViewModel.DetectedType`, not a property of `ModScanResult` — `ModScanResult` is a pure
scan-time snapshot built before any workbook exists, so it has no business knowing about a later
pipeline stage. This design keeps that same shape: `ModRowViewModel` continues to hold
`DetectedType`, now sourced from `ModScanResult.DetectedCategory` by default and overwritten by
`WorkbookImportRow.ResolvedModType` on import exactly as it is today. No new override plumbing,
no `ManualOverride` enum case anywhere in the scanning-side model.

**Explicitly out of scope:** a `RuntimeTarget`/Glamourer-compatibility layer (runtime
`EquipSlot`/`BonusItemFlag`/`CustomizeIndex` state) was considered and dropped. This app
organizes existing Penumbra mod files on disk; it doesn't apply live appearance state to
actors, so that layer would be scaffolding with no consumer.

## Integration points

| Concern | File | Change |
|---|---|---|
| Category definitions + `Detect` | `PenumbraOrganizer.Core/Models/WorkbookWorkflowModels.cs` | Replace `WorkbookCategoryCatalog` keyword matching with the pipeline above, consuming `ModScanResult.Targets` |
| Path/slot signal extraction | `PenumbraOrganizer.Infrastructure/Scanning/PenumbraScanService.cs` | Extend `ClassifySignal`/`ExtractContentSignals` to produce `ModTargetClassification` per path, add the race-code/BodySlot/ID-table logic |
| Bundled ID table | New embedded resource under `PenumbraOrganizer.Infrastructure` (exact path TBD at plan time) | Static JSON, generated once dev-side from `ffxiv-datamining` CSVs and checked into the repo; shipped app reads only this embedded copy, no runtime fetch or external tool dependency |
| Detected-type display | `PenumbraOrganizer.App/ViewModels/ModRowViewModel.cs`, `MainWindow.xaml` Mods grid | `DetectedType` sourced from `ModScanResult.DetectedCategory`; `MainViewModel.cs:909`'s existing `mod.DetectedType = row.ResolvedModType` override on workbook import is unchanged |
| Organize strategies | `PenumbraOrganizer.App/ViewModels/MainViewModel.cs` | "By mod type" / "Type then creator" / "Creator then type" strategies consume `DetectedCategory` |

## Testing

Extend the existing fixture pattern (`PenumbraOrganizer.Tests/Fixtures/TemporaryPenumbraFixture.cs`)
with real path examples per category, including:
- Each of the 15 categories, using the real example paths gathered during design (see table above)
- NPC vs. playable contrast: identical `_top` mesh under `c0101` (playable → Body) vs. `c1304`/
  `c0804` (NPC), confirming the race-code suffix rule, not just presence of a human path
- Gear hard-rule precedence: a mod with one `_sho` (feet) file plus ten accessory rings still
  resolves to Gear
- Skin vs. Body: texture-only vs. `.mdl`-present under `obj/body`; and Skin's scope is narrow —
  texture-only under `obj/face` stays Face, texture-only under `obj/hair` stays Hair,
  texture-only under `obj/tail`/`obj/zear` stays Body (no Skin carve-out outside `obj/body`)
- NPC precedence over Face/Hair/Skin: texture-only `obj/face` or `.mdl` `obj/hair` under an
  NPC-only race code (`c1304`, `c0804`) resolves to NPC, not Face/Hair/Skin
- Monster ID table resolution for at least one confirmed Minion/Mount/Pet/Ornament ID, plus an
  unmapped `m####`/`d####` ID resolving to Others with a note (not silently guessed)
- Weapon vs. Gear: `chara/weapon/` path does not trigger the Gear hard rule
- Gear suffix completeness: each of `_met`/`_top`/`_glv`/`_dwn`/`_sho`/`_ear`/`_nek`/`_wrs`/
  `_ril`/`_rir` individually resolves to Gear
- Mixed-target rollup: a mod with one Gear-slot file and one `.avfx` file still rolls up to
  Gear at the mod level, while `Targets` retains both entries as evidence
- `MetaManipulation`-only entries (a bare `Manipulations[].Slot` with no `Files` backing it) are
  classified with `GameTarget = null` and don't crash/no-op the rollup

## Open items for the implementation plan

- Exact bundled-table format/location and the extraction script (or a checked-in generation
  note) that produced it
- Hardcoded pet ID list — needs sourcing, likely by hand from datamining sites since no clean
  sheet exists
- Whether `Others`/Review-fallback triggers (scan warnings, missing author) carry over unchanged
  from today's `Detect` logic or need adjustment now that signal-based detection is much richer

## Revision note

Reviewed 2026-07-08 against an external scope review. Accepted: category-count wording, nullable
`GameTarget` (for `MetaManipulation` entries), explicit unmapped-ID → Others-with-note behavior,
explicit Skin/NPC precedence ordering, enum member reordering, and the additional test cases
above. Corrected: manual override is a `ModRowViewModel`-layer concern (matching the existing
`DetectedType`/`ResolvedModType` merge at `MainViewModel.cs:909`), not a field on `ModScanResult`
— `ModScanResult` is a pre-workbook scan snapshot and has no reason to know about workbook
overrides. Rejected: adding `MetaManipulation` to a `ClassificationSource` enum and adding
`ManualOverride` to `CanonicalTargetKind` — both conflated the "file vs. manipulation" axis with
the "automated vs. manual" axis; `ClassificationSource` was dropped entirely since, with override
excluded from `Targets`, every target-level classification has exactly one source.
