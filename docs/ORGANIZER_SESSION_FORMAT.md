# Organizer Session Format

Organizer sessions are application-owned proposal snapshots stored under:

`%LocalAppData%\PenumbraOrganizer\Sessions`

They do not contain live Penumbra files, mod assets, backups, undo history, or `.pmp` package data.

## File

The current last-session file is:

`%LocalAppData%\PenumbraOrganizer\Sessions\last-session.json`

Writes are atomic:

1. serialize to `last-session.json.tmp`
2. parse and validate the temporary JSON
3. flush file contents to disk
4. replace `last-session.json`

## Schema

Current `formatVersion` is `1`.

```json
{
  "formatVersion": 1,
  "sessionId": "guid",
  "savedAtUtc": "2026-06-27T15:30:00Z",
  "scanIdentity": "hashed scan identity",
  "scanTimestampUtc": "2026-06-27T15:20:00Z",
  "installationIdentity": "hashed installation identity",
  "installedPenumbraVersion": "1.6.1.10",
  "organizationPreferences": {},
  "proposedFolders": [
    {
      "path": "Clothing/Bizu Mods",
      "manuallyCreated": true,
      "protected": false
    }
  ],
  "mods": [
    {
      "stableScanId": "stable scan id",
      "currentVirtualFolder": "Current folder from scan",
      "proposedVirtualFolder": "Proposed folder",
      "protected": false,
      "organizerCreatorLabel": "Creator label",
      "organizerTypeLabel": "Type label",
      "proposalSource": "Manual",
      "needsReview": false
    }
  ],
  "metadataEdits": [
    {
      "stableScanId": "stable scan id",
      "name": "New name or null",
      "author": null,
      "description": null,
      "version": null,
      "website": null,
      "modTags": ["tag"],
      "favorite": true,
      "localTags": null,
      "note": null
    }
  ]
}
```

`metadataEdits` holds pending per-mod metadata changes (added without a `formatVersion` bump
because it is optional and defaults to empty). Each field is `null` when unchanged from the scanned
value, mirroring `ModMetadataEdit`. `name`/`author`/`description`/`version`/`website`/`modTags`
target the mod's `meta.json`; `favorite`/`localTags`/`note` target the per-user
`mod_data/<id>.json`. On resume, edits are matched by stable scan ID and re-applied to the rows.

## Staleness Rules

Resume is allowed only when:

* the session format is supported
* the saved installation identity matches the current scan
* the installed Penumbra version matches the current scan
* stable scan IDs still match sufficiently

When the library changed materially, the app must not restore silently. The user-facing message is:

`Your mod library changed since this organizer session was saved. Review which proposals can still be restored.`

Rows are matched by stable scan ID, never by display name alone.

## Proposal Source Precedence

Proposal sources are:

* `Manual`
* `DeterministicRule`
* `ImportedAi`
* `PreservedCurrent`
* `RestoredByUndo`

Manual changes override automated suggestions. Future deterministic or AI proposal generation must not silently replace manual rows.

## Undo And Redo Scope

Undo/redo is in-memory only in this first implementation. It covers:

* assignment to a proposed folder
* returning selected mods to their current folders
* protect and unprotect
* creating proposed folders
* renaming proposed folders
* deleting empty proposed folders

Undo history is not persisted. After resuming a saved session, the proposal state is restored and undo history starts fresh.

## Selected Versus Visible Rows

Primary organizer actions operate only on selected rows.

Visible filtered rows are not treated as selected. Any action that affects all visible rows must be separately labeled, such as:

* `Assign All Visible Mods`
* `Return All Visible Mods`

All-visible actions require confirmation with the exact affected count and destination.

## Review Changes

Review Changes is read-only in this milestone. It uses the reusable organizer validation service and reports:

* total mods
* changed
* unchanged
* protected
* needs review
* invalid
* warnings

Apply remains unavailable.
