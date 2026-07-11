# Controlled live test

## Scope

`Controlled Test Apply` is the first intended real-installation workflow for Penumbra Organizer.

It exists to validate the proven authoritative write path:

* `sort_order.json`: the mod's entry in `Data` (folder + display leaf)

It does not widen the write target. (Metadata edits, when present, additionally touch the mod's
`meta.json` / `mod_data/<id>.json`; the controlled test focuses on the folder move.)

The legacy `mod_filesystem\organization.json` is not authoritative and is not written.

## Default workflow

1. Scan the installed library.
2. Open `Review Changes`.
3. Choose `Choose Test Mods`.
4. Select one or more eligible mods.
5. Keep the default maximum of 3 mods unless a future milestone deliberately changes it.
6. Confirm or edit the test folder name.
7. Create a fresh dry run.
8. Create a verified backup.
9. Review the explicit confirmation modal.
10. Use `Controlled Test Apply`.
11. Let post-Apply verification finish.
12. Record the Penumbra UI observation.
13. Use guarded rollback after the test.

## Eligibility rules

Selectable candidates must:

* be chosen explicitly by the user
* have an authoritative `sort_order.json` entry (or be a root mod that will get one)
* not be protected
* not be flagged ambiguous by the current scan
* not be unsupported for the narrow write path

The dialog shows:

* current folder
* current proposal
* proposed test folder
* physical path as read-only information
* authoritative record key
* authoritative target path

## Safety checks

Before Apply is enabled, the workflow requires:

* a fresh dry run
* a verified backup
* a prepared rollback transaction
* writable authoritative target checks
* no relevant FFXIV or XIVLauncher process blockers
* no protected-row violations
* no unsupported or ambiguous controlled-test rows

## Final confirmation

The final modal must include:

* selected mod count
* current folders
* proposed folders
* authoritative target `sort_order.json`
* verified backup location
* rollback readiness
* confirmation that physical mod files will not move
* confirmation that FFXIV must be closed
* alpha-test warning text

Buttons:

* `Apply Test Changes`
* `Cancel`

## Verification

After Apply, the app re-reads `sort_order.json` and checks:

* selected rows now match the planned folder
* unrelated rows remain unchanged
* protected rows remain unchanged
* the file still parses cleanly

Success is not reported only because no exception occurred.

## Penumbra UI observation

After a verified Apply, the app asks:

`Open Penumbra and check whether the new virtual folder appears.`

Observation choices:

* `Appeared immediately`
* `Appeared after reload/restart`
* `Did not appear`
* `I have not checked yet`

This observation is stored as evidence only.

It does not prove that `organization.json` must be written, and it does not change the current write path by itself.

## Rollback

Rollback for the controlled test:

* verifies the backup again
* checks current live hashes
* restores exact backup bytes only when safe
* skips conflicts by default
* verifies restored rows afterward

No force restore is exposed in Beginner mode.

## Release recommendation boundary

`v0.2.0-alpha` should not be recommended from code changes alone.

Real readiness still depends on:

* successful read-only validation on a real installation
* one controlled real Apply
* verified post-Apply result
* physical mod assets remaining untouched
* observed Penumbra UI behavior
* successful rollback
