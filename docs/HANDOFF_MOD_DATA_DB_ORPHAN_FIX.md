# Handoff: mod_data.db-orphaned mod crashes Backup and Apply

_Started and fixed: 2026-07-08. Status: fix applied and tested here, not yet confirmed on the
real Linux install that surfaced it. Continue debugging Linux in a new conversation._

## The bug report

Found while a Linux user (the same person from
`docs/HANDOFF_LITEDB_MANUAL_SELECTION_FIX.md`, now testing the merged fix for that issue) was
confirming Backup and Apply worked after the LiteDB `PluginAssemblyPath` fix. The LiteDB issue
is resolved — Apply completed and version detection now works correctly on their machine — but
a new, unrelated error surfaced during that same testing pass:

```
The operation could not be completed safely.
mod_data.db has no LocalModData document for ,Thor - K.D.O.
```

This blocked the *entire* Backup and Apply batch (80 pending changes across 228 scanned mods),
not just the one affected mod. Screenshot evidence showed the error firing from the "Review
Changes" screen when clicking "Backup and Apply".

## Root cause

`,Thor - K.D.O.` is a real mod directory name (the leading comma is literal, not a formatting
bug — confirmed by tracing `entry.RecordKey` back to `mod.StableScanId`, the physical directory
name). The mod exists on disk and scans successfully, but Penumbra's `mod_data.db` (a LiteDB
database) has **no `LocalModData` document for it at all** — Penumbra itself never registered
this mod in its database, even though the folder is present.

`ModDataDbVirtualFolderWriter`'s own class doc comment states an assumption that turned out to
be false in this real case: "every installed mod already has a `LocalModData` document... moving
one is just overwriting its `Folder` value." Because `PenumbraModDataDb.GetFolderFor` (used by
`CurrentFolderFor`) returns `string.Empty` for a missing key rather than signaling "no entry,"
this mod scanned as sitting at the root/unassigned folder like any other unplaced mod, and got
proposed for a move like normal. At apply time, `ModDataDbVirtualFolderWriter.ApplyFolderUpdates`
looked up the mod's document by ID (`collection.FindById(entry.RecordKey)`) to update its
`Folder` field, got `null`, and threw — aborting the whole batch, including 79 other mods with
perfectly valid changes.

