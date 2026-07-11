# Handoff: manual Penumbra config selection left PluginAssemblyPath null (LiteDB.dll not found)

_Started and fixed: 2026-07-08. Status: fix applied and tested, ready to merge._

## The bug report

User (Linux/NixOS, running PenumbraOrganizer 0.3.1 via Lutris + Proton 11.0) hit this error
when pressing "Backup and Apply" after sorting by mod type:

```
The operation could not be completed safely.
Penumbra's LiteDB engine (LiteDB.dll) could not be
found next to the installed Penumbra plugin, so
mod_data.db could not be read.
```

Repro steps from the report:
1. Open Penumbra Organizer
2. Override Penumbra Config (auto-detect didn't find it) → browse to
   `Z:\home\user\.xlcore\pluginConfigs\Penumbra.json`
3. Sort method: "By mod type"
4. Review Changes → Backup and Apply
5. Error occurs, every time

User confirmed `LiteDB.dll` genuinely exists on their machine, at
`/home/user/.xlcore/installedPlugins/Penumbra/1.6.1.10/LiteDB.dll`, so the file is there; the
app just isn't finding it.

## Root cause

The app supports two Penumbra storage backends, picked per-install by comparing on-disk mtimes
(`PenumbraOrganizationBackendSelector.Detect` in
`PenumbraOrganizer.Infrastructure/Penumbra/PenumbraOrganizationBackendSelector.cs`):
`sort_order.json` or the older LiteDB-based `mod_data.db`. For the `mod_data.db` backend,
`LiteDbAssemblyLoader.TryLoad` (`PenumbraOrganizer.Infrastructure/Penumbra/LiteDbAssemblyLoader.cs`)
dynamically loads the *installed plugin's own* `LiteDB.dll`, found by looking next to
`PenumbraInstallation.PluginAssemblyPath`, rather than a NuGet-restored copy, so the on-disk
format written always matches whatever LiteDB version that specific Penumbra build uses.

`PluginAssemblyPath` is populated two different ways depending on how the install was found:

- **Auto-discovery** (`PenumbraDiscoveryService.TryDiscoverFromBasePath`) actively searches
  `<base>/installedPlugins/Penumbra/*/Penumbra.dll` and sets `PluginAssemblyPath` from what it
  finds.
- **Manual config selection** (`MainViewModel.ChoosePenumbraConfigAsync`, used when the user
  clicks "Override Penumbra Config") calls
  `IPenumbraDiscoveryService.ValidateManualSelectionAsync(configPath, null, null, ...)`,
  always passing `null` for `pluginAssemblyPath`. `ValidateManualSelectionAsync` never
  independently searched for the plugin, so it just echoed back whatever `null` it was given.

Result: anyone who has to browse to `Penumbra.json` manually (auto-discovery not finding their
install, as here, on Lutris/Proton/NixOS) ends up with `PluginAssemblyPath == null` even though
the plugin, and `LiteDB.dll` next to it, is really on disk. `LiteDbAssemblyLoader.TryLoad`'s
first check (`if (string.IsNullOrWhiteSpace(installation.PluginAssemblyPath)) return null;`)
then bails out immediately, and the app reports "LiteDB.dll could not be found" even though it
never actually looked in the right place.

## Fix applied

`PenumbraOrganizer.Infrastructure/Discovery/PenumbraDiscoveryService.cs`:

- Added `ResolvePluginAssemblyPath(string configPath, string? providedPluginAssemblyPath)`:
  returns the provided path if non-empty, otherwise derives the install's base directory from
  the config path (`<base>/pluginConfigs/Penumbra.json` → two levels up) and searches
  `<base>/installedPlugins/Penumbra/*/Penumbra.dll` via the existing `FindNewestPluginAssembly`
  helper, the same logic auto-discovery already uses.
- `ValidateManualSelectionAsync` now runs the incoming `pluginAssemblyPath` through this
  resolver before using it for `PluginAssemblyPath`, `PluginManifestPath`, and version lookup.

This only changes behavior when the caller passes `null`/empty. The `ChooseModsFolderAsync`
call site (which passes `_installation.PluginAssemblyPath` from a prior resolution) is
unaffected since a non-empty path always short-circuits to itself.

### Test added

`PenumbraOrganizer.Tests/Discovery/PenumbraDiscoveryServiceTests.cs`:
`ValidateManualSelectionAsync_ResolvesPluginAssemblyFromConfigPathWhenNotProvided`: asserts
that calling `ValidateManualSelectionAsync(configPath, null, null, ...)` against a fixture with
a real plugin manifest/assembly on disk now resolves `PluginAssemblyPath` correctly.

Verified: all 5 tests in `PenumbraOrganizer.Tests/Discovery/` pass.

## Known conflict for whoever merges this

The `PenumbraOrganizer.Tests` project **currently does not build** on `main` because of
in-progress, uncommitted work for the mod-category overhaul (see
`docs/HANDOFF_MOD_CATEGORY_OVERHAUL.md`): specifically,
`PenumbraOrganizer.Tests/Scanning/PenumbraScanServiceTests.cs` references a
`PenumbraOrganizer.Core.Classification` namespace/`ModCategory` enum that doesn't exist yet.
That file (and an unrelated one-line addition to `MainViewModel.cs` for a
`"By mod type (detailed)"` strategy string) were already modified and uncommitted before this
fix started, **not touched by this fix**. To verify this fix in isolation, the broken test file
was temporarily `git stash`ed, tests run, then restored untouched.

When merging: this fix (`PenumbraDiscoveryService.cs` + its test) is independent and safe to
take as-is. The build will stay broken until the mod-category overhaul branch's `Classification`
namespace actually lands, or that test file is reverted/finished.

## Files changed

| File | Change |
|---|---|
| `PenumbraOrganizer.Infrastructure/Discovery/PenumbraDiscoveryService.cs` | Added `ResolvePluginAssemblyPath`; `ValidateManualSelectionAsync` uses it |
| `PenumbraOrganizer.Tests/Discovery/PenumbraDiscoveryServiceTests.cs` | New regression test |
