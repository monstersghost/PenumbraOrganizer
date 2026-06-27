# Penumbra Organizer - Recovery Foundation Handoff

## Repository

* Repository URL: `https://github.com/monstersghost/PenumbraOrganizer`
* Workspace root: `F:\PenumbraOrganizer`
* Current branch: `main`
* Current tag/release: `v0.1.0-alpha`

## Current build state

* Build result: solution build passes with 0 warnings and 0 errors.
* Test count: 75/75 tests pass.
* Apply status: Apply remains unavailable and clearly disabled in the app.
* Live write status: no live Penumbra write path is exposed in the public alpha UI.

## Delivered in this session

The rollback-first milestone is implemented:

* recovery domain models in `PenumbraOrganizer.Core/Models/RecoveryModels.cs`
* recovery service interfaces in `PenumbraOrganizer.Core/Interfaces/Services.cs`
* verified backup creation in `PenumbraOrganizer.Infrastructure/Recovery/BackupService.cs`
* backup verification in `PenumbraOrganizer.Infrastructure/Recovery/BackupVerificationService.cs`
* rollback transaction persistence and exact-byte restore in `PenumbraOrganizer.Infrastructure/Recovery/RollbackService.cs`
* rollback verification in `PenumbraOrganizer.Infrastructure/Recovery/RollbackVerificationService.cs`
* operation-history rebuilding in `PenumbraOrganizer.Infrastructure/Recovery/OperationHistoryService.cs`
* shared atomic JSON persistence and safe package-path handling in `PenumbraOrganizer.Infrastructure/Recovery`
* read-only Backups screen foundation in `PenumbraOrganizer.App/ViewModels/BackupsViewModel.cs` and `PenumbraOrganizer.App/MainWindow.xaml`
* fixture-only recovery tests in `PenumbraOrganizer.Tests/Recovery/RecoveryServicesTests.cs`

## Safety invariants now enforced

* backup creation accepts only explicit file lists
* protected files are rejected from writable backup requests
* protected files are rejected from rollback transactions
* backup files are length-verified and SHA-256-verified
* expected JSON files must parse
* rollback restores exact backed-up bytes
* live files are not overwritten when the current hash differs from both the expected applied hash and original backup hash
* operation history can be rebuilt from operation packages
* automated tests use temporary fixture directories only

## Storage layout

The implemented package layout is:

`%LocalAppData%\PenumbraOrganizer\Backups\<operation-id>\`

Contents:

* `operation.json`
* `manifest.json`
* `rollback.json`
* `verification.json`
* `files\...`
* `logs\...`

Schema and behavior details are documented in:

`docs/BACKUP_AND_ROLLBACK_FORMAT.md`

## UI status

The app now includes a read-only `Backups` tab that can:

* list backup operations
* show beginner-facing summary columns
* show affected files for the selected operation
* re-run backup verification
* open the backup folder

The UI still does not expose:

* live Apply
* public rollback execution
* force restore

## Remaining blockers before safe Apply

The main remaining write milestones are:

* immutable dry-run planner
* atomic Apply pipeline
* post-Apply verification
* compatibility-backed write invalidation
* guarded public rollback execution only after end-to-end validation

These milestones must continue to avoid physical mod movement, collection editing, `.pmp` handling, and real-installation test access.
