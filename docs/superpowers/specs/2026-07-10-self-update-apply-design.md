# Self-Update: Download, Verify, and Apply — Design

**Status:** approved by user, 2026-07-10. Not yet implemented.

**Context:** the already-shipped "Check for Updates" feature (`IUpdateCheckService`,
`GitHubUpdateCheckService`) is informational only — it tells the user a newer release exists and
links to the GitHub release page. This design expands it into an actual one-click update: download
the new release, verify it against the real published checksums, and apply it in place, replacing
the running app's own files. This is a materially different risk class from anything else in this
app (which otherwise only ever touches Penumbra's own config files, always through
backup → verify → apply → rollback). The design below carries that same safety discipline over to
updating the app's own binary.

## Confirmed facts (not assumptions)

- `SHA256SUMS.txt` is genuinely published as a GitHub release **asset** (not just a local file in
  `artifacts/release/`) — confirmed via `gh api repos/monstersghost/PenumbraOrganizer/releases/tags/v0.3.3-beta`,
  which lists it alongside the zip in `.assets[]`. Its content (confirmed via direct download) is
  standard `sha256sum`-style output, one line per file: `<hex-hash> *<filename>`, with a UTF-8 BOM
  at the start of the file. Both the zip and the bundled `PenumbraOrganizer.exe` get their own line.
- `scripts/publish-release.ps1` (current, post-item-5) publishes `PenumbraOrganizer.exe`,
  `README_FOR_USERS.txt`, `THIRD_PARTY_NOTICES.txt`, `LICENSE`, and `docs/HOW_TO_USE.pdf` into the
  zip, then computes SHA-256 for the zip and the exe into `SHA256SUMS.txt`.
- `MainViewModel` already has everything needed to preserve in-progress work across a forced
  restart: `SaveSessionAsync()` (`MainViewModel.cs:2055`) calls
  `_organizerSessionService.SaveLastSessionAsync(BuildSessionDocument(), CancellationToken.None)`
  and is safe to call even mid-session; `BuildSessionDocument()` (`MainViewModel.cs:2106`) builds
  the full session record.
- The existing confirmation-dialog convention in this codebase is a private static helper like
  `ConfirmAllVisible(string message)` (`MainViewModel.cs:2052`) wrapping
  `MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question)`.
- `App.xaml.cs` already calls `Shutdown(-1)` for fatal startup errors — a graceful update-triggered
  shutdown will use `Application.Current.Shutdown()` (exit code 0) instead.
- `IUpdateCheckService`/`GitHubUpdateCheckService`/`UpdateCheckResult` currently only parse
  `tag_name`, `html_url`, `draft` from each GitHub release — no asset URLs are captured yet.

## Critical rollout constraint

Self-update only works for users updating **from** a version that already ships
`PenumbraOrganizer.Updater.exe` next to the main exe. The currently-released v0.3.3-beta does not
have it. The "Update Now" flow must check whether the updater exists in the current install
directory (`AppContext.BaseDirectory`) and, if not, fall back to the existing behavior (open the
GitHub release page in the browser) instead of attempting — and failing — the automated flow.

## Architecture

### 1. `PenumbraOrganizer.Updater` — new project

A tiny, self-contained, single-file .NET 8 console app (same publish flags as the main app, so end
users still never need a .NET runtime installed), added to the solution and bundled in every
release zip going forward. Invoked by the main app as it shuts down:

```
PenumbraOrganizer.Updater.exe --pid <mainAppPid> --source "<extractedNewReleaseFolder>" --dest "<currentInstallFolder>"
```

