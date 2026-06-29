# Penumbra Organizer Architecture

`PenumbraOrganizer` is an unofficial third-party Windows desktop application for scanning and organizing Penumbra's virtual mod folders. It must never modify Final Fantasy XIV game files and must remain usable offline.

## Solution layout

`PenumbraOrganizer.App`

- WPF desktop shell
- MVVM view models and commands
- dependency injection startup
- user-facing workflow states: `Scan`, `Review`, `Dry Run`, `Apply`
- beginner-first visual organizer under `Organize`
- manual folder-tree editing, drag and drop, bulk assignment, undo, redo, and before-and-after preview

`PenumbraOrganizer.Core`

- domain models
- interfaces
- protection rules
- creator canonicalization
- organization preferences and proposal source models
- manual override models for organizer-only creator/type labels
- versioned AI exchange models
- organizer mutation, history, validation, and session models
- schema fingerprinting rules
- dry-run and apply planning primitives

`PenumbraOrganizer.Infrastructure`

- Penumbra discovery
- config and metadata parsing
- filesystem scanning
- `sort_order.json` read/write for virtual-folder organization, plus `meta.json` and `mod_data/<id>.json` metadata editing
- backup, rollback, atomic write, CSV/JSON export
- versioned AI inventory package generation and validation
- read-only AI proposal validation
- organizer session persistence under `%LocalAppData%\PenumbraOrganizer\Sessions`
- compatibility/version checks
- application-owned persistence under `%LocalAppData%\PenumbraOrganizer`

`PenumbraOrganizer.Tests`

- fixture-backed parsing tests
- discovery tests
- compatibility tests
- temporary-directory integration tests

## Safety boundaries

- scanning is read-only
- AI export/import is read-only
- apply is disabled until scan, validation, backup, and dry run all succeed
- protected paths are immutable
- physical mod assets are never deleted or merged
- phase 1 writes only Penumbra virtual-folder metadata
- manual drag-and-drop changes only the in-memory proposed plan until Review, Backup, Apply, and Verify succeed
- spreadsheet export is optional advanced reporting, not the primary editor
- `.pmp` packages are not parsed, extracted, modified, imported, restored, or used as an organizer source
- collections are not modified during the first write-enabled milestone unless separately proven and approved

## Current alpha status

`v0.1.0-alpha` is a public prerelease at `https://github.com/monstersghost/PenumbraOrganizer`.

Complete foundations in the alpha include:

- public repository, MIT license, public README, user README, contribution/security docs, and issue/PR templates
- self-contained Windows x64 single-file release package and SHA-256 checksum generation
- separate `PenumbraStateDirectory` and `ModLibraryRoot`
- automatic Penumbra discovery and installed Penumbra version detection
- read-only installed-mod scanning and current virtual-folder inventory
- recognized mod metadata and Penumbra state data
- schema-fingerprint and compatibility foundations
- beginner-first manual organizer foundations with strategy cards, Folder View, All Mods, Suggested, Needs Review, and Changes Only
- proposed folder create/rename/delete, selected-row actions, explicit all-visible actions, assignment, return-to-current, protect/unprotect, centralized mutation, proposal-source tracking, undo, and redo
- atomic organizer session persistence under `%LocalAppData%\PenumbraOrganizer\Sessions\last-session.json`
- stable-scan-ID restoration, organization preference persistence, proposal persistence, organizer-only creator/type labels, and stale-session detection foundation
- Review Changes screen with reusable proposal validation and statuses `ValidChange`, `Unchanged`, `Protected`, `NeedsReview`, `InvalidPath`, `BlockedProtected`, `MissingMod`, and `StaleScan`
- sanitized external AI inventory export package with organization preferences

The repository now implements verified backup, rollback, immutable dry run, guarded Apply for supported virtual-folder changes, and post-Apply verification. GUI AI proposal import, drag-and-drop, collection editing, `.pmp` handling, and physical mod movement remain out of scope.

## PMP scope boundary

Penumbra `.pmp` files are import/export packages and are outside the installed-library organization workflow for the first write-enabled milestones.

The application must not parse, extract, modify, repack, import, restore, or otherwise operate on `.pmp` files during these milestones. It must not scan export/download directories for `.pmp` files and must not infer installed mods from `.pmp` packages.

The source of truth for organization is the currently installed Penumbra state and installed mod metadata: recognized state/configuration files, `sort_order.json` (authoritative virtual-folder organization), per-user `mod_data/<id>.json` files, installed mod directories, `meta.json`, `default_mod.json`, and `group_*.json`.

A future read-only "Inspect uninstalled package" feature may support `.pmp`, but it must remain separate and must not shape the organizer architecture now.

## Key data sources

- `%AppData%\XIVLauncher\pluginConfigs\Penumbra.json`
  carries Penumbra's configured `ModDirectory`
