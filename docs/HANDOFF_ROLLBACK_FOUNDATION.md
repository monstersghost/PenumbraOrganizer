# Penumbra Organizer - Recovery Foundation Handoff

## Repository

* Repository URL: `https://github.com/monstersghost/PenumbraOrganizer`
* Workspace root: `F:\PenumbraOrganizer`
* Current branch: `main`
* Current tag/release: `v0.1.0-alpha`

## Current build state

* Build result: solution build passes with 0 warnings and 0 errors.
* Test count: 92/92 tests pass.
* Apply status: guarded Apply is implemented only for supported `mod_data.db` virtual-folder writes.
* Live write status: no physical mod, collection, `.pmp`, plugin, or FFXIV write path is exposed.

## Delivered in this session

The rollback-first milestone and guarded dry-run/apply foundation are implemented:

* recovery domain models in `PenumbraOrganizer.Core/Models/RecoveryModels.cs`
* recovery service interfaces in `PenumbraOrganizer.Core/Interfaces/Services.cs`
* verified backup creation in `PenumbraOrganizer.Infrastructure/Recovery/BackupService.cs`
* backup verification in `PenumbraOrganizer.Infrastructure/Recovery/BackupVerificationService.cs`
* rollback transaction persistence and exact-byte restore in `PenumbraOrganizer.Infrastructure/Recovery/RollbackService.cs`
* rollback verification in `PenumbraOrganizer.Infrastructure/Recovery/RollbackVerificationService.cs`
* operation-history rebuilding in `PenumbraOrganizer.Infrastructure/Recovery/OperationHistoryService.cs`
* shared atomic JSON persistence and safe package-path handling in `PenumbraOrganizer.Infrastructure/Recovery`
* authoritative `mod_data.db` virtual-folder mapping and expected-result generation in `PenumbraOrganizer.Infrastructure/Apply/PenumbraVirtualFolderWriter.cs`
* immutable dry-run planning and invalidation in `PenumbraOrganizer.Infrastructure/Apply/DryRunPlanner.cs` and `PlanInvalidationService.cs`
* write-permission preflight in `PenumbraOrganizer.Infrastructure/Apply/WritePermissionPreflightService.cs`
* guarded Apply executor and post-Apply verification in `PenumbraOrganizer.Infrastructure/Apply/ApplyService.cs` and `PostApplyVerificationService.cs`
* read-only Backups screen foundation in `PenumbraOrganizer.App/ViewModels/BackupsViewModel.cs` and `PenumbraOrganizer.App/MainWindow.xaml`
* Review Changes dry-run/apply controls in `PenumbraOrganizer.App/ViewModels/MainViewModel.cs` and `PenumbraOrganizer.App/MainWindow.xaml`
* fixture-only recovery tests in `PenumbraOrganizer.Tests/Recovery/RecoveryServicesTests.cs`
* fixture-only dry-run/apply tests in `PenumbraOrganizer.Tests/Apply/DryRunAndApplyTests.cs`

## Safety invariants now enforced

* backup creation accepts only explicit file lists
* protected files are rejected from writable backup requests
* protected files are rejected from rollback transactions
* protected rows never generate writable dry-run operations
* backup files are length-verified and SHA-256-verified
* expected JSON files must parse
* Apply writes only exact planned bytes for `mod_data.db`
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
* `plan.json`
* `apply.json`
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

The UI now exposes guarded `Create Dry Run`, `Create Backup`, and `Apply Virtual-Folder Changes` actions in Review Changes.

The UI now also exposes:

* `Validate My Installation`
* guarded `Roll Back Changes` for valid restorable operations
* strict AI proposal import
* diagnostic export

The UI still does not expose:

* force restore
* any broader write target than `mod_data.db` `LocalModData.Folder`

## Remaining blockers before safe Apply

The main remaining blockers before wider safe public Apply exposure are:

* proving whether `mod_filesystem\organization.json` is ever required for immediate Penumbra UI consistency
* deciding whether Penumbra rebuilds any derived folder-tree presentation automatically
* deliberate public-release validation of the guarded Apply UI path
* broader real-installation validation beyond explicit user authorization

These milestones must continue to avoid physical mod movement, collection editing, `.pmp` handling, and real-installation test access.
