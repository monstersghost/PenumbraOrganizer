# Penumbra Organizer - Rollback Foundation Handoff

## Repository

* Repository URL: `https://github.com/monstersghost/PenumbraOrganizer`
* Workspace root: `F:\PenumbraOrganizer`
* Current branch: `main`
* Current tag/release: `v0.1.0-alpha`
* Initial public commit: `298e7fa Initial public alpha of Penumbra Organizer`
* Latest handoff commit: see repository HEAD after `Add rollback-first development handoff`

## Current build state

* Build result: solution build passes with 0 warnings and 0 errors.
* Test count: 42/42 tests pass.
* Release status: `v0.1.0-alpha` public prerelease exists with `PenumbraOrganizer-v0.1.0-alpha-win-x64.zip`.
* Apply status: Apply remains unavailable and clearly disabled in the app.
* Live write status: no live Penumbra write path is implemented.

## Existing architecture to reuse

Use these existing files and concepts before adding new recovery code:

* Organizer domain models: `PenumbraOrganizer.Core/Models/OrganizerModels.cs`
* General domain models: `PenumbraOrganizer.Core/Models/DomainModels.cs`
* Service interfaces: `PenumbraOrganizer.Core/Interfaces/Services.cs`
* Organizer mutation service: `PenumbraOrganizer.Core/Services/OrganizerMutationService.cs`
* Organizer proposal validation service: `PenumbraOrganizer.Core/Services/OrganizerProposalValidationService.cs`
* Organizer session service: `PenumbraOrganizer.Infrastructure/Sessions/OrganizerSessionService.cs`
* Compatibility service: `PenumbraOrganizer.Infrastructure/Compatibility/PenumbraCompatibilityService.cs`
* Dependency-injection registration: `PenumbraOrganizer.Infrastructure/ServiceCollectionExtensions.cs`
* WPF shell and current Review Changes UI: `PenumbraOrganizer.App/MainWindow.xaml`
* Main view model: `PenumbraOrganizer.App/ViewModels/MainViewModel.cs`
* Safety documentation: `docs/SAFETY_AND_ROLLBACK.md`
* Session documentation: `docs/ORGANIZER_SESSION_FORMAT.md`

There is no complete Backups screen yet. The next session should add a read-only Backups screen foundation that lists backup and rollback operations and their status without enabling Apply.

## Objective

Build the recovery subsystem first so all future writes are reversible by design.

## Scope for the next session

Include only:

* rollback domain models
* verified backup engine
* backup manifest
* rollback transaction record
* rollback executor
* rollback conflict detection
* atomic restoration
* backup and rollback verification
* read-only Backups screen foundation
* fixture-backed tests
* documentation updates

## Explicitly out of scope

* enabling live Apply
* changing live Penumbra files
* AI proposal GUI import
* drag-and-drop
* collection editing
* `.pmp` handling
* physical mod movement
* game-file changes
* automatic Penumbra update
* general UI redesign

## Proposed domain models

Recommended model names:

* `BackupOperation`
* `BackupManifest`
* `BackupFileEntry`
* `RollbackTransaction`
* `RollbackFileEntry`
* `RollbackConflict`
* `RollbackResult`
* `RollbackStatus`
* `BackupVerificationResult`

The next session may adjust names to match existing conventions.

## Recommended interfaces

Recommended interfaces:

* `IBackupService`
* `IBackupVerificationService`
* `IRollbackService`
* `IRollbackVerificationService`
* `IOperationHistoryService`

The next session should first inspect existing interfaces to avoid duplication.

## Safety invariants

* no backup means no future Apply
* a backup is usable only after verification
* rollback restores exact backed-up bytes
* rollback checks current hash before overwrite
* conflicts never overwrite silently
* protected files must never appear in a writable plan
* rollback records are immutable after operation finalization
* backup and rollback records do not depend on display names
* backup paths are stored safely and preferably relative within the operation package
* temporary fixture tests must never access the real Penumbra installation

## Recommended storage layout

Use:

`%LocalAppData%\PenumbraOrganizer\Backups\<operation-id>\`

Example contents:

* `operation.json`
* `manifest.json`
* `rollback.json`
* `verification.json`
* `files\...`
* `logs\...`

Do not store full mod assets.

## Rollback conflict behavior

* Current hash equals expected applied hash: safe automatic restore.
* Live file missing: restore only when the manifest identifies a previously existing target and the backup is valid.
* Current hash differs: mark conflict and do not overwrite by default.
* Backup missing or corrupt: block restore.
* User chooses force restore: Advanced-only explicit confirmation, fully logged.

## Required tests

At minimum add tests for:

* valid backup succeeds
* backup hash mismatch fails
* missing backup fails
* exact rollback succeeds
* modified live file causes conflict
* missing live file behavior
* partial transaction rollback
* protected file exclusion
* rollback idempotence or safe second-run behavior
* interrupted rollback recovery
* JSON restoration validation
* operation-history persistence
* no access to live installation

## Definition of done

The next milestone is done when:

* backup and rollback APIs exist
* all tests use temporary fixtures
* backups are verifiable
* rollback can restore simulated modified files exactly
* conflicts are reported safely
* a read-only Backups view can list operations and status
* no live Apply path is enabled
* build passes
* all tests pass
* docs are updated

## Suggested first task

1. inspect existing models and services
2. define operation and rollback models
3. add fixture-backed backup tests
4. implement verified backup copy
5. implement rollback exact-byte restore
6. implement conflict detection
7. add operation-history persistence
8. expose read-only backup history to the GUI