- `%AppData%\XIVLauncher\pluginConfigs\Penumbra\sort_order.json`
  stores the authoritative per-mod virtual-folder mapping. Shape:
  `{ "Data": { "<mod dir name>": "<full path incl. display leaf>" }, "EmptyFolders": [ ... ] }`.
  The `Data` value encodes both the containing folder (everything before the last `/`) and the
  mod's display/sort name (the final segment). A mod with no entry sits at the root under its
  `meta.json` name.
- `%AppData%\XIVLauncher\pluginConfigs\Penumbra\mod_data\<mod dir name>.json`
  stores per-user local data: `FileVersion`, `ImportDate`, `LocalTags`, `Note`, `Favorite`
- `<ModDirectory>\<mod dir name>\meta.json`
  stores author metadata: `Name`, `Author`, `Description`, `Version`, `Website`, `ModTags`
- `%AppData%\XIVLauncher\pluginConfigs\Penumbra\collections\*.json`
  stores collection state and enabled information
- installed plugin manifests and assemblies under `%AppData%\XIVLauncher\installedPlugins\Penumbra`
- installed mod directories and recognized installed-mod metadata files

The scanner intentionally ignores `.pmp` package archives for organizer decisions.

## Organization model

Organization must not force a single `Type/Creator` hierarchy. The supported strategies are:

- `CreatorOnly`: `Creator`
- `TypeOnly`: `Type`
- `TypeThenCreator`: `Type/Creator`
- `CreatorThenType`: `Creator/Type`
- `PreserveAndClean`: preserve meaningful existing folders while flattening clearly temporary source/import wrappers
- `Custom`: compose a validated pattern from `{Creator}`, `{Type}`, and an optional fixed root folder

The organization-preferences model records:

- strategy
- useTypeFolders
- useCreatorFolders
- folderOrder
- fixedRootFolder
- preserveMeaningfulExistingFolders
- flattenTemporarySourceFolders
- normalizeCreatorAliases
- unknownCreatorBehavior
- unknownTypeBehavior
- uncertainClassificationBehavior
- preserveCurrentFolderWhenUncertain

Protection overrides all organization preferences.

## Manual organizer workspace

Manual human organization is a primary workflow. Users must be able to organize entirely inside the GUI without AI, spreadsheets, exported CSV files, JSON editing, full path typing, command-line tools, or knowledge of Penumbra metadata.

The `Organize` screen starts with "How would you like your mods organized?" and offers beginner-friendly cards:

- Start Manually
- By creator
- By mod type
- By type and creator
- By creator and type
- Keep my current layout and clean it
- Custom
- External AI review

AI is optional and must not be presented as required or recommended.

The visual organizer contains:

- `Folder View`
- `All Mods`
- `Suggested`
- `Needs Review`

`Folder View` is the default. It uses a folder tree with counts and lock indicators, a mod list for the selected proposed folder, and an optional collapsible details pane. Users can create proposed folders, rename proposed folders, delete empty proposed folders, search/filter, assign selected mods, drag movable mods or folders, mark items protected, and undo/redo in-memory changes.

Every proposed row tracks its source:

- Manual
- Deterministic rule
- Imported AI suggestion
- Preserved current
- Restored by undo

Manual changes override automated or AI suggestions and must not be silently replaced by later automation. Re-running a strategy must ask whether to preserve manual changes, replace all proposals, or cancel, defaulting to preserving manual changes.

Primary organizer actions operate on selected rows. Filtered-but-unselected rows must remain unchanged. Any all-visible operation is exposed separately, clearly labeled, and confirmed with the exact count and destination.

All in-memory proposal mutations go through `IOrganizerMutationService`, including assignment, return-to-current, protection changes, folder creation, folder rename, folder deletion, future deterministic suggestions, future AI import, and future drag-and-drop. The service creates one undo history entry per successful logical action and does not touch live Penumbra files.

`IOrganizerProposalValidationService` provides reusable read-only validation for Review Changes, future AI import, and future dry-run planning.

`IOrganizerSessionService` persists application proposal state only. The session format is documented in `docs/ORGANIZER_SESSION_FORMAT.md`.

## Deterministic organization

The app must be able to produce proposals without AI when metadata is sufficient:

- Creator-only uses explicit author metadata, then meaningful existing creator folders, then preserves or sends to Review based on preference. It does not require type classification.
- Type-only uses explicit metadata and content signals, does not add creator folders, and preserves or sends uncertain items to Review based on preference.
- Combined strategies resolve type and creator independently, build destinations in the selected order, and omit empty components.
- Preserve-and-clean removes only clearly temporary wrappers and does not impose new creator/type layers unless requested.

## AI export and validation

Sanitized AI exports include `organizationPreferences` at the payload root. The generated master prompt must require the external AI to follow those preferences exactly, avoid imposing type folders when disabled, avoid imposing creator folders when disabled, skip unneeded type/creator inference for single-axis strategies, minimize changes in preserve-and-clean mode, and not replace the user's strategy with the AI's preferred structure.

