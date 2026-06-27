# Dry Run And Apply Format

## Authoritative write target

The first write-enabled milestone updates only one proven Penumbra structure:

* `mod_data.db`
* collection: `LocalModData`
* stable key: `_id`
* changed field: `Folder`

In the current project and fixtures, `_id` matches the installed top-level physical mod directory name. Penumbra Organizer uses that value as the stable mapping between an installed mod and its authoritative virtual-folder entry.

`mod_filesystem\organization.json` is currently treated as presentation data, not a proven source of truth for writable folder mapping. The app does not write it in this milestone.

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

Only supported writable entries produce `fileChanges`.

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

Planning never writes the live authoritative database.

For `mod_data.db` planning:

1. copy the live database to a temporary working file
2. update only `LocalModData.Folder` for approved stable IDs
3. preserve unrelated documents and unknown fields
4. validate record counts and unaffected rows
5. compute deterministic SHA-256 for the expected result
6. store the exact expected bytes in the plan

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

For `mod_data.db`:

1. write the expected bytes to a same-directory temporary file
2. open the temp copy with LiteDB
3. validate the targeted `LocalModData.Folder` values
4. flush to disk
5. atomically replace the live database
6. re-read the final file and verify the planned hash

## Post-Apply verification

Post-Apply verification checks:

* every completed target file hash equals the planned hash
* the authoritative database still parses
* every changed mod now matches the planned folder
* protected rows remain unchanged
* unchanged rows remain unchanged

Rollback becomes available only when Apply completed enough live writes to restore safely.

## UI availability

The Review Changes screen now exposes:

* `Create Dry Run`
* `Create Backup`
* `Apply Virtual-Folder Changes`

The guarded apply path is intentionally narrow:

* only supported `mod_data.db` virtual-folder changes
* no physical mod movement
* no collection editing
* no `.pmp` handling
* no option-group, enabled-state, or priority edits

The Backups screen remains read-only and shows rollback availability without exposing force restore.
