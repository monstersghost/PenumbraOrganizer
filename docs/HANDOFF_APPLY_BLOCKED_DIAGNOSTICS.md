# Handoff: Apply-blocked messaging was misleading in three separate ways

_Started and fixed: 2026-07-08. Status: fix applied and tested here (fixture-based tests only),
not yet confirmed against a real user report for the zero-changes case. Continue from "What's NOT
done yet" in a new conversation if further reports come in._

## The bug reports

Three separate, unrelated issues surfaced in the same session while looking at how the "Backup
and Apply" flow reports why it refuses to run:

1. A user screenshot showed `Backup and Apply failed` with `Apply is blocked while these
   processes are running: ffxiv_dx11`, but the user's own Task Manager search for `ffxiv_dx11`
   found nothing running. Diagnosing this required checking whether the app persists anything
   about this failure anywhere retrievable.
2. While tracing the popup text for that same failure, the friendly-message mapping meant to
   describe it turned out to never fire at all.
3. A second screenshot showed `Backup and Apply blocked` with `0 mods will change` in the header,
   but the popup body listed a jumble of unrelated scan warnings (ambiguous display-name matching,
   damaged `default_mod.json`/`group_*.json` files) plus a stray `No folder change.` line, none of
   which explained that the real reason was simply "nothing has been proposed to change yet."

## Root causes and fixes

### 1. `bffbab0`: Backup and Apply failures were invisible to diagnostics

The catch block around `ApplyService.PrepareAsync` (`MainViewModel.cs`, the one wrapping the
FFXIV-running preflight check) was the only failure path in `MainViewModel` that never called
`AppendLog`. Since `PrepareAsync` throws before creating an operation record, nothing about this
failure landed in the Activity Log, the exported diagnostic package's `sanitized-logs.txt`
(`DiagnosticExportService` builds that file from the in-memory `ActivityLog` string, not from
`ILogger`), or operation history. The `ILogger` calls in this file are also a dead end in a
shipped build: logging is wired up as `AddDebug()` only (`App.xaml.cs`), which writes to
`OutputDebugString`, not a file. Net effect: **the diagnostic package would have come back empty**
for exactly the failure a user would ask for help with.

Fix: added the missing `AppendLog(...)` call so the exact failure message (including which
process names were detected) gets a timestamp in the Activity Log and flows into future
diagnostic packages.

### 2. `1b1373a`: Dead pattern match hid the exact blocking-process list by accident

`MainViewModel.ToUserMessage`'s friendly-message switch had:

```csharp
InvalidOperationException invalidOperation when invalidOperation.Message.Contains("blocking process", StringComparison.OrdinalIgnoreCase)
    => "FFXIV is currently running." + Environment.NewLine + "Close the game before applying changes.",
```

The actual error text from `WritePermissionPreflightService` is `"Apply is blocked while these
processes are running: {names}"`; it never contains the substring `"blocking process"`, so this
branch was dead code. Every real occurrence fell through to the generic case
(`"The operation could not be completed safely." + exception.Message`), which is *why* the
original screenshot happened to show the exact process name (by accident, not by design). Fixing
the substring match without also fixing the message would have been a regression: it would have
started matching and replaced the specific process name with a vague sentence that doesn't name
anything.

Fix: corrected the match to `"processes are running"` and kept the exact process list in the
resulting message instead of hiding it.

### 3. `929b8aa`: Zero proposed changes produced a confusing warnings dump

`DryRunValidationService.ValidateAsync` computes `ApplyPermitted` as false when
`plan.FileChanges.Count == 0`, independent of `Validation.Status` (which can still be `Valid` with
zero real errors). `MainViewModel`'s block-check
(`!ApplyPermitted || Status != Valid`) fires on either condition, and
`BuildPlanBlockedMessage` unconditionally dumped `Errors.Concat(Warnings)` into the popup with no
distinction for *why* it was blocked.

