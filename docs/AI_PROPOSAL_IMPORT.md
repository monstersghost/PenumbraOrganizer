# AI Proposal Import

The app now supports importing `Penumbra_AI_Proposal.json` into the existing in-memory organizer session.

## Validation rules

Imports are blocked when:

* `formatVersion` is unsupported
* `sourceExportId` does not match the exported inventory
* rows are missing
* unknown scan IDs appear
* duplicate scan IDs appear
* protected rows are modified
* proposed virtual-folder paths are invalid
* the selected organization strategy is violated

Global validation failures reject the entire import.

## Merge behavior

Validated imported rows enter the same proposal model used by manual edits and dry run.

Manual proposal changes keep precedence:

* existing manual overrides are preserved
* imported rows become `Imported AI`
* uncertain imported rows are marked `Needs review`

Import never triggers Apply automatically.
