# Safety And Rollback

The organizer is read-only until:

- a scan succeeds
- protected-path validation passes
- dry-run validation passes
- a verified backup completes

## Protected content

Protected mods and protected folder prefixes are immutable during apply.

For protected rows:

- current virtual folder must equal proposed virtual folder
- no creator normalization is applied
- no file is written
- no path is flattened or merged

## Backup contents

Before apply, the app creates a timestamped backup under `%LocalAppData%\PenumbraOrganizer\Backups` or a user-selected backup root.

The backup includes:

- metadata files that will be written
- Penumbra config files relevant to the plan
- scan snapshot
- rule profile
- dry-run plan
- manifest
- rollback map
- version and schema snapshot

Large mod assets are not duplicated unless a future feature would modify those files directly.

## Rollback

Rollback uses the saved manifest, backup files, and rollback map rather than the current workbook or live scan state.

For each file restore:

- current hash is checked first
- mismatches produce a warning instead of blind overwrite
- restored files are revalidated when possible
