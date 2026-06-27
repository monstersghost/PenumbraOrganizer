# AI Exchange Format

This document defines the version 1 exchange contract for optional external AI review.

AI review is read-only. It may suggest Penumbra virtual-folder changes, but it must not apply changes, edit files, inspect `.pmp` packages, move physical directories, or alter Final Fantasy XIV game files.

## Inventory Export

The app writes `Penumbra_Mod_Inventory.json` with this schema:

```json
{
  "formatVersion": 1,
  "sourceExportId": "export-20260627T153000Z-guid",
  "generatedAtUtc": "2026-06-27T15:30:00Z",
  "installedPenumbraVersion": "optional version",
  "organizationPreferences": {
    "strategy": "CreatorOnly",
    "useTypeFolders": false,
    "useCreatorFolders": true,
    "folderOrder": ["Creator"],
    "fixedRootFolder": null,
    "preserveMeaningfulExistingFolders": true,
    "flattenTemporarySourceFolders": true,
    "normalizeCreatorAliases": true,
    "unknownCreatorBehavior": "PreserveCurrent",
    "unknownTypeBehavior": "NotApplicable",
    "uncertainClassificationBehavior": "Review",
    "preserveCurrentFolderWhenUncertain": true,
    "customPattern": null
  },
  "mods": [
    {
      "scanId": "stable scan id",
      "protectedRow": false,
      "currentVirtualFolder": "Current Folder",
      "name": "Mod name",
      "author": "Author",
      "version": "Version",
      "website": "Website",
      "description": "Description",
      "tags": [],
      "recognizedMetadataFiles": ["meta.json"],
      "unknownMetadataFiles": [],
      "malformedMetadataFiles": [],
      "collectionReferenceCount": 0,
      "warnings": [],
      "contentSignalSummary": "",
      "schemaFingerprints": [
        {
          "fileName": "meta.json",
          "fingerprint": "hash",
          "differenceKind": "None",
          "notes": []
        }
      ]
    }
  ]
}
```

`formatVersion` is currently `1`.

`sourceExportId` is globally unique and must be copied exactly into any proposal document.

## Proposal Document

The AI must return JSON only, with no Markdown fences and no prose outside the JSON.

The required proposal schema is:

```json
{
  "formatVersion": 1,
  "sourceExportId": "copy exactly from inventory",
  "generatedBy": {
    "provider": "optional provider name",
    "model": "optional model name"
  },
  "summary": {
    "totalRowsReceived": 0,
    "totalRowsReturned": 0,
    "protectedRows": 0,
    "changedRows": 0,
    "unchangedRows": 0,
    "reviewRows": 0
  },
  "creatorAliases": [
    {
      "original": "Original value",
      "canonical": "Canonical value",
      "confidence": "high",
      "reason": "Brief explanation"
    }
  ],
  "proposals": [
    {
      "scanId": "copy exactly from inventory",
      "protected": false,
      "currentVirtualFolder": "copy exactly from inventory",
      "proposedVirtualFolder": "Creator or Type/Creator",
      "proposedType": null,
      "proposedCreator": null,
      "action": "keep",
      "confidence": "high",
      "reason": "Brief explanation",
      "evidence": [],
      "warnings": []
    }
  ]
}
```

Allowed `action` values:

* `keep`
* `move`
* `review`

Allowed `confidence` values:

* `high`
* `medium`
* `low`
* `protected`

## Proposal Rules

The AI must:

* read `organizationPreferences`
* follow the selected strategy exactly
* return every input `scanId` exactly once
* never add unknown IDs
* never omit rows
* copy `sourceExportId` exactly
* copy `currentVirtualFolder` exactly
* leave protected rows unchanged
* use `action = keep` and `confidence = protected` for protected rows
* use `keep` only when current and proposed folders match
* use `move` only when they differ
* use `review` for uncertain decisions
* never emit physical paths as proposed virtual folders
* never propose deleting, merging, renaming, or moving physical mod directories
* avoid classification dimensions disabled by the selected strategy

Strategy-specific rules:

* `CreatorOnly`: infer creator if useful, do not infer type for organization, and do not create type folders.
* `TypeOnly`: classify type if useful, do not infer creator for organization, and do not create creator folders.
* `TypeThenCreator`: resolve type and creator independently and use `Type/Creator`.
* `CreatorThenType`: resolve creator and type independently and use `Creator/Type`.
* `PreserveAndClean`: minimize changes, preserve meaningful folders, and flatten only clearly temporary wrappers.
* `StartManually`: normally do not generate an AI package; if generated through Advanced mode, return unchanged rows unless the user supplied a separate explicit instruction.
* `Custom`: obey the validated pattern and fixed root; do not invent unsupported path components.

## Sanitization

The inventory export sanitizes path-like fields before writing JSON:

* absolute paths are rejected unless they can be safely relativized to the mod directory or mod-library root
* mod-library root paths are removed
* Windows profile paths are removed from free-text path signals
* Penumbra state-directory paths are removed from free-text path signals
* exported path separators are normalized to `/`
* `..` traversal is rejected
* rooted paths, drive letters, empty path components, and control characters are rejected

Reviewed fields include:

* `recognizedMetadataFiles`
* `unknownMetadataFiles`
* `malformedMetadataFiles`
* `contentSignalSummary`
* schema fingerprint `fileName`
* schema fingerprint notes
* warnings

The inventory must never include:

* `C:\Users\<name>`
* the absolute Penumbra state directory
* the absolute mod-library root
* arbitrary unrelated filesystem locations

## Package Validation

Version 1 packages contain exactly these root-level ZIP entries:

* `Penumbra_Mod_Inventory.json`
* `AI_INSTRUCTIONS.txt`
* `HOW_TO_USE.txt`

The standalone files and zipped entries must be byte-identical.

The ZIP must not contain nested entries or extra files.

## Import Validation

The read-only validator checks:

* supported format version
* matching `sourceExportId`
* exact row-count match
* every inventory `scanId` occurs exactly once
* no unknown IDs
* no duplicate IDs
* copied current folder matches inventory exactly
* protected rows are unchanged
* protected rows use `keep` and `protected`
* keep rows have equal current/proposed paths
* move rows have different paths
* review rows use a valid Review destination or preserve current based on preferences
* proposed paths are logical relative Penumbra paths
* no drive letters
* no rooted paths
* no path traversal
* no invalid control characters
* no empty path components
* strategy compliance

Validation returns structured errors, warnings, accepted proposals, rejected proposals, and a summary. It does not modify live state.
