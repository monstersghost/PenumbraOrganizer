# Handoff: sort_order.json retarget + metadata-editing engine

_Last updated: 2026-06-29. Supersedes the storage assumptions in the older docs (see "Stale docs" below)._

## Why this work happened

The app was built assuming Penumbra stores virtual-folder organization in a LiteDB
database `mod_data.db` with a `LocalModData` collection / `Folder` field. **That file does
not exist on a real Penumbra install.** The real, file-based format (verified live) is:

- **`<config>/sort_order.json`** — authoritative virtual-folder organization.
  Shape: `{ "Data": { "<mod dir name>": "<full path incl. display leaf>" }, "EmptyFolders": [ ... ] }`.
  The `Data` value encodes both the containing folder (everything before the last `/`) and the
  mod's display/sort name (the final segment). A mod with no entry sits at the root using its
  `meta.json` name.
- **`<config>/mod_data/<mod dir name>.json`** — per-user local data: `FileVersion` (mixed **2 and 3**),
  `ImportDate`, `LocalTags`, `Note`, `Favorite`.
- **`<modRoot>/<mod dir name>/meta.json`** — author metadata: `Name`, `Author`, `Description`,
  `Version`, `Website`, `ModTags`.

The same string is the physical folder name, the `sort_order.json` `Data` key, and the
`mod_data/<id>.json` filename. That is the `StableScanId`.

Real install paths on the original dev machine:
- config: `%AppData%\XIVLauncher\pluginConfigs\Penumbra`
- mod library (assets — never edited): `C:\Mods`

## What is DONE (merged in PR #1)

All of this is implemented, tested (129 tests green), and verified end-to-end against **copies**
of the real install (the live config and `C:\Mods` were never modified).

- **Reader/scanner** read `sort_order.json` (`PenumbraSortOrder`), surfacing `EmptyFolders` into
  `ScanInventory.EmptyFolders`.
- **Writer** (`PenumbraVirtualFolderWriter`) rewrites `sort_order.json` via a `JsonNode`
  round-trip that preserves unknown top-level keys and each mod's display leaf.
- **Folder organization**: mod moves, and create/delete/rename of empty folders, all persist.
  The writer is authoritative for `EmptyFolders`.
- **Absent `sort_order.json`** is materialized as a canonical empty baseline before backup
  (`ApplyService.EnsureSortOrderTargetsExist`), so fresh installs work; rollback restores the
  empty baseline. The proven backup/rollback core is untouched.
- **Metadata-editing engine**: `ModMetadataEdit` + `ProposalSnapshot.MetadataEdits` +
  `PenumbraMetadataWriter` emit one `DryRunFileChange` per touched `meta.json` and
  `mod_data/<id>.json` (preserving unknown fields and `FileVersion`). `DryRunPlanner` produces
  **multi-file** plans that flow through the existing N-file backup/apply/rollback.
- All LiteDB usage removed from production **code** (the package reference still lingers — see below).

## What is LEFT TO DO

### P1 — Metadata-editing UI (the main user-facing gap)
The engine is complete and proven, but there is **no WPF UI to enter metadata edits**, and
`MainViewModel.BuildBaseProposalSnapshot` passes **no** `MetadataEdits`. Needed:
- UI to edit per-mod `Favorite` / `LocalTags` / `Note` (local data) and
  `Name` / `Author` / `Description` / `Version` / `Website` / `ModTags` (author meta.json) —
  e.g. grid columns + an edit dialog.
- Collect edits into `IReadOnlyList<ModMetadataEdit>` and pass them when building the snapshot
  (`BuildBaseProposalSnapshot` → `new ProposalSnapshot(..., metadataEdits)`).
- Persist in-progress edits in the organizer session (`OrganizerSessionDocument`) so they survive
  restore, like folder proposals do.
- Surface metadata changes in the Review Changes / dry-run summary and the Apply confirmation.

### P1 — Confirm the Apply flow is actually exposed end-to-end in the app
The alpha shipped Apply "guarded" against the old `mod_data.db` target. Verify the WPF Review →
Backup → Apply path now lets a user actually apply `sort_order.json` (and metadata) changes,
and that the Backups screen / rollback UI work against the new operations. Run the app and walk
the flow (the automated tests cover the engine, not the WPF wiring).