Separately (and this is what actually put `No folder change.` in the popup), both
`PenumbraVirtualFolderWriter.MapPlanEntriesAsync` and `ModDataDbVirtualFolderWriter
.MapPlanEntriesAsync` copied every row's `OrganizerValidationRow.Message` into the entry's
`Warnings` unconditionally, including the purely informational messages for `Unchanged`
(`"No folder change."`), `ValidChange` (`"Ready for Review Changes."`), and `Protected`
(`"Protected and unchanged."`) rows. Those messages are display-only Notes-column text, not
warnings: the file provides no other user surface for the Notes column source, and this is why
`Distinct()` didn't hide it: `Unchanged` rows dominate a library where nothing has been organized
yet, so their identical message survives dedup as a single, misleading line in a "blocked" dialog.

Fix:
- Both writers now only add `row.Message` to `entry.Warnings` when the row's status is one that
  actually needs attention (`NeedsReview`, `InvalidPath`, `BlockedProtected`, `MissingMod`,
  `StaleScan`); added a shared-shape `IsNoteworthyRowStatus` helper (duplicated in both writer
  classes, matching this codebase's existing pattern of two independent writer implementations).
- `MainViewModel` now has `HasNoProposedChanges(plan)` (`FileChanges.Count == 0 &&
  Validation.Errors.Count == 0`) and uses it in three places: the popup body
  (`BuildPlanBlockedMessage`), the on-screen `BackupStatus` line, and the persistent
  `ApplyUnavailableReason` text. All three now say something like *"There are no proposed changes
  to apply yet. Choose an organization strategy or assign mods to folders in Organize, then return
  to Review Changes."* instead of the generic blockers dump, when that's genuinely why Apply is
  disabled.

### Test added

`PenumbraOrganizer.Tests/Apply/DryRunAndApplyTests.cs`:
`Plan_WithNoProposedChanges_IsValidWithoutBenignRowNotesAsWarnings`: builds a snapshot with no
changes (every mod proposes its current folder), confirms the plan is `Valid` with zero errors and
zero file changes, and asserts `plan.Warnings` does not contain a "No folder change" message.
Confirmed RED before the writer fix (failed with the exact `"No folder change."` warning present),
GREEN after.

Verified: 199/199 tests pass (full solution), 0 build warnings/errors.

## What's NOT done yet

- Neither the FFXIV-process-block screenshot nor the zero-changes screenshot has been confirmed
  fixed by the actual reporting user; both fixes were derived entirely from code tracing plus the
  screenshots already shared in this session, not a live repro.
- No independent (opus) review was dispatched for this change, unlike the mod_data.db orphan fix
  in `docs/HANDOFF_MOD_DATA_DB_ORPHAN_FIX.md`. Worth doing before merge if that's the team's normal
  bar for Apply-path changes.
- `BuildApplyUnavailableReason` and `BuildPlanBlockedMessage` still don't distinguish "real
  warnings exist but nothing changed" from "genuinely nothing to report"; if a library has actual
  data-integrity warnings (ambiguous display names, damaged JSON) *and* zero proposed changes, the
  dedicated message currently hides those warnings entirely rather than showing both. Not a
  regression (the old behavior buried them in noise too), but worth a follow-up if a report comes
  in about a hidden warning after this fix ships.

## Key files (orientation)

| Concern | File |
|---|---|
| FFXIV-running preflight check | `PenumbraOrganizer.Infrastructure/Apply/WritePermissionPreflightService.cs` |
| Activity Log / diagnostic export gap | `PenumbraOrganizer.App/ViewModels/MainViewModel.cs` (`ApplyVirtualFolderChangesAsync` catch block) |
| Friendly-message mapping | `PenumbraOrganizer.App/ViewModels/MainViewModel.cs` (`ToUserMessage`) |
| Zero-changes detection | `PenumbraOrganizer.App/ViewModels/MainViewModel.cs` (`HasNoProposedChanges`, `BuildPlanBlockedMessage`, `BuildApplyUnavailableReason`) |
| Per-row warning filtering | `PenumbraOrganizer.Infrastructure/Apply/PenumbraVirtualFolderWriter.cs` and `ModDataDbVirtualFolderWriter.cs` (`IsNoteworthyRowStatus`) |
| Regression test | `PenumbraOrganizer.Tests/Apply/DryRunAndApplyTests.cs` (`Plan_WithNoProposedChanges_...`) |