This is likely related to the same kind of `mod_data.db` drift already seen as scan warnings on
this same install ("mod_data.db references a mod folder that is missing on disk" — the *inverse*
direction: entries with no folder, vs. this bug's folder with no entry). Root cause of *why*
Penumbra's own database ends up in this state is outside this app's control; this fix makes the
app handle it gracefully rather than crash.

## Fix applied

Two commits on `worktree-mod-data-db-orphan-fix` (based on `main` post-PR#9, i.e. includes the
full mod-category overhaul and the LiteDB fix):

1. **`151f2b9`** — `PenumbraOrganizer.Infrastructure/Apply/ModDataDbVirtualFolderWriter.cs`:
   `MapPlanEntriesAsync` now checks `state.Data.GetEntry(mod.StableScanId) is not null` (not
   `GetFolderFor`, which can't distinguish "no document" from "document with an empty folder")
   before deciding a mod requires a write. A mod with no document is excluded from the write plan
   — same treatment as an already-protected mod — with a warning: *"mod_data.db has no record
   for this mod yet. Open it in Penumbra once, then re-scan, before it can be reorganized here."*
2. **`0ee7a21`** — same file: the orphaned mod's `DryRunPlanEntry.ValidationStatus` is downgraded
   from `ValidChange` to the existing `OrganizerRowStatus.NeedsReview` (only when it would
   otherwise have had a real, writable change — an orphan that wasn't being moved anyway is left
   alone). Without this, `DryRunPlanner`'s `ChangedRowCount` and `MainViewModel`'s "All target
   records mapped" apply-checklist line (both keyed on `ValidationStatus == ValidChange`) would
   have shown a false failure/miscounted the orphan as an applied change even though apply
   completed successfully.

### Test added

`PenumbraOrganizer.Tests/Apply/DryRunAndApplyTests.cs`:
`Apply_ModDataDb_ModWithNoDocument_IsExcludedFromWriteWithWarning_OtherModsStillApply` —
reproduces the exact crash (confirmed RED before the fix, same exception message as the report),
then verifies end-to-end: plan creation succeeds, the orphan is excluded with a warning and
reported as Needs Review, the other mod's valid change still applies and persists, summary counts
stay consistent (`ChangedRowCount`/`AffectedModCount` both 1, not 1/0), and the orphan's document
is confirmed still absent after apply (not fabricated).

Verified: 198/198 tests pass (full solution), no regressions.

### Independent review

Dispatched a full review (opus) before pushing. Confirmed: the exclusion is genuinely data-safe
across all three paths that touch `mod_data.db` (dry-run byte computation, live apply replay, and
post-apply verification) — the orphan is never looked up or written in any of them. No Critical
findings. One Important finding (the checklist/count inconsistency) was found and fixed in the
second commit before push. Two Minor suggestions not yet acted on:
- Confirm the orphan's warning text actually surfaces on a row the user reads in the Review
  Changes screen (not just indirectly via the checklist/status change) — plausible given
  `NeedsReview` rows are shown, but not explicitly re-verified after the second commit.
- Scope check confirmed correct: `PenumbraVirtualFolderWriter` (the `sort_order.json` backend)
  does not share this bug — it creates a missing entry rather than requiring one to already exist,
  so this fix is correctly scoped to the `mod_data.db` backend only.

## What's NOT done yet

- **Not confirmed on the real Linux install that reported this.** Everything above was verified
  via the automated test suite and code review on Windows; the reporter has not yet re-tested
  with this fix.
- The mod_data.db drift itself (why Penumbra's own database is missing/has extra entries relative
  to disk on this install) is not investigated or fixed — out of scope, Penumbra's own data
  hygiene, not this app's bug.
- PR not yet created for this fix as of this handoff — see "Next steps."

## Next steps for whoever picks this up

1. Check whether a PR already exists for branch `worktree-mod-data-db-orphan-fix` (it may have
   been created in the same session that wrote this handoff, shortly after). If not, one is ready
   to open — the branch is pushed, tests pass, and a draft description was already approved.
2. Get confirmation from the Linux reporter that this specific fix resolves their crash.
3. Watch for whether they hit further issues in the same testing pass — this is the second
   distinct bug found in one confirmation session (after the LiteDB `PluginAssemblyPath` fix), so
   a third might surface before they're fully unblocked.
4. If the Discord release-announcement workflow (`.github/workflows/discord-release.yml`) becomes
   relevant to this work (e.g. a beta build gets released for the reporter to test more easily
   than building from source), remember it pings a Discord role and posts the release body
   verbatim — see the discussion in this session's history for details, not repeated here.

## Key files (orientation)

| Concern | File |
|---|---|
| The actual fix | `PenumbraOrganizer.Infrastructure/Apply/ModDataDbVirtualFolderWriter.cs` (`MapPlanEntriesAsync`) |
| Regression test | `PenumbraOrganizer.Tests/Apply/DryRunAndApplyTests.cs` (`Apply_ModDataDb_ModWithNoDocument_...`) |
| mod_data.db read/entry lookup | `PenumbraOrganizer.Infrastructure/Penumbra/PenumbraModDataDb.cs` (`GetEntry` vs `GetFolderFor`) |
| Apply-checklist UI consumer | `PenumbraOrganizer.App/ViewModels/MainViewModel.cs` (~line 2253, "All target records mapped") |
| Prior related handoff (LiteDB, resolved) | `docs/HANDOFF_LITEDB_MANUAL_SELECTION_FIX.md` |
