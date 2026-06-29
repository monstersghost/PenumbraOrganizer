# Handoff: sort_order.json retarget + metadata-editing engine

_Last updated: 2026-06-29. Supersedes the storage assumptions in the older docs (see "Stale docs" below)._

> **Status update (follow-up session, 2026-06-29):** Every item in "What is LEFT TO DO" below is
> now **DONE** and the test suite is green (134 tests). The original list is kept for context with
> per-item resolutions inline. See the git history on branch `retarget-sort-order-and-metadata`.

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

## What is LEFT TO DO  →  ALL DONE

### P1 — Metadata-editing UI ✅ DONE
The scanner now reads local data (`Favorite`/`LocalTags`/`Note` from `mod_data/<id>.json`).
`ModRowViewModel` tracks per-field edits (`BuildMetadataEdit` diffs against scanned values). A new
`ModMetadataDialog` plus an inline Favorite checkbox and an "Edits" indicator column edit all nine
fields. `BuildBaseProposalSnapshot` collects edits and passes them to `ProposalSnapshot`; the
snapshot identity folds edits in (so metadata-only changes invalidate a stale plan); edits persist
in `OrganizerSessionDocument.MetadataEdits` and restore on resume; metadata changes are surfaced in
the dry-run summary and Apply confirmation. Editing a placed mod's `Name` also rewrites its
`sort_order.json` display leaf (root mods are renamed by `meta.json` alone). Original spec:
- UI to edit per-mod `Favorite` / `LocalTags` / `Note` (local data) and
  `Name` / `Author` / `Description` / `Version` / `Website` / `ModTags` (author meta.json) —
  e.g. grid columns + an edit dialog.
- Collect edits into `IReadOnlyList<ModMetadataEdit>` and pass them when building the snapshot
  (`BuildBaseProposalSnapshot` → `new ProposalSnapshot(..., metadataEdits)`).
- Persist in-progress edits in the organizer session (`OrganizerSessionDocument`) so they survive
  restore, like folder proposals do.
- Surface metadata changes in the Review Changes / dry-run summary and the Apply confirmation.

### P1 — Confirm the Apply flow end-to-end ✅ DONE
Fixed two latent crashes that triggered the moment a plan had more than one file change (any
metadata edit): `BuildDryRunStatus` and `BuildApplyConfirmationMessage` used `SingleOrDefault()`
and now summarize all write targets. Smoke-launched the WPF app: clean startup through
`MainWindow shown` / `startup completed` with the new columns/dialog/command. The full engine path
(scan → plan → backup → apply → rollback across `sort_order.json` + `meta.json` + `mod_data/<id>.json`)
is covered by `MetadataEditingTests` against a temp fixture. _Not_ done: a click-through live Apply
against a real install (needs FFXIV closed + a real/copied Penumbra config).

### P2 — Update stale documentation ✅ DONE
All nine docs below were rewritten to the file-based model (plus `ORGANIZER_SESSION_FORMAT.md` for
the new `metadataEdits` field). Originally wrong:
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

### P2 — Remove the unused LiteDB dependency ✅ DONE
Removed the `LiteDB` PackageReference from the Infrastructure csproj and its
`THIRD_PARTY_NOTICES.txt` entry.

### P3 — Known limitations / edge cases
- **Authoritative-EmptyFolders footgun** ✅ documented as a contract note on `ProposalSnapshot.Folders`
  (Core/Models/DryRunModels.cs). Both production builders already seed existing empty folders;
  behavior is locked in by `DryRunAndApplyTests` (persist/drop/delete/rename) and
  `ControlledLiveTestAndRecoveryTests`.
- **meta.json `Name` vs display leaf** ✅ resolved: editing `Name` on a placed mod now also rewrites
  the `sort_order.json` leaf (root mods unchanged). See `PenumbraVirtualFolderWriter.MapPlanEntriesAsync`
  and `NameEdit_*` tests.
- **Absent `mod_data/<id>.json` when editing local data** ✅ resolved: `PenumbraMetadataWriter` uses a
  canonical empty baseline (`EmptyLocalDataJson`) and `ApplyService.EnsureWriteTargetsExist`
  materializes it before backup; rollback restores the baseline. See
  `LocalDataEdit_WhenModDataFileAbsent_*` test.
- **`DiagnosticExportService` `mod_data.db` sanitization** ✅ removed.
- **Legacy `mod_filesystem/organization.json`** — intentionally KEPT. The "ignored, non-authoritative"
  integration test (`OrganizationJson_IsIgnoredForAuthoritativeMapping`) and the controlled-test
  assertion are useful regression guards that no write target ever points at it; removing valid
  guards is not a net improvement.

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
