# Penumbra Organizer — Project Context

## Product goal

Penumbra Organizer is an unofficial Windows desktop utility for ordinary Final Fantasy XIV players who may have little or no technical knowledge.

Its purpose is to:

* automatically detect Penumbra
* scan installed mods and current Penumbra organization
* show current logical folders and relevant settings
* allow users to propose folder changes manually
* optionally export sanitized metadata as a workbook for offline review
* import and validate edited workbook proposals
* protect selected mods and folder trees from all changes
* create verified backups
* generate dry-run plans
* safely apply supported Penumbra virtual-folder metadata changes
* provide rollback
* warn when the installed Penumbra version or recognized schema changes

The application must not modify Final Fantasy XIV game files.

## Intended users

The primary audience is gamers, not developers.

The release experience must be:

1. Download the release ZIP.
2. Extract it.
3. Double-click `PenumbraOrganizer.exe`.
4. Use the GUI.

The user must not need:

* Command Prompt
* PowerShell
* .NET installation
* Python
* Node.js
* Java
* Git
* Visual Studio
* LibreOffice
* Microsoft Excel
* WebView2
* a package manager
* an external database
* any SDK or runtime installation

## Technology and distribution

The project uses:

* .NET 8
* C#
* WPF
* MVVM
* Microsoft.Extensions.DependencyInjection
* Microsoft.Extensions.Logging
* System.Text.Json
* xUnit

The release is:

* Windows x64
* self-contained
* single-file
* GUI subsystem
* portable
* usable without an installer
* functional offline for all core features

The primary artifact is:

`PenumbraOrganizer.exe`

Application-owned writable data belongs under:

`%LocalAppData%\PenumbraOrganizer\`

The app should not rely on writing beside its EXE.

## Public repository and alpha release

The public repository is:

`https://github.com/monstersghost/PenumbraOrganizer`

The current public prerelease is:

`v0.1.0-alpha`

The initial public commit is:

`298e7fa Initial public alpha of Penumbra Organizer`

The current public release package is:

`PenumbraOrganizer-v0.1.0-alpha-win-x64.zip`

Verified release package details:

* ZIP size: `71,391,756 bytes`
* ZIP SHA-256: `ae68aa55f0b7fd78c6b6346b82dcf49656ac6b8efd36daa13a0882f00e9fd500`
* release EXE launches from the extracted ZIP
* solution build passes with 0 warnings and 0 errors
* tests pass: 92/92
* guarded Apply foundation is implemented for supported `sort_order.json` virtual-folder writes
* no physical mod, collection, or FFXIV writes are implemented

## Critical distinction between Penumbra state and mod storage

Penumbra's configuration/state directory and the user's physical mod library are different locations.

### Penumbra state directory

The normal XIVLauncher path is:

`%AppData%\XIVLauncher\pluginConfigs\Penumbra`

This typically expands to:

`C:\Users\<WindowsUser>\AppData\Roaming\XIVLauncher\pluginConfigs\Penumbra`

Never hardcode a specific Windows username.

Resolve the roaming application-data directory through Windows APIs.

Supported alternatives may include XIVLauncherCN and relevant local application-data locations.

Use the code concept:

`PenumbraStateDirectory`

### User mod library

The physical mod library is located wherever the user selected it in Penumbra. It may be on another drive or in any custom directory.

Discover it from Penumbra configuration/state where possible.

Use the code concept:

`ModLibraryRoot`

Do not assume it is inside XIVLauncher.

The project repository itself must never be pointed at or created inside the mod library.

## Physical assets versus virtual organization

Normal organization operations must not:

* move physical mod directories
* rename physical mod directories
* merge physical mod folders
* delete mod files
* rewrite textures
* rewrite models
* rewrite animations
* rewrite sounds
* rewrite VFX
* copy the full mod library

The primary operation is changing supported Penumbra logical or virtual-folder metadata.

The GUI should say:

* Current Penumbra folder
* Proposed Penumbra folder
* Apply virtual-folder changes

