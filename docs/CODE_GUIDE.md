# Code Guide

A developer-oriented map of the Penumbra Organizer codebase: how the projects fit
together, where the important types live, and how a change flows from a click to a
written `sort_order.json`. For the higher-level design rationale and safety boundaries,
see [ARCHITECTURE.md](ARCHITECTURE.md); this document is about navigating the source.

## Solution layout

The solution (`PenumbraOrganizer.sln`) is a four-project layered architecture. Dependencies
point inward: `App → Infrastructure → Core`. `Core` depends on nothing else in the solution.

| Project | Role | Depends on |
| --- | --- | --- |
| `PenumbraOrganizer.Core` | Domain models, service interfaces, pure logic (no I/O) | — |
| `PenumbraOrganizer.Infrastructure` | Interface implementations: filesystem, Penumbra parsing, apply, backup | Core |
| `PenumbraOrganizer.App` | WPF/MVVM desktop shell | Core, Infrastructure |
| `PenumbraOrganizer.Tests` | xUnit tests (unit + temp-dir integration) | all |

The dependency rule is the most important thing to preserve: **Core never references
Infrastructure or App**, and the App talks to Infrastructure only through interfaces
declared in Core. If you need a new capability, declare the interface in
`Core/Interfaces/Services.cs`, implement it in Infrastructure, and register it in DI.

## Core (`PenumbraOrganizer.Core`)

Pure domain layer. No file or network access lives here — these are records, enums, and
deterministic helpers that are cheap to unit-test.

### Interfaces — `Interfaces/Services.cs`

Every service the App consumes is declared here as an interface. This single file is the
contract surface between App and Infrastructure. Grouped by concern:

- **Discovery & scanning:** `IPenumbraDiscoveryService`, `IPenumbraScanService`, `IPenumbraCompatibilityService`
- **Organize (in-memory editing):** `IOrganizerMutationService`, `IOrganizerProposalValidationService`, `IOrganizerSessionService`, `ICreatorCanonicalizer`, `IProtectionService`
- **Workbook exchange:** `IWorkbookWorkflowService`
- **Dry run & apply:** `IDryRunPlanner`, `IDryRunValidationService`, `IPenumbraVirtualFolderWriter`, `IApplyService`, `IWritePermissionPreflightService`, `IPlanInvalidationService`, `IPostApplyVerificationService`
- **Live-installation safety:** `IRealInstallationValidationService`, `IControlledLiveTestService`
- **Backup, rollback & history:** `IBackupService`, `IBackupVerificationService`, `IRollbackService`, `IRollbackVerificationService`, `IOperationHistoryService`, `IOperationRecoveryService`, `IOperationObservationService`
- **Diagnostics:** `IDiagnosticExportService`

### Models — `Models/*.cs`

| File | Holds |
| --- | --- |
| `DomainModels.cs` | Core nouns and enums: `PenumbraInstallation`, `ScanInventory`, `ModScanResult`, `VirtualFolderNode`, `OrganizationPreferences`, `OrganizationStrategy`, `CompatibilityReport`, `ProposalSource` |
| `OrganizerModels.cs` | The in-memory editing model: `OrganizerModProposal`, `OrganizerFolder`, `OrganizerMutationResult`, `OrganizerHistoryEntry` (undo/redo), `OrganizerValidationResult`, `OrganizerSessionDocument` |
| `DryRunModels.cs` | `DryRunPlan` and its entries/file-change/fingerprint snapshots, plus `ProposalSnapshot` (the immutable plan handed into the apply pipeline) |
| `RecoveryModels.cs` | Backup/rollback/operation-history records, incomplete-operation records, observation status |
| `IntegrationModels.cs` | Live-installation safety and diagnostics inputs/outputs: controlled-test options/candidates, `RealInstallationValidation*`, diagnostic export request/result |
| `WorkbookWorkflowModels.cs` | Workbook export/import request and result types |

A few names worth knowing because they thread through everything:

- **`ScanInventory`** — the read-only snapshot of the live Penumbra install (mods, current virtual folders, collections). The input to organizing, validation, dry run, and apply.
- **`OrganizerModProposal`** — one row in the Organize grid: a mod plus its *proposed* destination folder and protection flag. This is the editable working state.
- **`ProposalSnapshot`** — an immutable capture of the proposed plan handed across the App/Infrastructure boundary into the dry-run → apply pipeline.
- **`DryRunPlan`** — the validated, file-level description of exactly what apply will write, produced before any bytes are touched.

