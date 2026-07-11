# Backup and rollback format

## Scope

The rollback foundation is implemented for fixture-backed development and future Apply integration. It does not enable live Apply in the public alpha UI.

All development verification and automated tests must use temporary fixture directories only.

## Package layout

Operation packages live under:

`%LocalAppData%\PenumbraOrganizer\Backups\<operation-id>\`

Each package contains:

```text
<operation-id>\
├── operation.json
├── manifest.json
├── rollback.json
├── verification.json
├── files\
└── logs\
```

`files\` contains a full copy of the Penumbra configuration directory (`sort_order.json`,
`mod_data\`, `collections\`, and any other files Penumbra keeps there) captured before every
Apply. It must not contain full mod assets (the mod-library root is never backed up).

Current apply-enabled packages also persist:

* `plan.json`
* `apply.json`

## operation.json

`operation.json` is the durable summary record for the package. It stores:

* operation ID
* created time in UTC
* application version
* Penumbra version when known
* scan identity
* backup status
* apply status
* rollback status
* verification status
* affected file count
* affected mod count when known
* conflict count
* failure count
* operation folder
* whether a rollback transaction exists
* whether rollback is currently available
* last error when present

## manifest.json

`manifest.json` is immutable once a verified backup is finalized.

Each backup file entry stores:

* source target path
* relative backup path inside the operation package
* original length
* original SHA-256
* backup length
* backup SHA-256
* file classification
* JSON validation result
* protected status
* associated stable scan IDs
* writable-plan operation ID when present

Backup relative paths must remain inside the operation package. Traversal such as `..` is invalid.

## rollback.json

`rollback.json` stores the rollback transaction and per-file progress. It is updated atomically after each file or safe batch so interrupted work can be resumed or safely re-evaluated.

Each rollback file entry stores:

* target path
* relative backup path
* original SHA-256
* original length
* expected applied SHA-256
* whether the target existed before Apply
* expected file classification
* protected state
* Apply result status
* rollback file status
* associated stable scan IDs
* planned operation ID when present
* observed live hash and length when relevant
* plain-language status message
* whether force restore was used

## verification.json

`verification.json` stores the latest backup verification result, post-Apply verification result, and latest rollback verification result.

Backup verification checks:

* manifest parses
* required entries exist
* backup files exist
* lengths match
* SHA-256 hashes match
* JSON parses when expected
* protected files do not appear in writable backup plans

Rollback verification checks:

* restored targets exist when expected
* restored length equals the original length
* restored SHA-256 equals the original backup hash
* restored JSON parses when expected
* skipped conflicts remain unchanged
* transaction status matches file outcomes

## Exact-byte restore rule

Rollback restores the exact backed-up bytes. It does not reconstruct JSON from domain models.

Restore flow:

1. validate the backup entry
2. validate the backup file length and SHA-256
3. validate backup JSON when expected
4. inspect the live target
5. compare the current live hash with the expected applied hash and original backup hash
6. write the backup bytes to a temporary file in the target directory
7. verify the temporary file
8. flush to disk
9. atomically replace or create the live target
10. verify the restored target length and SHA-256

## Conflict logic

Safe automatic restore is allowed when:

`current live hash == expected applied hash`

Already restored is detected when:

`current live hash == original backup hash`

Conflict is recorded when:

`current live hash` matches neither the expected applied hash nor the original backup hash

Missing or corrupt backups block restoration for that file.

If the live file is missing and the transaction says it existed before Apply, rollback may recreate it from the verified backup.

If the live file did not exist before Apply, deletion rollback is not invented implicitly. That file is skipped unless a future explicit deletion record exists.

## Idempotence and interruption behavior

Rollback is designed to be safe on repeat execution:

* already restored files are detected
* conflicts remain conflicts unless the live file changes
* restored files are not rewritten unnecessarily on the second run
* incomplete rollback records can be reloaded
* progress is persisted during execution

## Atomic persistence

`operation.json`, `manifest.json`, `rollback.json`, `verification.json`, and the history index use same-directory temporary JSON files, validation, flush-to-disk, atomic move/replace, and re-read validation.

Temporary files left behind by interrupted work are not treated as finalized records.

## Operation history rebuilding

The app keeps a local history index, but it is not the source of truth.

History is rebuildable from operation packages by scanning the backup root and reading `operation.json` from each valid operation directory.

## Protected-file exclusion

Protected files must not be accepted into writable backup requests or rollback transactions.

If a protected file is supplied:

* backup creation fails
* rollback transaction persistence fails
* the package is not reported as a usable writable backup plan

## Current UI availability

The WPF app now includes a read-only `Backups` screen foundation that lists operations, shows backup/apply/rollback summaries, and shows affected files.

It may expose:

* refresh backup history
* verify backup
* open backup folder
* view summary
* view affected files

It does not expose:

* public rollback execution
* force restore

Live Apply is now guarded from the Review Changes workflow for the proven `sort_order.json`
virtual-folder target. A single operation backs up and can roll back the entire Penumbra
configuration directory, not just the file(s) Apply is about to write, so unrelated files stay
recoverable too. Files the operation didn't touch are recorded as already matching their backup
(no-op on restore unless something else changed them, in which case restore treats that as a
conflict like any other).