Avoid misleading terms such as "move files" when no assets are physically moved.

## PMP scope boundary

Penumbra `.pmp` files are import/export packages and are not required for the installed-library organization workflow.

The application must not parse, extract, modify, repack, import, restore, or otherwise operate on `.pmp` files during the first write-enabled milestones.

The source of truth for organization is the currently installed Penumbra state and installed mod metadata, including recognized state/configuration files, `sort_order.json` (authoritative virtual-folder organization), per-user `mod_data/<id>.json` files, installed mod directories, `meta.json`, `default_mod.json`, and `group_*.json`.

Do not scan the user's export/download directories for `.pmp` files.

Do not infer installed mods from `.pmp` packages.

A future read-only "Inspect uninstalled package" feature may support `.pmp`, but it must remain a separate feature and must not affect the organizer architecture now.

## Protection model

Protected content is immutable.

For every protected mod or protected subtree:

* the proposed virtual folder must equal the current virtual folder
* no folder or creator normalization may apply
* no metadata file belonging to it may be written
* no parent or intermediate logical folder may be flattened
* no protected row may be omitted from validation
* Apply must be blocked if a protected path differs

Protection takes priority over all cleanup, classification and creator rules.

## Classification lessons from the successful reorganization

The prototype reorganization successfully processed a library of 1,913 mods.

Results:

* 214 protected mods remained unchanged
* 1,699 movable mods were updated
* zero protected-path changes
* zero failed operations
* the dry run and applied move log matched

Important rules learned:

### Clothing

If a package contains at least one real clothing item, it normally belongs under Clothing even when accessory or texture signals dominate.

Texture-file count alone must not make a clothing package a texture mod.

### Accessory

Use for standalone additions such as:

* horns
* ears
* tails
* jewellery
* glasses
* piercings
* decorative extras

### Animation

Bundled VFX and sound do not override an animation classification.

A package can contain more sound or VFX files than animation files and still primarily be an animation mod.

### Source folders

Preserve meaningful creator, studio, collection, or intentional character folders.

Flatten temporary or source-only wrappers such as:

* telegram
* downloads
* random
* compress
* experimental
* test
* date folders
* import batches
* generic wrappers that merely repeat the destination category

Example:

`telegram/outfits/Bizu Mods`

becomes logically:

`Clothing/Bizu Mods`

not:

`Clothing/telegram/outfits/Bizu Mods`

### Creator normalization

Obvious capitalization, whitespace, punctuation, and formatting variants may be merged for non-protected mods.

Previously approved examples included:

* `enni` and `Enni` -> `Enni`
* `Etherealsins` and `EtherealSins` -> `EtherealSins`
* Soft Bun and Illy capitalization variants -> `Soft Bun`
* `Nini` and `_Nini_` -> `Nini`
* `Hanzo` and `Hanzo Dojo` -> `Hanzo Dojo`
* `Koneko` and `Konekomods` -> `Koneko`

Do not automatically merge genuinely ambiguous creator names.

Creator normalization never applies inside protected paths.

## Organization strategies

The application must not assume every user wants the hierarchy:

`Type/Creator`

Users must be able to organize by creator, by type, by both, by preserving their existing layout, or by starting manually.

Supported strategies:

### Start manually

Users can create proposed Penumbra folders, select mods, assign or drag them into folders, undo and redo, mark protected items, and reach Review Changes without spreadsheets, exported CSV files, JSON editing, command-line tools, or repeated full-path typing.

### Creator only

Pattern:

`Creator`

Examples:

* `Bizu Mods`
* `C&O`
* `Soft Bun`

Do not create type folders. A mod does not need to be classified by type for this strategy.

### Type only

Pattern:

`Type`

Examples:

* `Clothing`
* `Hair`
* `Accessory`
* `Animation`

Do not create creator subfolders. Creator may still be displayed as metadata.

### Type then creator

Pattern:

`Type/Creator`

Examples:

* `Clothing/Bizu Mods`
* `Hair/C&O`