### Services — `Services/*.cs`

The Core logic that is pure enough to live without Infrastructure:

- `OrganizerMutationService` — applies edits to the in-memory proposal/folder lists (assign, return-to-current, protect, create/rename/delete folder) and the `ApplyUndo`/`ApplyRedo` inverse operations. The engine behind every Organize-tab button.
- `OrganizerProposalValidationService` — validates a proposed plan against the inventory (collisions, protected paths, empty folders) and produces the Review Changes result.
- `CreatorCanonicalizer` — normalizes creator names so "Bizu" and "bizu_" collapse to one folder.
- `ProtectionService` — decides whether a virtual-folder path is protected (immutable).
- `SchemaFingerprintService` — hashes Penumbra's on-disk schema so the dry run can detect if the format changed under us.

## Infrastructure (`PenumbraOrganizer.Infrastructure`)

Implements the Core interfaces. Organized by concern into folders:

- **`Discovery/`** — `PenumbraDiscoveryService` finds the Penumbra config directory and mod-library root, with confidence scoring and manual-override validation.
- **`Scanning/`** — `PenumbraScanService` reads the live install into a `ScanInventory` (read-only).
- **`Penumbra/`** — `PenumbraSortOrder` models `sort_order.json`, the file that actually stores virtual-folder organization.
- **`Compatibility/`** — `PenumbraCompatibilityService` checks the detected Penumbra version/schema against what the app supports.
- **`Apply/`** — the write pipeline (see below): `DryRunPlanner`, `DryRunValidationService`, `PenumbraVirtualFolderWriter`, `ApplyService`, plus guards (`WritePermissionPreflightService`, `PlanInvalidationService`, `RealInstallationValidationService`, `PostApplyVerificationService`, `ControlledLiveTestService`).
- **`Recovery/`** — backup, rollback, and operation history. `BackupService`/`RollbackService` do the work; `OperationHistoryService` indexes past operations; `OperationRecoveryService` handles operations interrupted mid-flight; `AtomicJsonFileStore` provides crash-safe writes. `RecoveryStorageLayout` defines the on-disk folder structure under `%LocalAppData%\PenumbraOrganizer`.
- **`Exports/`** — `WorkbookWorkflowService` exports the inventory + plan to an Excel workbook (ClosedXML) and imports an edited one back, validating against the live inventory.
- **`Sessions/`** — `OrganizerSessionService` persists/restores the in-memory Organize session so work survives a restart.
- **`Diagnostics/`** — `DiagnosticExportService` bundles sanitized logs/state for issue reports.
- **`ServiceCollectionExtensions.cs`** — `AddPenumbraOrganizerInfrastructure()` registers every implementation as a singleton. This is the one place to wire a new service.

## App (`PenumbraOrganizer.App`)

WPF desktop shell using MVVM. No business logic lives in views — they bind to view models,
which call Core interfaces resolved from DI.

### Startup — `App.xaml.cs`

`OnStartup` builds the `ServiceCollection`, calls `AddPenumbraOrganizerInfrastructure()`,
registers `MainViewModel`, `BackupsViewModel`, and `MainWindow`, then shows the window.
`StartupBootstrapLogger` records staged progress so a startup crash leaves a breadcrumb
trail; crash handlers are wired for `AppDomain`, `TaskScheduler`, and dispatcher exceptions.

### View models — `ViewModels/`

- **`MainViewModel`** — the orchestrator (~2,500 lines). Holds the injected Core services, the observable collections behind the grid (`ICollectionView` for filtered/selected/changed mods), and every `ICommand`. The commands map directly to UI buttons — e.g. `ScanCommand`, `SelectStrategyCommand`, `AssignSelectedToSelectedFolderCommand`, `CreateDryRunCommand`, `CreateBackupCommand`, `ApplyVirtualFolderChangesCommand`, `BackupAndApplyCommand`, `UndoCommand`/`RedoCommand`. If you want to understand a UI action, find its command here and follow it to the service call.
- **`BackupsViewModel`** — the Backups tab: list, verify, restore (rollback), open-folder.
- **`ModRowViewModel`** / **`OrganizerFolderViewModel`** — per-row and per-folder wrappers for the grid and folder tree.
- **`ObservableObject`** — `INotifyPropertyChanged` base.