Imported AI proposals must validate that proposed paths conform to the selected strategy unless a row is protected or explicitly manually overridden.

The version 1 inventory and proposal contracts are documented in `docs/AI_EXCHANGE_FORMAT.md`. Inventory exports use explicit domain models, globally unique `sourceExportId` values, sanitized path-like fields, strict package validation, and byte-identical ZIP/standalone files.

The read-only proposal validator accepts the original `AiInventoryExport` plus an imported `AiProposalDocument` and returns structured errors, warnings, accepted proposals, rejected proposals, and a summary. Global validation failures now block the entire import. Successful imported rows merge into the same in-memory proposal model used by manual editing, while manual overrides keep precedence.

## Review model

`Review Changes` remains mandatory for manual, deterministic, and imported AI proposals. It shows exact current and proposed virtual folders, validation status, and proposal source. Where applicable it can show proposed type, proposed creator, and final proposed folder, but Beginner mode hides irrelevant columns such as proposed type in Creator-only mode.

Normal flow:

1. Detect Penumbra.
2. Scan the installed library.
3. Select an organization strategy or start manually.
4. Generate proposals manually, deterministically, or through external AI.
5. Review exact virtual-folder changes.
6. Validate the real installation if the user explicitly requests it.
7. Create and verify a backup.
8. Apply supported virtual-folder metadata changes.
9. Verify and offer rollback.

This flow must not introduce `.pmp` handling.

For the first real-installation live test, the app now provides a dedicated `Controlled Test Apply` path on top of the same planner and recovery backend:

- the user explicitly selects a tiny live-test set
- the default limit is 3 mods
- protected, ambiguous, and unsupported candidates stay unavailable
- unselected proposals are excluded from the controlled dry run
- the default test folder is `PenumbraOrganizer Test`
- the final write target remains only the mod's entry in `sort_order.json`

## Rollback-first write architecture

The rollback and verified-backup foundation is implemented, and the first guarded Apply path now sits on top of it.

The current recovery slice includes:

- recovery domain models and service interfaces in `PenumbraOrganizer.Core`
- verified backup creation from explicit file lists only
- immutable `manifest.json` and rollback transaction persistence
- atomic JSON persistence for operation, manifest, rollback, verification, and history records
- exact-byte rollback restore with conflict detection
- backup and rollback verification services
- operation-history rebuilding from operation packages
- read-only `Backups` UI foundation
- immutable dry-run planner and plan invalidation service
- exact expected-result generation for `sort_order.json`, `meta.json`, and `mod_data/<id>.json`
- write-permission preflight and `asInvoker` execution level
- user-authorized real-installation validation workflow
- guarded Apply executor that writes only `sort_order.json` entries and (for metadata edits) `meta.json` / `mod_data/<id>.json`
- post-Apply verification and rollback availability tracking
- privacy-conscious diagnostic export
- post-Apply Penumbra UI observation capture
- incomplete-operation detection for interrupted backup, Apply, verification, and rollback work

Rollback remains independent of display names, current AI proposals, current organizer sessions, new scans, workbooks, or reconstructing metadata from current state. It uses immutable operation records, verified backups, original hashes, applied hashes, affected file lists, and exact original bytes.

If the app closes or crashes mid-operation, the next launch keeps the operation visible and offers narrow recovery actions instead of hiding it:

- `Re-verify`
- `Continue verification`
- `Roll back`
- `View details`

The package layout and schema details are documented in `docs/BACKUP_AND_ROLLBACK_FORMAT.md`.

The currently proven authoritative write targets are:

* organization: `sort_order.json`, key = mod directory name, value = full path (folder + display leaf); `EmptyFolders` is authoritative for empty folders
* author metadata: `<ModDirectory>\<mod dir name>\meta.json`
* per-user local data: `mod_data\<mod dir name>.json`

The top-level physical mod directory name is the stable scan ID used to map an installed mod to its `sort_order.json` entry, its `meta.json`, and its `mod_data/<id>.json` file. The same string is the physical folder name, the `Data` key, and the `mod_data/<id>.json` filename.

Writes are multi-file: a single Apply can touch `sort_order.json` plus one `meta.json` and/or one `mod_data/<id>.json` per edited mod, all captured by one N-file backup and rolled back together. Editing a placed mod's `meta.json` `Name` also rewrites its `sort_order.json` display leaf so the rename is visible in Penumbra; root mods are renamed by `meta.json` alone.

## Milestone 1

Milestone 1 focuses on a production-grade read-only inventory and safe planning foundation:

- automatic and manual Penumbra discovery
- installed version detection
- recursive mod scan with metadata tolerance
- current virtual folder inventory
- protected row enforcement
- beginner-friendly manual proposed-folder editing
- visual folder organizer workspace
- deterministic organization rules
- strategy preferences and custom pattern validation
- dry-run and backup artifacts
- compatibility warning model
- atomic metadata-write backend for future apply UI