### P2 — Update stale documentation
These docs still describe the `mod_data.db` / `LocalModData.Folder` model and are now wrong:
- `docs/ARCHITECTURE.md`
- `docs/BACKUP_AND_ROLLBACK_FORMAT.md`
- `docs/DRY_RUN_AND_APPLY_FORMAT.md`
- `docs/CONTROLLED_LIVE_TEST.md`
- `docs/REAL_INSTALLATION_VALIDATION.md`
- `docs/SAFETY_AND_ROLLBACK.md`
- `docs/PROJECT_CONTEXT.md`
- `docs/PENUMBRA_DISCOVERY.md`
- `docs/HANDOFF_ROLLBACK_FOUNDATION.md`
Update them to reference `sort_order.json` + `mod_data/<id>.json` + `meta.json`, multi-file
backup, and the metadata-editing engine.

### P2 — Remove the unused LiteDB dependency
`PenumbraOrganizer.Infrastructure/PenumbraOrganizer.Infrastructure.csproj` still has
`<PackageReference Include="LiteDB" Version="5.0.21" />`. Nothing uses it anymore; remove it and
drop the LiteDB entry from `THIRD_PARTY_NOTICES.txt`.

### P3 — Known limitations / edge cases to address later
- **Authoritative-EmptyFolders footgun**: any code that builds a `ProposalSnapshot` MUST include
  existing empty folders in `Folders`, or the writer will delete them from `sort_order.json`.
  Both current builders (`MainViewModel.BuildBaseProposalSnapshot` via `SeedExistingEmptyFolders`,
  and `ControlledLiveTestService.BuildControlledSnapshot`) handle this. New snapshot builders must
  too. Consider centralizing/guarding this.
- **meta.json `Name` vs display leaf**: editing a mod's `meta.json` `Name` changes its display name
  only for root mods. A *placed* mod's display name is the `sort_order.json` leaf, which is
  preserved independently. There is currently no way to rename the display leaf of a placed mod.
  Decide the intended UX (e.g. editing `Name` could also rewrite the leaf).
- **Absent `mod_data/<id>.json` when editing local data**: `PenumbraMetadataWriter` throws if the
  file is missing. Installed mods normally have one, but consider the empty-baseline approach
  (as done for `sort_order.json`) if this surfaces.
- **`DiagnosticExportService`** still sanitizes the literal `mod_data.db` (harmless legacy) — can be
  dropped.
- **Legacy `mod_filesystem/organization.json`**: still referenced by the test fixture and an
  "ignored, non-authoritative" integration test. It is not a real Penumbra file; can be removed.

## Key files (orientation for the next dev)

| Concern | File |
|---|---|
| sort_order.json read model | `Infrastructure/Penumbra/PenumbraSortOrder.cs` |
| Scan (reads folders + empty folders) | `Infrastructure/Scanning/PenumbraScanService.cs` |
| Folder writer (sort_order.json) | `Infrastructure/Apply/PenumbraVirtualFolderWriter.cs` |
| Metadata writer (meta.json + mod_data) | `Infrastructure/Apply/PenumbraMetadataWriter.cs` |
| Plan assembly (folder + metadata) | `Infrastructure/Apply/DryRunPlanner.cs` |
| Apply / per-kind validation / ensure-baseline | `Infrastructure/Apply/ApplyService.cs` |
| Post-apply verification | `Infrastructure/Apply/PostApplyVerificationService.cs` |
| Backup / rollback (untouched core) | `Infrastructure/Recovery/*` |
| Edit model | `Core/Models/OrganizerModels.cs` (`ModMetadataEdit`) |
| Snapshot + file-change model | `Core/Models/DryRunModels.cs` (`ProposalSnapshot.MetadataEdits`, `PenumbraWriteTargetKind`) |
| Organizer UI / folder seeding | `App/ViewModels/MainViewModel.cs` (`SeedExistingEmptyFolders`, `BuildBaseProposalSnapshot`) |

## How to verify

```powershell
dotnet build .\PenumbraOrganizer.sln
dotnet test .\PenumbraOrganizer.sln   # 129 tests
```

For real-data confidence without risk, the pattern used during this work was: copy
`sort_order.json` (+ the relevant `mod_data/<id>.json` and a single mod folder) into a temp
config/modRoot, point a `PenumbraInstallation` at the copy, and run scan → plan → prepare →
apply → rollback. Never write to the live config or `C:\Mods`.