This matches the organization used by the original prototype library.

### Creator then type

Pattern:

`Creator/Type`

Examples:

* `Bizu Mods/Clothing`
* `C&O/Hair`

### Preserve and clean

Keep meaningful existing organization while flattening clearly temporary source/import wrappers.

Do not rebuild the full tree unless the user explicitly chooses another strategy.

### Custom

Allow the user to compose a supported folder pattern using:

* `{Creator}`
* `{Type}`
* optionally a fixed user-entered parent folder

Examples:

* `{Creator}`
* `{Type}`
* `{Type}/{Creator}`
* `{Creator}/{Type}`
* `My Mods/{Creator}`

Do not allow arbitrary executable templates or scripting.

Validate path segments and show a live preview.

## Organization preference model

The organization-preferences model contains at least:

* strategy
* useTypeFolders
* useCreatorFolders
* folderOrder
* fixedRootFolder
* preserveMeaningfulExistingFolders
* flattenTemporarySourceFolders
* normalizeCreatorAliases
* unknownCreatorBehavior
* unknownTypeBehavior
* uncertainClassificationBehavior
* preserveCurrentFolderWhenUncertain

Supported strategy values:

* `CreatorOnly`
* `TypeOnly`
* `TypeThenCreator`
* `CreatorThenType`
* `PreserveAndClean`
* `Custom`

Unknown creator behavior examples:

* place directly under the selected type
* preserve current meaningful folder
* send to Review

Unknown type behavior examples:

* place directly under the creator
* preserve current meaningful folder
* send to Review

Uncertain classification behavior examples:

* send to Review
* preserve current folder
* use best-supported result with a warning

Protection always overrides these preferences.

## Deterministic organization

The application should be capable of producing proposals automatically when metadata is sufficient.

For Creator-only organization:

1. Use explicit author metadata.
2. Otherwise use a meaningful existing creator folder.
3. Otherwise preserve the current folder or send to Review according to user preference.
4. Do not perform type classification unless needed for display.

For Type-only organization:

1. Use explicit metadata and content signals.
2. Do not add creator folders.
3. Send uncertain items to Review or preserve them according to user preference.

For combined organization:

1. Resolve type and creator independently.
2. Build the destination using the selected order.
3. Do not create empty path components.
4. Follow the configured unknown-value behavior.

For Preserve-and-clean:

1. Identify meaningful existing creator, studio, collection and character folders.
2. Remove only clearly temporary wrappers.
3. Do not impose creator or type layers that did not exist unless requested.

## Manual organizer workspace

Manual human organization is a primary, beginner-friendly workflow.

The application should feel like arranging files in a safe visual organizer, while making it clear that only Penumbra's virtual organization is being changed.

Use familiar interactions:

* folders
* drag and drop
* multi-select
* right-click actions
* new folder
* rename proposed folder
* move selected items
* undo
* redo
* before-and-after preview

Normal manual organization must not require users to understand review IDs, confidence scoring, schema fingerprints, physical paths, or raw metadata.

The `Organize` area includes these views:

1. `Folder View`
2. `All Mods`
3. `Suggested`
4. `Needs Review`

Default to `Folder View`.

### Folder View layout

Use a two-pane or three-pane layout.

Left pane:

* proposed Penumbra folder tree
* expandable and collapsible folders
* mod counts beside folders
* protected folders visibly locked
* search/filter field
* New Folder
* Rename
* Delete Empty Folder
* Undo
* Redo

Main pane:

* mods inside the selected proposed folder
* selectable rows or cards
* mod name
* creator
* current folder shown as secondary text
* warning icon when relevant
* lock icon for protected mods
* optional thumbnail only when cheaply and safely available

Optional details pane:

* selected mod information
* current Penumbra folder
* proposed Penumbra folder
* creator
* detected type
* warnings
* reason for any automated suggestion
* protection state

The details pane should be collapsible.

### Drag and drop

Allow users to drag one or multiple movable mods into a proposed virtual folder.

