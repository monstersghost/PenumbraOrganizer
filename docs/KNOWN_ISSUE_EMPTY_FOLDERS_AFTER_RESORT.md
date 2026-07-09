# Known Issue: Empty folders left behind after re-sorting or rollback

_Investigated 2026-07-09. **Fixed and validated on a real install as of 2026-07-09** — see
[Fix and validation](#fix-and-validation) below. This doc records the root cause and the fix so
neither needs to be re-derived next time someone picks this up._

## User-facing summary

**What happens:** After sorting mods one way (e.g. by Creator) and later switching to a different
scheme (e.g. by Type), or after using Backup and Rollback to undo a change, old folders can be left
behind in Penumbra's mod list — empty, with nothing in them. Fully closing and reopening the game
does not clear them. They also do not appear as folders inside Penumbra Organizer's own Proposed
Folder tree, so there's currently no way to select and delete them from within the app either.

**Discord-ready version:**

> Yeah, this is a real thing, not something you're doing wrong. When you sort by one method then
> switch to another (or roll back a change), Penumbra can leave the old empty folders sitting there
> in your mod list. Restarting the game doesn't fix it, and right now the app can't clean them up
> either.
>
> Turns out Penumbra keeps its own memory of every folder it's ever made, separate from what's
> actually organized. It only forgets a folder if you delete it yourself in Penumbra. The organizer
> moves your mods around just fine, it just doesn't have a way to clean up that leftover folder list
> yet.
>
> **For now:** just manually delete the empty ones in Penumbra (right-click → delete). Annoying, I
> know — looking into a real fix, it's just going to take some care since it touches a file that also
> stores your folder colors/sort settings.

## Reports that surfaced this

1. User sorted "Creator then Type" (thousands of folders, one per creator — expected, just a lot),
   then switched to "Type then Creator" and reapplied. The old per-creator folders remained, empty,
   in Penumbra.
2. Separately: after reverting a Backup and Apply via this app's Rollback feature, folders created by
   the reverted sort stayed behind, still empty.
3. A third report (via Discord) confirmed they were fully closing the game between each sort attempt
   — ruling out "Penumbra just hasn't reloaded" as the explanation — and noted the empty folders
   never showed up as options in Penumbra Organizer's own Proposed Folder tree.

## Root cause

Confirmed by reading Penumbra's actual source (`stable` branch,
[xivdev/Penumbra](https://github.com/xivdev/Penumbra) and its
[Ottermandias/Luna](https://github.com/Ottermandias/Luna) filesystem library). Penumbra has moved
past the file format this app was originally built against:

- `sort_order.json` is now literally named `OldFilesystemFile` in Penumbra's own source
  ([FilenameService.cs](https://github.com/xivdev/Penumbra/blob/stable/Penumbra/Files/FilenameService.cs)).
  It is read **exactly once** — the first time a modern Penumbra starts after seeing it — to migrate
  old data in, then it's renamed to `sort_order.json.bak` and never read again
  (`FileSystemSaver.MigrateOldFileSystem` in
  [Luna/Filesystem/FileSystemSaver.cs](https://github.com/Ottermandias/Luna/blob/main/Luna/Filesystem/FileSystemSaver.cs)).
- Mod *placement* (which folder a mod lives in) is still read fresh from `mod_data.db`'s `Folder`
  field on every load (`ModFileSystemSaver.CreateDataNodes`), so this app's writes to
  `mod_data.db` / (indirectly, via the one-time migration) `sort_order.json` do still move mods
  correctly.
- Folder *structure and metadata* — which folders exist, their colors, sort mode, separators — is
  tracked separately in `mod_filesystem/organization.json`. On load, Penumbra reads this file's
  `Folders` dictionary and calls `FindOrCreateAllFolders` for **every key in it, unconditionally**
  (`FileSystemSaver.HandleOrganization`). On save, it writes out **every folder node currently in the
  live tree with no filtering for whether it's empty** (`FileSystemSaver.OrganizationData.Save`).
- Nothing in Penumbra's own code path ever removes an entry from `organization.json` just because a
  folder lost its last mod. The only way a folder leaves that file is an explicit user delete inside
  Penumbra's own UI.

In short: `organization.json` is an ever-growing list of "every folder that has ever existed," and
Penumbra faithfully recreates all of them on every load, forever, regardless of whether anything is
in them.

### Why this explains every symptom

- **Restarting the game doesn't help** — the file on disk is what's perpetuating the folders. A
  fresh load just recreates whatever it lists.
- **They don't appear in Penumbra Organizer's Proposed Folder tree** — this app has never read
  `organization.json`. For the `mod_data.db` backend, the app's empty-folder reader already hard-codes
  an empty list with a comment noting this is out of scope
  ([PenumbraModDataDb.cs](../PenumbraOrganizer.Infrastructure/Penumbra/PenumbraModDataDb.cs)).  For the
  `sort_order.json` backend, once Penumbra migrates and renames it to `.bak`, the file this app reads
  is frozen at that one-time snapshot and no longer reflects Penumbra's live folder state.
- **Rollback doesn't clear them either** — rollback only restores files this app actually backed up
  (`sort_order.json` or `mod_data.db`). `organization.json` was never part of that backup, so whatever
  it lists survives a rollback untouched.

## Why this app couldn't fix it before

This app previously had an explicit, documented promise — in `README_FOR_USERS.txt` and several docs
(`SAFETY_AND_ROLLBACK.md`, `DRY_RUN_AND_APPLY_FORMAT.md`, `HANDOFF_SORT_ORDER_AND_METADATA.md`) — that
it never writes `organization.json`. A real fix meant reading and writing that file, which also
stores the user's folder colors, sort modes, and separators — a materially bigger and riskier change
than anything else in the write path at the time, with no existing tests or format documentation for
it in this codebase. That gap has now been closed; see below.

## Fix and validation

A **Folder Cleanup** feature now reads `organization.json`, classifies every folder as occupied or
orphaned (empty, with or without color/sort-mode/separator customization reusing the same
equal-or-prefix occupancy logic as the existing write path), and lets the user select specific
orphaned folders to prune. Selected folders become a real write target
(`PenumbraWriteTargetKind.OrganizationJson`) that goes through the same dry run → verified backup →
Apply → rollback pipeline as every other change this app makes, with its own safeguards:

- A hard cap of 3 folders pruned per Apply.
- The Apply confirmation dialog lists the exact folder paths that will be pruned.
- A post-Apply observation prompt asks the user to confirm in Penumbra itself whether the folder(s)
  actually disappeared (immediately or after reload/restart).
- The Backups tab can restore `organization.json` on its own, independent of other files, with an
  explicit warning that restoring it can reintroduce the orphaned-folder symptom this feature fixes.
- Selecting only Folder Cleanup entries (no mod moves — e.g. with the "Preserve" strategy) still
  produces a valid, appliable plan; the "nothing to apply" gate checks the write plan's file changes,
  not proposed mod moves specifically.

**Validation (2026-07-09):**

- On the original investigation machine (~220 mods): the live repro steps (sort one way, switch
  scheme, reapply) did not reproduce new orphaned folders under v0.3.2 — and no `organization.json`
  existed on this install at all, before or after. This install's usage pattern apparently never
  exercises the Penumbra code path that creates the file in the first place, so it couldn't serve as
  a positive test case. Consistent with the reports below all coming from much larger libraries.
- On a second real install (~2,000+ mods, the scale the original reports came from): the same live
  repro steps still didn't produce *new* orphaned folders, but the Folder Cleanup tab correctly
  detected multiple **pre-existing** orphaned entries already in that install's real
  `organization.json`. A subset was selected, taken through a Preserve-strategy dry run, verified
  backup, and Apply. The selected folders disappeared from Penumbra as expected, and rollback via the
  Backups tab correctly restored them. This confirms the read/classify/write/rollback path is correct
  and safe against a real, large, organically-grown `organization.json` — even though the *original*
  live-creation trigger still hasn't been reproduced on demand.
- Because live creation of new orphans wasn't reproduced on either machine, the safety cap and
  observation-prompt gate stay on rather than being promoted to "off" — that's a decision for once
  more real installs (ideally one that can reproduce live creation) have exercised this path.
