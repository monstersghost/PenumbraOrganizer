# Safety And Rollback

Penumbra Organizer must be rollback-first. No live write feature should be enabled until the project has a verified backup engine, immutable dry-run plan, rollback transaction model, conflict detection, exact-byte restoration, and post-rollback verification.

The organizer is read-only until:

- a scan succeeds
- protected-path validation passes
- dry-run validation passes
- a verified backup completes

The current repository now includes a guarded Apply foundation for this proven file-based target:

- `sort_order.json` (virtual-folder organization; `Data` entries + `EmptyFolders`)

Physical mod folders, collections, option groups, priorities, enabled states, `.pmp` files, plugin binaries, and FFXIV files remain outside this write path.

The recovery foundation is now implemented for future Apply integration:

- verified backup creation of the entire Penumbra configuration directory
- immutable backup manifests
- rollback transaction persistence
- exact-byte rollback restore with conflict detection
- post-rollback verification
- read-only Backups screen foundation

This foundation remains fixture-tested and does not expose live Apply or public rollback execution in the current alpha UI.

The first live write path now adds:

- immutable dry-run planning
- exact expected-result generation for `sort_order.json`
- verified-backup integration before Apply (the full Penumbra configuration directory, not just the file(s) being written)
- atomic file replacement for supported virtual-folder changes
- post-Apply verification
- guarded rollback availability after Apply
- controlled live-test selection before the first real Apply
- post-Apply Penumbra UI observation capture without widening the write target

## Absolute boundaries

- no FFXIV game files are modified
- no physical mod folders are moved
- no physical mod directories are renamed
- no mod assets are rewritten
- no `.pmp` packages are handled
- no collections are modified
- no legacy `mod_filesystem\organization.json` writes are performed
  Source of truth: `sort_order.json` (`Data` entry = folder + display leaf; `EmptyFolders` = empty folders)
  The legacy `organization.json` is not authoritative and is not written.
- scanning remains read-only
- in-memory proposals do not write to Penumbra
- protected items are immutable
- Apply remains disabled until dry run, backup, write, verification, and rollback are complete

## Protected content

Protected mods and protected folder prefixes are immutable during apply.

For protected rows:

- current virtual folder must equal proposed virtual folder
- no creator normalization is applied
- no file is written
- no path is flattened or merged

## Backup contents

Before Apply can exist, the app must create a verified backup under:

`%LocalAppData%\PenumbraOrganizer\Backups\<operation-id>\`

The backup includes:

- only metadata files that will be written
- operation ID
- application version
- Penumbra version
- scan identity
- affected file list
- original file hashes
- backup file hashes
- byte lengths
- backup paths stored safely, preferably relative within the operation package
- backup manifest
- rollback transaction record
- dry-run plan reference
- version and schema snapshot

Large mod assets are not duplicated unless a future feature would modify those files directly.

Backup verification must check:

- every required source file exists before backup
- every backup file exists after copy
- original and backup SHA-256 hashes match
- byte lengths match
- JSON parses when applicable

Future Apply must abort when backup verification fails.

The current package format is documented in:

`docs/BACKUP_AND_ROLLBACK_FORMAT.md`

## Rollback

Rollback uses the saved manifest, backup files, and rollback transaction rather than the current workbook, current imported proposal, current organizer session, current display names, or a new live scan.

Rollback is available only after a prepared Apply operation completes enough verified live writes to restore safely.

Rollback records must include:

- operation ID
- application version
- Penumbra version
- scan identity
- affected file list
- original hashes
- applied hashes
- backup paths
- original and proposed virtual folders
- completed operations
- skipped operations
- failed operations
- rollback status
- post-Apply verification status

Rollback restores exact backed-up bytes.

For each file restore:

- current hash is checked first
- mismatches produce a warning instead of blind overwrite
- restored files are revalidated when possible

## Rollback conflict behavior

- If the current hash equals the expected applied hash, automatic restore is safe.
- If the live file is missing, restore only when the manifest identifies a previously existing target and the backup is valid.
- If the current hash differs, mark a conflict and do not overwrite by default.
- If the backup is missing or corrupt, block restore.
- If the user chooses force restore in a future build, require Advanced-only explicit confirmation and fully log the action.

Rollback writes must use temporary files, format validation, flushing, atomic replacement where supported, and post-rollback verification.

Partial rollback must be reported clearly. Re-running rollback should be resumable or safely repeatable.

For the first real-installation alpha test, the app now defaults to a controlled path:

- the user explicitly selects the mods
- the default limit is 3 mods
- protected, ambiguous, and unsupported rows stay excluded
- a fresh dry run is required
- a verified backup is required
- rollback is prepared before the Apply button is enabled
- FFXIV and related launcher processes must be closed

If backup preparation, Apply, verification, or rollback is interrupted, the incomplete operation remains visible on the next launch with guided recovery actions instead of being silently cleared.

## Next rollback milestone

The rollback foundation milestone is complete and the guarded Apply foundation is now implemented for `sort_order.json` plus per-mod `meta.json` / `mod_data/<id>.json` metadata edits.

Remaining blockers before wider public Apply exposure are:

- confirming Penumbra reloads `sort_order.json` cleanly after external edits across versions
- expanding real-installation validation coverage without widening automated test scope
- deliberate public-release validation beyond fixture-only automation

Future write milestones must continue using temporary fixtures for automated verification and must not access the user's real Penumbra installation during development tests.