Dragging changes only the proposed virtual folder in application memory. It must not immediately write to Penumbra.

Allow dragging:

* mods into folders
* movable folders beneath another folder
* selected groups of mods
* items from search results into a destination folder

Do not allow:

* dragging protected mods
* moving protected folder trees
* placing a folder inside itself or one of its descendants
* invalid virtual-folder names
* silent overwriting or merging
* moving physical directories

When dragging a protected item, show:

`This mod is protected and will remain in its current Penumbra folder.`

When a drag is valid, show a clear preview such as:

`12 mods will be assigned to Clothing/Bizu Mods`

The user must still review and apply the resulting proposal later.

### Familiar manual actions

Support right-click or toolbar actions:

* Move to folder
* Create folder here
* Create creator folder
* Create type folder
* Rename proposed folder
* Return to current folder
* Mark as protected
* Unprotect
* Select all in folder
* Select all by creator
* Select all by detected type
* Find similar creator names
* Send to Needs Review

Use "Assign to folder" or "Change proposed folder" rather than "Move files."

### Folder creation

Creating a folder should use a simple dialog:

Title:

`Create Penumbra folder`

Fields:

* Folder name
* Parent folder

Show the resulting path as a preview.

Validate:

* empty names
* leading or trailing separators
* invalid control characters
* `.` and `..`
* duplicate sibling names, using Penumbra/Windows-appropriate comparison
* accidental absolute paths

Do not create a real Penumbra folder immediately. The folder exists only in the proposed plan until Apply.

### Quick organization actions

Provide large beginner-friendly actions:

* Group by creator
* Group by mod type
* Group by type and creator
* Keep current organization
* Assign selected mods

Bulk actions should display the number of affected mods before applying the proposal in memory.

Example:

`Assign 47 selected mods to Clothing/Bizu Mods?`

This is not the final Apply operation and does not require a backup yet.

### Manual correction of labels

Allow the user to correct organization labels without modifying the mod's original metadata.

Examples:

* display creator override used only by Penumbra Organizer
* detected type override
* preferred canonical creator name
* meaningful existing folder marker
* temporary source folder marker

Store these as application-owned organization decisions under:

