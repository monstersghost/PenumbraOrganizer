# Real fix for orphaned empty folders: read + prune `organization.json`

Status: design approved 2026-07-09, not yet implemented.
Builds on: `docs/KNOWN_ISSUE_EMPTY_FOLDERS_AFTER_RESORT.md` (root cause). This spec is the "real
fix" that doc's Open Questions section asked for.

## Why

`docs/KNOWN_ISSUE_EMPTY_FOLDERS_AFTER_RESORT.md` traced the empty-folder complaint to Penumbra's
own `mod_filesystem/organization.json`: Penumbra writes an entry for every folder node that has
*ever* existed in its live in-memory tree, with no filtering for emptiness, and recreates every
one of those entries on every load, forever. This app has never read or written that file
(enforced today by `OrganizationJson_IsIgnoredForAuthoritativeMapping_AndNoWriteTargetsPointToIt`
in `PenumbraOrganizer.Tests/Integration/ValidationAndImportTests.cs`), so it has no way to clean
up the folders it — indirectly, via mod moves — causes to become orphaned.

This spec adds detection and, with explicit user review, pruning of orphaned entries in
`organization.json`.

## Ground truth (verified against real source, not guessed)

Read directly from `Ottermandias/Luna` (`Luna/Filesystem/FileSystemSaver.cs`, `main` branch) and
`xivdev/Penumbra` (`Penumbra/Files/FilenameService.cs`,
`Penumbra/Mods/Manager/ModFileSystemSaver.cs`, `stable` branch), and cross-checked against the
strings embedded in this machine's installed `Penumbra.dll`/`Luna.dll` (v1.6.1.10).

- Real path: `FilenameService.FileSystemOrganization` = `<config dir>/mod_filesystem/organization.json`.
- Real schema (`FileSystemSaver.Organization`, `CurrentVersion = 1`):
  ```json
  {
    "Version": 1,
    "Folders": {
      "<full folder path>": {
        "ExpandedColor": 4294901760,
        "CollapsedColor": null,
        "SortMode": "FoldersFirst",
        "IsSeparator": false
      }
    },
    "Separators": {
      "<full path>": { "Folder": false, "Color": null, "CreationDate": 638123456789 }
    }
  }
  ```
  All fields on `FolderData`/`SeparatorData` are optional (`uint?`/`string?`/`bool?`) and omitted
  from the JSON when unset — an entry with nothing customized serializes as `{}`.
- `OrganizationData.Save` (Luna) writes one object per `FileSystemFolder` currently in the live
  tree, unconditionally — **confirmed**, no emptiness filter exists anywhere in this path.
- `HandleOrganization` (load) calls `FindOrCreateAllFolders` for every key in `Folders`,
  unconditionally — **confirmed**, this is what recreates orphaned folders forever.
- `organization.json` is only saved on *live* tree-mutation events (`FolderAdded`,
  `FolderChanged`, `SeparatorAdded/Changed`, folder rename/remove, certain moves, `Reload`) — not
  during Penumbra's own startup folder reconstruction (`CreateDataNodes` runs with the
  change-listener explicitly unsubscribed). Some installs (e.g. this dev's own — confirmed empty
  `mod_filesystem` apart from `expanded_folders.json`/`selected_nodes.json`) may have no
  `organization.json` at all yet. That's a valid, common state, not an error.
- Mod *placement* (`mod.Path.Folder`) is unrelated to this file and keeps working exactly as
  today — `organization.json` only tracks folder-node metadata/structure, never mod membership.
- Penumbra's own internal backup (`FilenameService.GetBackupFiles()`) already includes
  `FileSystemOrganization` alongside `mod_data.db` — Penumbra itself treats this file as
  important, non-disposable state.

## Decisions

- **Ambition: full automated prune**, not just detection + manual-cleanup guidance. The app reads
  `organization.json`, computes which entries are orphaned, and — with user confirmation — writes
  the pruned file back.
- **Trigger: reviewed step in Apply**, not silent and not a fully separate tool. Detection is
  automatic; the specific folders to delete are surfaced for the user to see and adjust before
  they're included in that Apply. Matches every other write path in this app (dry run → verified
  backup → explicit confirmation).
- **Customized-but-empty folders are surfaced, flagged, not silently excluded.** A folder with a
  custom color/sort-mode/separator that also happens to be empty is shown separately with a
  stronger warning, unchecked by default — the user can still choose to prune it, just with more
  friction than a plain empty folder.
- **Rollout: staged through Controlled Live Test first.** This is a brand-new write target for a
  file this app has never touched, verified against one real install plus source — not yet
  battle-tested across the range of Penumbra versions in the wild. It ships gated behind the
  existing `ControlledLiveTestService` opt-in/capped workflow and only gets promoted to the
  default Backup & Apply flow after real-install validation, the same path originally used to
  trust the `sort_order.json` writer.

