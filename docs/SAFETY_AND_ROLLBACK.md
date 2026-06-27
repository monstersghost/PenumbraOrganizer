# Safety And Rollback

Penumbra Organizer must be rollback-first. No live write feature should be enabled until the project has a verified backup engine, immutable dry-run plan, rollback transaction model, conflict detection, exact-byte restoration, and post-rollback verification.

The organizer is read-only until:

- a scan succeeds
- protected-path validation passes
- dry-run validation passes
- a verified backup completes

The current `v0.1.0-alpha` build remains read-only for live Penumbra state. It can scan, create in-memory proposals, save organizer sessions, export sanitized AI inventory packages, and show Review Changes. It does not apply changes to Penumbra.

## Absolute boundaries

- no FFXIV game files are modified
- no physical mod folders are moved
- no physical mod directories are renamed
- no mod assets are rewritten
- no `.pmp` packages are handled
- no collections are modified
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

## Rollback

Rollback uses the saved manifest, backup files, and rollback transaction rather than the current workbook, current AI proposal, current organizer session, current display names, or a new live scan.

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

## Next rollback milestone

The immediate next milestone is documented in:

`docs/HANDOFF_ROLLBACK_FOUNDATION.md`

That milestone must use temporary fixtures only and must not access the user's real Penumbra installation.