`%LocalAppData%\PenumbraOrganizer\`

Do not rewrite a mod's author metadata merely because the user changes its organization label.

Distinguish:

* Original metadata creator
* Organizer creator label
* Proposed folder

In Beginner mode, show only the effective creator label unless the user opens details.

### Selection, search, and filters

Support standard Windows selection:

* click
* Ctrl+click
* Shift+click
* Ctrl+A within the current view

Search across:

* mod name
* creator
* current folder
* proposed folder

Filters:

* changed
* unchanged
* protected
* unprotected
* unknown creator
* unknown type
* warnings
* needs review
* suggestion source
* current folder
* proposed folder

For large libraries, use virtualization and do not render all items at once.

### Suggested versus manual changes

Every proposed change must track its source:

* Manual
* Deterministic rule
* Imported external suggestion
* Preserved current path
* Restored by undo

A manual action overrides an automated or imported suggestion for that row.

Later automation must not silently overwrite a manual decision.

If the user deliberately runs a new organization strategy, show:

`This may replace existing automated suggestions. Your manual changes will be preserved unless you choose otherwise.`

Provide explicit choices:

* Preserve manual changes
* Replace all proposals
* Cancel

Default to preserving manual changes.

### Undo and redo

Implement a proper in-memory command history for organization actions.

Undo and redo should cover:

* assigning mods to folders
* bulk assignments
* creating proposed folders
* renaming proposed folders
* deleting empty proposed folders
* creator canonicalization
* type corrections
* protection changes
* resetting items to their current folders

Do not rely on reopening the scan to undo mistakes.

Show concise descriptions such as:

* `Undo: Assign 12 mods to Hair/C&O`
* `Redo: Rename Bizu to Bizu Mods`

Clear undo history after a successful Apply and new scan, with an explicit explanation.

### Current and proposed views

Users must be able to switch between:

* Current organization
* Proposed organization
* Changes only

Current organization is read-only. Proposed organization is editable. Changes only shows what differs.

A split comparison mode may show current tree on the left and proposed tree on the right. Do not require this advanced comparison for normal use, but make it available.

### Needs Review workspace

Items that cannot be confidently organized should appear in one simple queue.

For each item show:

* mod name
* current folder
* likely creator
* likely type
* suggested destinations, when available
* Keep Where It Is
* Choose Folder
* Mark Protected

Allow keyboard navigation so a user can review many items quickly.

Do not show internal confidence decimals.

Use plain labels:

* Clear match
* Likely match
* Unsure

### Manual acceptance criteria

A first-time user must be able to:

1. Scan a library.
2. Create `Clothing`.
3. Create `Bizu Mods` beneath it.
4. Search for Bizu.
5. Select multiple mods.
6. assign or drag them into `Clothing/Bizu Mods`.
7. Undo the action.
8. Redo it.
9. mark one mod protected.
10. inspect the Changes-only view.
11. reach Review Changes without opening a spreadsheet, JSON file, terminal, or Advanced mode.

## External review design

> An earlier versioned JSON/ZIP exchange-package design was superseded by the current
> Excel-workbook export/import below and no longer exists in the codebase. This section
> describes the current design only.

The application exports a sanitized workbook the user can review or edit offline in any
spreadsheet tool. It does not require any integrated service, API keys, or a specific provider.

The intended workflow is:

1. Penumbra Organizer exports a sanitized inventory workbook.
2. The user reviews or edits the workbook offline (by hand, or with any external tool of their
   choosing).
3. The app imports and validates the edited workbook.
4. The user reviews all changes.
5. Only the app performs backup, dry run, validation, and Apply.

An imported workbook may only suggest changes. It must never directly alter the live installation.

## Current implementation

A real first milestone exists in the repository root.

Implemented:

* public GitHub repository
* MIT license
* public README
* user README
* contribution and security documentation
* issue and pull-request templates
* solution and project structure
* Penumbra discovery
* read-only scanning
* schema fingerprint foundations
* compatibility service foundations
* creator canonicalization service
* protection service
* WPF inventory screen
* dependency injection
* structured domain models
* organization preference, proposal source, and manual override model foundations
* explicit versioned workbook export and proposal models
* strict workbook export package validation
* read-only workbook import proposal validation service
* first in-memory manual organizer workspace in the WPF app
* proposed folder tree, strategy selection, bulk visible-row assignment, protection marking, changes-only view, undo, and redo foundations
* selected-row organizer actions with explicit all-visible actions
* centralized in-memory organizer mutation service
* read-only organizer proposal validation service
* atomic organizer session persistence under `%LocalAppData%\PenumbraOrganizer\Sessions`
* Review Changes foundation with validation status rows, immutable dry-run planning, backup preparation, and guarded Apply actions
* verified backup foundation under `%LocalAppData%\PenumbraOrganizer\Backups`
* immutable backup manifests and rollback transaction records
* exact-byte rollback executor with conflict detection and post-rollback verification
* operation-history persistence with package rebuilding
* authoritative `sort_order.json` mapping, deterministic expected-result generation, write preflight, atomic Apply, and post-Apply verification
* read-only Backups screen foundation with backup verification and backup-folder access
* controlled live-test selection with a default limit of 3 mods
* explicit controlled-test Apply confirmation and post-Apply Penumbra UI observation capture
* incomplete-operation detection with re-verify, continue-verification, rollback, and detail-view recovery actions
* fixture-based tests
* self-contained single-file publishing
* release ZIP generation
* SHA-256 output
* public alpha prerelease
* smoke launch of the published EXE

The current repository also contains an inventory export service and WPF controls for creating a review workbook. The export model includes organization preferences for future strategy-aware review. A read-only proposal validation service exists. The WPF app now has a selected-row in-memory manual organizer slice, centralized mutation/history services, atomic session persistence, guarded Review Changes dry-run/apply controls, and a read-only Backups foundation. Drag-and-drop, richer folder tree interactions, GUI workbook import, and any wider write target beyond `sort_order.json` remain part of the safe organizer milestone unless separately completed and verified.

Existing projects:

* `PenumbraOrganizer.App`
* `PenumbraOrganizer.Core`
* `PenumbraOrganizer.Infrastructure`
* `PenumbraOrganizer.Tests`

Important existing files include:

### Documentation

* `docs/ARCHITECTURE.md`
* `docs/BACKUP_AND_ROLLBACK_FORMAT.md`
* `docs/DRY_RUN_AND_APPLY_FORMAT.md`
* `docs/HANDOFF_ROLLBACK_FOUNDATION.md`
* `docs/ORGANIZER_SESSION_FORMAT.md`
* `docs/PENUMBRA_DISCOVERY.md`
* `docs/SAFETY_AND_ROLLBACK.md`
* `docs/COMPATIBILITY_MODEL.md`

### Core

* `PenumbraOrganizer.Core/Models/DomainModels.cs`
* `PenumbraOrganizer.Core/Interfaces/Services.cs`
* `PenumbraOrganizer.Core/Services/CreatorCanonicalizer.cs`
* `PenumbraOrganizer.Core/Services/ProtectionService.cs`
* `PenumbraOrganizer.Core/Services/SchemaFingerprintService.cs`
* `PenumbraOrganizer.Core/Services/OrganizerMutationService.cs`
* `PenumbraOrganizer.Core/Services/OrganizerProposalValidationService.cs`

### Infrastructure

* `PenumbraOrganizer.Infrastructure/Discovery/PenumbraDiscoveryService.cs`
* `PenumbraOrganizer.Infrastructure/Scanning/PenumbraScanService.cs`
* `PenumbraOrganizer.Infrastructure/Compatibility/PenumbraCompatibilityService.cs`
* `PenumbraOrganizer.Infrastructure/Exports/InventoryExportService.cs`
* `PenumbraOrganizer.Infrastructure/Sessions/OrganizerSessionService.cs`
* `PenumbraOrganizer.Infrastructure/ServiceCollectionExtensions.cs`

### Application

* `PenumbraOrganizer.App/App.xaml.cs`
* `PenumbraOrganizer.App/MainWindow.xaml`
* `PenumbraOrganizer.App/ViewModels/MainViewModel.cs`
* `PenumbraOrganizer.App/ViewModels/ModRowViewModel.cs`
* `PenumbraOrganizer.App/Commands/AsyncRelayCommand.cs`
* `PenumbraOrganizer.App/ExportPackageDialog.xaml`
* `PenumbraOrganizer.App/ExportPackageDialog.xaml.cs`

### Tests

* `PenumbraOrganizer.Tests/Fixtures/TemporaryPenumbraFixture.cs`
* `PenumbraOrganizer.Tests/Discovery/PenumbraDiscoveryServiceTests.cs`
* `PenumbraOrganizer.Tests/Scanning/PenumbraScanServiceTests.cs`
* `PenumbraOrganizer.Tests/Compatibility/SchemaFingerprintServiceTests.cs`
* `PenumbraOrganizer.Tests/Organizer/OrganizerServicesTests.cs`

### Release

* `scripts/publish-release.ps1`
* `README_FOR_USERS.txt`
* `THIRD_PARTY_NOTICES.txt`
* `artifacts/release/package/PenumbraOrganizer.exe`
* `artifacts/release/PenumbraOrganizer-v0.1.0-alpha-win-x64.zip`
* `artifacts/release/SHA256SUMS.txt`

## Supported discovery paths

The current implementation searches:

* `%AppData%\XIVLauncher\pluginConfigs\Penumbra.json`
* `%LocalAppData%\XIVLauncher\pluginConfigs\Penumbra.json`
* `%AppData%\XIVLauncherCN\pluginConfigs\Penumbra.json`
* `%LocalAppData%\XIVLauncherCN\pluginConfigs\Penumbra.json`
* relevant `pluginConfigs\Penumbra` state directories
* sibling `installedPlugins\Penumbra\*\Penumbra.dll`
* sibling Penumbra plugin manifests

Manual validation exists in code, but the GUI manual-selection wizard is not complete.

## Metadata currently recognized

The current scanner recognizes or inspects:

* Penumbra plugin configuration
* mod `meta.json`
* mod `default_mod.json`
* mod `group_*.json`
* collection JSON containing `Name`, `Settings`, and `Inheritance`
* `sort_order.json` `Data` folder entries (folder + display leaf) and `EmptyFolders`
* per-user `mod_data/<id>.json` local data (`Favorite`, `LocalTags`, `Note`)
* unknown JSON fields preserved as raw data in scan memory

Unknown structures should remain readable and preserved. They must not be silently dropped.

## Known assumptions and limitations

* Collection matching currently uses mod display names and can be ambiguous when names are duplicated.
* Top-level mod directories are treated as candidate mod roots and validated using metadata and `sort_order.json`.
* `.pmp` package handling is intentionally out of scope for the installed-library organizer.
* Linux is supported by running this app under Wine/Proton: discovery adds the XIVLauncher.Core base (`$HOME/.xlcore/pluginConfigs/Penumbra.json`, reached via the Wine `Z:` drive mapping) and POSIX mod roots are normalized to the `Z:` drive. Needs validation on a real Linux/Wine install.
* The guarded Apply path writes only `sort_order.json` entries plus per-mod `meta.json` / `mod_data/<id>.json` for metadata edits.
* The legacy `mod_filesystem\organization.json` is not authoritative and is not written.
* The manual folder-picker wizard is not yet complete.
* The visual manual organizer workspace has selected-row actions, folder creation/rename/delete, undo/redo, session persistence, and Review Changes foundations, but drag-and-drop and richer tree interactions remain incomplete.
* Clean-machine release validation remains outstanding.

## Remaining blockers before safe Apply

The following are not yet complete:

* confirming Penumbra reloads `sort_order.json` cleanly after external edits across versions
* widening Apply beyond `sort_order.json`
* persistent compatibility history
* manual detection and folder-selection wizard
* strategy-aware proposal generation and validation
* workbook inventory export package verification for clean-machine release usage
* workbook proposal GUI import flow and user-facing validation preview
* mod-to-folder drag-and-drop
* clean Windows machine validation without an installed development runtime

## Required operation pipeline

The final write pipeline must be:

1. Detect Penumbra.
2. Scan the installed library.
3. Select an organization strategy or start manually.
4. Generate proposals manually, deterministically, or through an imported workbook.
5. Review exact virtual-folder changes.
6. Create and verify a backup.
7. Apply supported virtual-folder metadata changes.
8. Verify and offer rollback.

Do not introduce `.pmp` handling into this workflow.

Apply must remain disabled until:

* a valid scan exists
* the Penumbra version still matches
* supported schemas are recognized
* all protected rows are unchanged
* destinations are valid
* collisions are resolved
* source files still match their scan hashes
* backup completes and verifies successfully
* the dry-run plan is current

Writes must use same-directory temporary files, format validation, flush-to-disk, and atomic replacement where supported.

Never continue blindly after a structural failure.

## Compatibility behavior

Track separately:

* currently installed Penumbra version
* version used for the current scan
* version used during the last successful Apply
* recognized metadata schema fingerprints
* Penumbra Organizer version

If the Penumbra version changes after scanning:

* invalidate the dry run
* disable Apply
* require a fresh scan

If a schema becomes unknown or incompatible:

* allow read-only scanning and export
* block writes to affected files
* identify the exact affected metadata

An optional official-release check may query only the official `xivdev/Penumbra` release source and must remain informational.

It must not download, replace, or manually install Penumbra.

## User-experience requirements

Beginner mode is the default.

Normal users should see:

* Scan
* Installed Mods
* Organize
* Review Changes
* Backups
* Compatibility

The Organize screen should begin with:

`How would you like your mods organized?`

Show beginner-friendly selectable cards:

* Start Manually
* By creator
* By mod type
* By type and creator
* By creator and type
* Keep my current layout and clean it
* Custom
* External workbook review

Each card should include a small folder-tree example.

After selecting a strategy, show only relevant options.

Examples:

For Creator only:

* Normalize obvious creator spellings
* Keep mods with unknown creators where they are
* Put unknown creators in Review

For Type only:

* Review category suggestions
* Keep uncertain mods where they are
* Put uncertain mods in Review

For Type and Creator:

* Choose order: Type -> Creator or Creator -> Type
* Normalize creator spellings
* Choose behavior for unknown values

Common options:

* Preserve protected folders exactly
* Preserve meaningful existing folders
* Flatten temporary source folders
* Preview organization before making changes

Primary actions:

* `Preview Organization`
* `Edit Manually`
* `Export Workbook`

The intended manual beginner workflow is:

1. Scan My Mods
2. Open Organize
3. Choose a starting strategy or `Start Manually`
4. Create folders or use existing folders
5. Drag mods or use bulk assignment
6. Resolve Needs Review items
7. Open Review Changes
8. Back up and apply

Spreadsheet exports may exist only as optional advanced reporting. Do not require spreadsheet review before Apply, and do not use a spreadsheet as the primary editor.

Review Changes must show the organization source for every proposed path:

* Manual
* Deterministic rule
* Imported external suggestion
* Preserved current path

Show resolved components separately where applicable:

* Proposed type
* Proposed creator
* Final proposed Penumbra folder

Do not show irrelevant columns in Beginner mode. For example, hide Proposed type when Creator-only mode is selected.

Technical data should be hidden behind an Advanced mode.

Errors must be written in plain language with direct actions.

Do not show stack traces or raw JSON by default.

Core operation must remain offline.

## Current authoritative write target

The current project and fixtures now prove the authoritative live write targets:

* organization: `sort_order.json`, key = mod directory name, value = full path (folder + display leaf); `EmptyFolders` is authoritative for empty folders
* author metadata: `<ModDirectory>\<mod dir name>\meta.json`
* per-user local data: `mod_data\<mod dir name>.json`

The installed top-level physical mod directory name is the stable scan ID and the mapping used by the scanner and dry-run/apply pipeline. The same string is the physical folder name, the `sort_order.json` `Data` key, and the `mod_data/<id>.json` filename.

The legacy `mod_filesystem\organization.json` is not authoritative and is not written. A move preserves the mod's existing `sort_order.json` display leaf so reorganization never silently renames it in Penumbra.

The current app also includes:

* explicit `Validate My Installation` read-only validation
* `Controlled Test Apply` for tiny explicitly selected live tests
* guarded rollback from the Backups screen for valid completed operations
* strict workbook proposal import with manual-override precedence
* privacy-conscious diagnostic export
* visible incomplete-operation recovery guidance after interruption

The dry-run and Apply format is documented in:

* `docs/BACKUP_AND_ROLLBACK_FORMAT.md`
* `docs/DRY_RUN_AND_APPLY_FORMAT.md`

## Development rules

* Production-quality implementation only
* No demo data
* No fake fallback services
* No silent behavior changes
* No destructive defaults
* No writes during scanning
* No filesystem logic in ViewModels
* No assumptions hidden from documentation
* No modification of the user's real installation in automated tests
* Tests must use temporary fixtures
* Build and run tests after meaningful changes
* Update this context document when major architectural decisions change

## Session startup procedure

At the beginning of every future development session:

1. Verify the workspace is the repository root.
2. Read `docs/PROJECT_CONTEXT.md`.
3. Inspect the current Git status.
4. Inspect recent changes before modifying files.
5. Build the solution.
6. Run the relevant tests.
7. Report existing failures before making unrelated changes.