## Architecture

`organization.json` pruning is a **second, independent write target** in the same dry run/apply
cycle, orthogonal to which mod-placement backend (`sort_order.json` vs `mod_data.db`) is active —
orphaned folders can accumulate under either, since the mechanism lives entirely in Penumbra's
own live folder tree, not in either placement store.

- **New read model** `PenumbraOrganizationJson` (mirrors `PenumbraSortOrder`), in
  `PenumbraOrganizer.Infrastructure/Penumbra/`. Parses the schema above. Hard version gate:
  `Version != 1` → treat as unsupported, skip the whole feature for that install (fail closed,
  same posture as `PenumbraSortOrder`'s schema-difference handling). Missing file → valid empty
  state. Malformed JSON → same fail-closed skip.
- **New interface** `IOrganizationCleanupPlanner`, deliberately *not* an extension of
  `IPenumbraVirtualFolderWriter` — that interface's `MapPlanEntriesAsync` returns per-mod entries
  keyed by `StableScanId`, which doesn't fit a folder-level prune operation. Takes the *proposed*
  (post-apply) occupied-folder set and the parsed `organization.json`, returns candidate prunable
  folders split into `PlainEmpty` / `CustomizedEmpty`. Occupancy check reuses the same
  equal-or-prefix logic as `PenumbraVirtualFolderWriter.IsFolderOccupied` — a folder counts as
  occupied if it equals or prefixes any mod's proposed folder (so a folder with only occupied
  descendants is never flagged as prunable, even if nothing occupies that literal path).
- **`DryRunPlanner`** is extended to optionally compose a second contributor alongside the primary
  `IPenumbraVirtualFolderWriter`, appending its `DryRunFileChange` (new
  `PenumbraWriteTargetKind.OrganizationJson`, `TargetPath` = `organization.json`) into the same
  `plan.FileChanges` list.
