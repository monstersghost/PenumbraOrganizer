# Dry Run And Apply Format

## Authoritative write target

Apply updates one proven, file-based Penumbra structure (verified live):

* **`sort_order.json`** — authoritative virtual-folder organization.
  Shape: `{ "Data": { "<mod dir name>": "<full path incl. display leaf>" }, "EmptyFolders": [ ... ] }`.
  The `Data` value encodes both the containing folder (everything before the last `/`) and the
  mod's display/sort name (the final segment). `EmptyFolders` is authoritative for empty folders.

The scanner additionally *reads* (never writes) `<ModDirectory>\<mod dir name>\meta.json` (author
metadata: `Name`, `Author`, `Description`, `Version`, `Website`, `ModTags`) and
`mod_data\<mod dir name>.json` (per-user local data: `Favorite`, `LocalTags`, `Note`) for display.

The installed top-level physical mod directory name is the stable scan ID. The same string is the
physical folder name, the `sort_order.json` `Data` key, and the `mod_data/<id>.json` filename.

> There is **no** `mod_data.db` (LiteDB) file on a real Penumbra install. Earlier drafts of this
> project assumed one; the real format is the file-based layout above. The legacy
> `mod_filesystem\organization.json` is not authoritative and is not written.

A mod with no `sort_order.json` entry sits at the Penumbra root under its `meta.json` `Name`. A move
preserves the mod's existing `sort_order.json` display leaf so reorganization never silently renames
it in Penumbra.

## Dry-run plan

Each finalized dry run is immutable and contains:

* globally unique `planId`
* UTC creation time
* app version
* installed Penumbra version
* scan identity
* installation identity
* organizer-session identity
* proposal snapshot identity
* organization preferences snapshot
* authoritative source-file snapshots and schema fingerprints
* exact per-mod mapping entries
* exact per-file expected output hashes and bytes
* validation result
* summary counts
* warnings

Only supported writable entries produce `fileChanges`. Apply currently produces a single
`fileChange` for `sort_order.json` (`WriteTargetKind.SortOrderJson`). The plan/backup/apply model
still supports multiple `fileChanges` per operation, leaving room for future multi-file writes.

Protected rows never produce writable operations.

## Plan invalidation

The plan is stale when any of these change:

* proposal snapshot
* protection state
* organization preferences
* installed Penumbra version
* authoritative source-file hash
* authoritative schema fingerprint
* installation identity
* current folder state
* targeted mod resolution

The Review Changes UI shows:

`Your plan is out of date. Create a new dry run before applying changes.`

## Expected-result generation

Planning never writes the live authoritative files.

For the `sort_order.json` target:

1. round-trip the live JSON through a `JsonNode` so unknown keys are preserved
2. apply only the approved changes (the mod's `Data` entry / `EmptyFolders`), preserving the
   display leaf
3. validate that unrelated entries are byte-for-byte unchanged
4. compute deterministic SHA-256 for the expected result
5. store the exact expected bytes in the plan

## Permission preflight

Preflight checks only the exact write targets in the plan plus the organizer backup root.

It verifies:

* readable source target
* non-read-only target
* exclusive-lock availability
* same-directory temp-file create/flush/delete
* atomic replacement support
* backup-root writability
* available disk space
* blocking FFXIV/XIVLauncher process detection

The app runs with `asInvoker` and does not request administrator elevation automatically.

## Backup integration

Apply preparation runs this sequence:

1. finalize dry run
2. create operation ID
3. create verified backup package for every planned writable file
4. verify backup package
5. persist rollback transaction with original and expected applied hashes
6. persist `plan.json` and `apply.json`
7. enable Apply only when all checks pass

## Atomic Apply

Before each write:

1. re-read the live source file
2. recompute SHA-256
3. confirm it matches the dry-run source hash
4. confirm the Penumbra version still matches
5. confirm the backup package is verified

For each target JSON file:

1. write the expected bytes to a same-directory temporary file
2. flush to disk
3. atomically replace the live file
4. re-read the final file and verify the planned hash

## Post-Apply verification

Post-Apply verification checks:

* every completed target file hash equals the planned hash
* when a `sort_order.json` change was applied, the file still parses, every changed mod now
  matches the planned folder, protected rows remain unchanged, and unchanged rows remain unchanged

Rollback becomes available only when Apply completed enough live writes to restore safely.

## UI availability

The Review Changes screen now exposes:

* `Validate My Installation`
* `Create Dry Run`
* `Create Backup`
* `Controlled Test Apply`

`Validate My Installation` is explicitly user-authorized and read-only unless the user separately chooses to create a verified backup later. It:

* rescans the installation
* remaps authoritative records
* creates a fresh dry run
* runs exact-target permission checks
* reports whether the installation currently appears safe for Apply

The guarded apply path is intentionally narrow:

* only supported `sort_order.json` virtual-folder changes
* no metadata editing (edit metadata in-game)
* no physical mod movement
* no collection editing
* no `.pmp` handling
* no option-group, enabled-state, or priority edits

The first real-installation live test now uses a controlled selection layer before dry-run creation:

* the user explicitly chooses the mods
* the default limit is 3 mods
* the default test folder is `PenumbraOrganizer Test`
* protected, ambiguous, and unsupported rows are excluded
* unselected proposals are reset to their current folders in the controlled snapshot

Before any live write, the confirmation modal must state:

* selected mod count
* current folders
* proposed folders
* authoritative target (`sort_order.json`)
* verified backup location
* rollback readiness
* that physical mod files will not move
* that FFXIV must be closed
* that the workflow is an alpha test

The Backups screen now exposes guarded rollback only for operations with:

* a completed or partially completed Apply
* a verified backup
* a persisted rollback transaction

Rollback verifies current hashes first, skips conflicts by default, and never moves physical mod files.