### Commands & behaviors

- `Commands/RelayCommand.cs` and `AsyncRelayCommand.cs` — the `ICommand` implementations (sync and async). Async commands disable themselves while running.
- `Behaviors/SelectedItemsBehavior.cs` — bridges multi-select in the `DataGrid` (which isn't bindable by default) to the view model.

### Views & dialogs

`MainWindow.xaml` is the tabbed shell (Home/Scan, Organize, Review Changes, Apply, Backups).
`Dialogs/` holds modal flows: `ModMetadataDialog`, `ControlledTestDialog`,
`ApplyTestConfirmationDialog`, `PenumbraObservationDialog`.

## The apply pipeline

The write path is deliberately staged so nothing touches Penumbra until every guard passes.
Reading the interfaces in this order in `Core/Interfaces/Services.cs` traces the whole flow:

1. **Scan** — `IPenumbraScanService` produces a read-only `ScanInventory`.
2. **Organize** — `IOrganizerMutationService` edits the in-memory proposals; `IOrganizerProposalValidationService` feeds Review Changes. Nothing is written.
3. **Snapshot** — the App captures a `ProposalSnapshot` from the current proposals.
4. **Dry run** — `IDryRunPlanner.CreatePlanAsync` builds a `DryRunPlan` (the exact file changes), backed by `IPenumbraVirtualFolderWriter` which captures source files and schema fingerprints. `IDryRunValidationService` and `IPlanInvalidationService` confirm the plan is still valid against the live install.
5. **Preflight** — `IWritePermissionPreflightService` checks the target files are writable; `IRealInstallationValidationService` confirms the live install still matches the plan.
6. **Backup** — `IBackupService.CreateBackupAsync` copies the live Penumbra config to a verified, timestamped operation package before any write.
7. **Apply** — `IApplyService.PrepareAsync` then `ApplyAsync` writes the new `sort_order.json` atomically.
8. **Verify** — `IPostApplyVerificationService` confirms the result matches the plan.
9. **Recover / roll back** — if an operation is interrupted, `IOperationRecoveryService` surfaces it and `IRollbackService` restores the backup; `IOperationObservationService` records the user's "did Penumbra look right?" confirmation.

`IControlledLiveTestService` is a narrowed variant of this flow that applies a tiny,
reversible subset first, for cautious real-installation testing.

The on-disk formats produced by these stages are specified in
[DRY_RUN_AND_APPLY_FORMAT.md](DRY_RUN_AND_APPLY_FORMAT.md) and
[BACKUP_AND_ROLLBACK_FORMAT.md](BACKUP_AND_ROLLBACK_FORMAT.md).

## Where data lives at runtime

All app-owned state is under `%LocalAppData%\PenumbraOrganizer\` — sessions, settings,
logs, operation backups, and manual "Back Up My Penumbra" snapshots. The layout is defined
by `RecoveryStorageLayout`. The app never writes inside the FFXIV or Penumbra install except
the guarded `sort_order.json` write during Apply.

## How to make common changes

- **Add a new service** — declare the interface in `Core/Interfaces/Services.cs`, implement under the matching Infrastructure folder, register it in `ServiceCollectionExtensions.cs`, then inject it into a view model.
- **Add a new Organize action** — add a method to `IOrganizerMutationService` (with its undo/redo inverse), expose a `RelayCommand` on `MainViewModel`, and bind a button in `MainWindow.xaml`.
- **Change what Apply writes** — work in `Apply/PenumbraVirtualFolderWriter.cs`, and keep `DryRunPlanner` in sync so the dry run predicts the same changes.
- **Touch a domain shape** — edit the relevant record in `Core/Models`; the compiler will walk you through the call sites.

## Testing

`PenumbraOrganizer.Tests` mixes pure unit tests (Core services, canonicalization,
validation, mutation/undo) with temp-directory integration tests that exercise discovery,
scanning, backup, and apply against synthesized Penumbra fixtures. Run the whole suite with:

```powershell
dotnet test .\PenumbraOrganizer.sln
```