Behavior:
1. Wait for the given PID to fully exit (`Process.GetProcessById(pid).WaitForExit()`, tolerating
   `ArgumentException` if it's already gone).
2. Retry-with-backoff before touching files — Windows can briefly hold a file handle open for a
   moment after a process exits.
3. Rename the current `PenumbraOrganizer.exe` in `dest` to `PenumbraOrganizer.exe.old` (the
   rollback safety net — mirrors this app's own backup-before-write philosophy, applied to itself).
4. Copy every file from `source` into `dest`, overwriting.
5. On success: delete `PenumbraOrganizer.exe.old`, delete `source` (the temp extraction folder),
   write `update-log.txt` in `dest` recording success + the new version, relaunch
   `PenumbraOrganizer.exe` from `dest`, exit.
6. On any copy failure: restore `PenumbraOrganizer.exe.old` back to `PenumbraOrganizer.exe`, write
   `update-log.txt` recording the failure and reason, do **not** relaunch (leave the old, working
   version in place), exit non-zero.

### 2. Extend `IUpdateCheckService` to expose asset URLs

`GitHubReleaseDto` gains an `Assets` list (`name`, `browser_download_url`). `UpdateCheckResult`
gains two new nullable fields: `ZipDownloadUrl` and `ChecksumsDownloadUrl`, resolved by matching
asset names ending in `.zip` and equal to `SHA256SUMS.txt` respectively. Both are `null` if the
release doesn't have them (e.g. a hypothetical future release with different asset naming) — the
UI must treat that as "can't self-update this one, only inform," not throw.

### 3. New `IAppUpdateService` / `AppUpdateService`

Owns the actual download → verify → extract → invoke-updater sequence, kept separate from
`IUpdateCheckService` (which stays a cheap, read-only "is there something newer" check). Method:

```csharp
Task<AppUpdatePrepareResult> PrepareUpdateAsync(UpdateCheckResult update, IProgress<string>? progress, CancellationToken cancellationToken);
```

Steps: download the zip to a temp file → download `SHA256SUMS.txt` → parse expected hashes →
compute the zip's actual SHA-256 and compare (abort with a clear reason on mismatch) → extract the
zip to a temp folder → compute the extracted `PenumbraOrganizer.exe`'s SHA-256 and compare against
its line in the checksums file too (defense in depth — matches the release process's own practice
of hashing both the zip and the exe) → return the extracted folder path on success, or a failure
reason on any step failing. Never partially apply anything — this method only prepares files in a
temp location; it never touches the live install.

### 4. `MainViewModel` — the "Update Now" flow

New `UpdateNowCommand`, enabled only when `UpdateCheckResult.UpdateAvailable` is true. Handler:

1. Check whether `PenumbraOrganizer.Updater.exe` exists next to the running exe
   (`AppContext.BaseDirectory`). If not, fall back to opening `ReleaseUrl` in the browser (today's
   behavior) and stop — do not attempt anything below.
2. Show a confirmation dialog (matching the `ConfirmAllVisible` pattern): *"Update to v{X.Y.Z}?
   Penumbra Organizer will close and restart. Your current session will be saved first."* —
   OK/Cancel. Cancel aborts with no side effects.
3. Call `SaveSessionAsync()` so in-progress manual organizing survives the restart.
4. Run `IAppUpdateService.PrepareUpdateAsync(...)` inside the existing `RunBusyAsync` pattern, with
   `ProgressMessage` updates ("Downloading update...", "Verifying update...", "Extracting
   update..."). Any failure shows a clear error via `MessageBox.Show` and stops — the app keeps
   running normally, nothing was touched.
5. On success: launch `PenumbraOrganizer.Updater.exe` with the current process ID, the extracted
   folder, and `AppContext.BaseDirectory` as arguments, detached (`UseShellExecute = false`, no
   window). Then `Application.Current.Shutdown()`.

On the **next** startup, `MainViewModel`/`App.xaml.cs` should check for `update-log.txt` in
`AppContext.BaseDirectory`; if present, surface its one-line result (success or failure reason) in
the activity log, then delete it — closes the loop so the user has confirmation the update actually
completed, without building a whole new "operation history" entry for it.

### 5. `MainWindow.xaml`

An "Update Now" button next to the existing "Check for Updates" button (added by the already-shipped
item 3), visible only when `UpdateCheckResult.UpdateAvailable` is true.

### 6. `scripts/publish-release.ps1`

Also publish `PenumbraOrganizer.Updater` (same self-contained single-file flags as the main app),
copy its exe into the package folder, include it in the zip, and add its hash as a third line in
`SHA256SUMS.txt`.

## Global Constraints

- No silent/automatic updates — every step requires the explicit "Update Now" click and the
  confirmation dialog. This is a one-click *manual* trigger, not a background auto-updater.
- Never delete or overwrite the current, working install until the new files are fully downloaded,
  checksum-verified, and extracted to a temp location. The `.old` rename in the Updater is the
  final safety net on top of that.
- If any step fails, the app must be left in its original, fully working state — never a partial
  update.
- No new NuGet packages beyond what's already used (`HttpClient`, `System.Text.Json`,
  `System.IO.Compression` — all in the base class library already).

## Next steps

This becomes its own implementation plan (`docs/superpowers/plans/2026-07-10-self-update-apply.md`),
separate from the already-shipped items 1–5.