- **No structural changes needed** to `ApplyService`, `WritePermissionPreflightService`, or the
  core backup/rollback write path — all three already operate generically over
  `plan.FileChanges`/`transaction.Files` keyed by target path (verified by reading
  `WritePermissionPreflightService.CheckAsync`'s `foreach (var fileChange in plan.FileChanges)`
  loop and `RollbackService.ExecuteAsync`'s per-file loop). Process-lock and disk-space preflight
  checks cover `organization.json` automatically once its file change is in the plan — no new code
  needed for that specifically.
- **`BackupService`'s file enumeration must be checked during implementation** — if it derives its
  file list from `plan.FileChanges` the same way, this is also free; if it's hardcoded to today's
  two known targets, it needs the same generic treatment.

## New UI: "Folder Cleanup" tab

Inserted between the existing **Proposed Changes** and **Review Changes** tabs (Review Changes is
where `BackupAndApplyCommand`/dry run/Apply live):

Home/Scan → Sort Method → Current Mods → Proposed Changes → **Folder Cleanup (new)** → Review
Changes → Backups → Settings

- Populated whenever proposals/folders are rebuilt (scan, strategy switch, manual edits) so it
  always reflects the *current* proposed plan.
- Occupancy computed from the **proposed** (post-apply) folder set, not the pre-apply scan
  snapshot — a folder about to be vacated by this apply must show as orphaned; a folder about to
  be newly occupied by this apply must not.
- Two collapsible, counted sections:
  - **Empty, no customization** — pre-checked by default.
  - **Empty but customized** — unchecked by default; shows what's set (e.g. "custom color, sort
    mode: FoldersFirst").
- Select-all/select-none per section, live counts, text filter (report #1's scenario had
  thousands of folders — no flat unchecked list).
- If `organization.json` is absent or fails the version gate: "Nothing to clean up" /
  "unsupported version, skipped" — informational, not an error, doesn't block anything downstream.

## Data flow

1. Scan → inventory (mods, current folders) as today.
2. Proposed Changes → per-mod destinations computed as today; this produces the post-apply
   occupied-folder set.
3. Folder Cleanup tab → reads `organization.json`, diffs against that occupied set, user
   checks/unchecks. The **confirmed selection**, not the full detected set, is what carries
   forward.
4. Confirmed selection is added to `ProposalSnapshot` as a new field,
   `OrganizationCleanupSelections: IReadOnlyList<string>` — same pattern `Folders` already uses to
   carry the confirmed empty-folder set for `sort_order.json`.
5. Review Changes → `CreateDryRunAsync` composes both contributors. The cleanup planner
   **re-verifies every confirmed path at this point**: still present in `organization.json`, still
   unoccupied under the final proposal set, and (for plain-empty selections) still uncustomized.
   Anything stale is silently dropped from the write with a warning line, not a hard failure — the
   same discipline `PenumbraVirtualFolderWriter` already applies by re-deriving emptiness from
   live proposals rather than trusting a passed-in flag. A broken/missing/version-mismatched
   `organization.json` degrades the same way: skip cleanup, mod moves proceed unaffected.
6. New `PlanInvalidationReason.OrganizationCleanupSelectionChanged` invalidates an existing dry run
   if the user revisits Folder Cleanup and changes selections afterward — same mechanism
   `ProposalChanged`/`ProtectionChanged` already use.
7. Apply → the cleanup contributes one more `DryRunFileChange` into the same flat list
   `ApplyService`/`BackupService`/`RollbackService`/`WritePermissionPreflightService` already
   iterate generically — verified backup, atomic write, post-apply hash verification, and rollback
   all fall out of existing machinery.

## Error handling

Every failure mode for this feature degrades to "skip cleanup, don't touch `organization.json`,
mod moves proceed" — it's strictly additive, never a precondition for the rest of Apply:

- File absent → no-op.
- `Version != 1` → unsupported, skip, log.
- Malformed JSON → skip, log.
- Content changed on disk since Folder Cleanup computed its list (e.g. Penumbra ran and re-saved
  it) → caught by the existing hash/schema-fingerprint staleness checks
  (`SourceFileHashChanged`/`SchemaFingerprintChanged`), forcing a fresh dry run. No new mechanism.
- Process-lock / disk-space preflight: already covered generically (see Architecture).

## Backup and separate restore

- `organization.json` is captured in the same verified backup as the other target files before
  Apply is enabled (falls out of the generic `plan.FileChanges` handling, pending the
  `BackupService` enumeration check noted above).
- **Separate, independent restore.** `RollbackExecutionOptions` gets a new field,
  `OnlyTargetPaths: IReadOnlySet<string>?` (sibling to the existing `ForceRestoreTargets`) — when
  set, `RollbackService.ExecuteAsync` restores only matching targets and marks the rest `Skipped`
  ("Not included in this restore selection"). Additive and backward-compatible: `null` keeps
  today's full-transaction restore behavior.
- Backups tab UI: the existing per-operation `AffectedFiles` grid (Target / Backup file / Type /
  JSON status) gets a **"Restore Selected File Only"** button next to the existing "Restore
  Backup" button, enabled when a row is selected, calling rollback with
  `OnlyTargetPaths = { that row's TargetPath }`.
- When the selected row's target is `organization.json`, this action is gated behind a
  confirmation dialog with this specific warning: *"This restores Penumbra's folder
  structure/colors from the backup, independently of your mod placements. If this backup predates
  a folder cleanup, restoring it will bring back the orphaned folders that cleanup just removed.
  Mod positions won't be reverted unless you also restore those files."*

## Testing

- `PenumbraOrganizationJson` read model: version gating, malformed input, real-schema fixtures
  (replace the fabricated stub JSON in `TemporaryPenumbraFixture.WriteOrganizationJson` with the
  real shape now that it's confirmed from source), byte-stable round-trip for untouched entries.
- Cleanup-diff logic: occupied / plain-empty / customized-empty classification; prefix-based
  occupancy (folder with an occupied descendant is never prunable, even if nothing occupies that
  exact path).
- **Rewrite** (not just revisit — it will start failing by design)
  `OrganizationJson_IsIgnoredForAuthoritativeMapping_AndNoWriteTargetsPointToIt` in
  `PenumbraOrganizer.Tests/Integration/ValidationAndImportTests.cs` to assert the new correct
  behavior: `organization.json` is never a write target with zero confirmed cleanup selections,
  and is a write target only when the user actually confirmed prunes.
- Backup/rollback round-trip test for a pruned `organization.json`, including the new
  `OnlyTargetPaths` partial-restore path.
- Version-mismatch / missing-file no-op tests.
- Real-install validation through the Controlled Live Test gate (this machine's v1.6.1.10 at
  minimum) before promotion to default Apply.

## Open risks / follow-ups for the implementation plan

- Confirm `BackupService`'s file enumeration is already generic over `plan.FileChanges` (assumed
  by analogy with `ApplyService`/`WritePermissionPreflightService`, not yet directly verified).
- Schema confirmed against `stable`/`main` branch source plus one real installed version
  (1.6.1.10). Other Penumbra versions in the wild are not yet verified — this is exactly why
  rollout is staged through Controlled Live Test rather than shipping straight to default Apply.
- `Separators` entries are read but never targeted for pruning by this spec (separators are
  visual dividers, not folders, and weren't part of the reported symptom) — confirm the writer
  leaves the `Separators` object byte-for-byte untouched when only `Folders` entries change.
