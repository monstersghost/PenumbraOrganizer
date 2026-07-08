# Penumbra Organizer

A beginner-friendly, unofficial Windows utility for viewing and reorganizing Penumbra virtual mod folders without physically moving mod files.

## Suport Server on Discord ( I Hope i don't regret this)
https://discord.gg/MhQzVJ65c

## Current status

**0.3.2-beta**

The current build can apply organization changes to Penumbra. It reads Penumbra's
file-based config (`sort_order.json` or `mod_data.db`, collections), lets you reorganize
your virtual folders, and writes the changes back behind a verified backup.

Penumbra has used more than one on-disk format for virtual-folder organization across
versions. This build detects which one your install is actually using (whichever file
Penumbra touched most recently) and reads/writes that one, instead of assuming
`sort_order.json` is always authoritative — so an install on an older or newer Penumbra
version isn't silently shown an empty or stale folder structure.

The current build includes:

* automatic Penumbra discovery
* distinction between Penumbra state directory and mod-library root
* installed-mod scanning (read-only)
* current Penumbra-folder inventory, from whichever storage format (`sort_order.json` or
  `mod_data.db`) your installed Penumbra version is actually using
* manual in-memory folder proposals
* one-click organization strategies that produce a full plan
* selected-row and bulk proposal actions
* proposed folder creation and rename
* protected mods, including one-click protection for an entire current folder
* manual override for the detected Penumbra config path and Mods folder
* undo and redo
* organizer session saving
* Review Changes validation
* verified backup and restore (rollback), backing up the entire Penumbra
  configuration directory before every Apply, not just the file being written
* dry run and guarded Apply
* controlled live-test Apply
* incomplete-operation recovery
* workbook (Excel) export and import for offline review and editing

## What the application does not do

Penumbra Organizer does not:

* modify FFXIV game files
* move physical mod directories
* rewrite textures, models, animations, sounds, or VFX
* edit `.pmp` packages
* edit Penumbra collections, priorities, enabled states, or option groups
* write `organization.json`
* require command-line knowledge

## Download

Download the latest package from [GitHub Releases](../../releases).

1. Download `PenumbraOrganizer-v0.3.2-beta-win-x64.zip`.
2. Extract the ZIP.
3. Double-click `PenumbraOrganizer.exe`.
4. Windows SmartScreen may warn about an unsigned beta build. Check that the file came from this repository's Releases page before running it.

The app is self-contained and requires no separate .NET installation.

## How to use

1. Open the app and let it detect Penumbra.
2. Confirm the displayed Penumbra state directory and mod library root.
3. Click **Scan My Mods**.
4. Open **Sort Method** and choose a strategy:
   * Start manually
   * By creator
   * By mod type
   * Type then creator
   * Creator then type
   * Preserve and clean
   * Custom
5. Open **Current Mods** to search/filter your library and protect or unprotect specific mods
   or folders (Protect Folder/Unprotect Folder act on the folder selected in the current folder
   tree; Protect Selected/Unprotect Selected act on the mods selected in the grid).
6. Open **Proposed Changes** to adjust proposed folders and assignments, using Undo or Redo as
   needed.
7. Open **Review Changes** and resolve anything flagged.
8. **Close FFXIV.**
9. Create a backup, then **Backup and Apply**.
10. If you need to undo a previous operation, open **Backups** and use **Restore Backup**.

## Workbook workflow

The app can **Export Workbook** to an Excel file you can review and edit offline in any
spreadsheet tool, then **Import Workbook** to bring the edited assignments back in. The
import is validated against your live Penumbra inventory before anything is applied. If your
mod library changed since export (a mod moved, or one row no longer matches), only the
affected rows are skipped with a reason — every other valid row still imports. Import is only
fully blocked when nothing in the workbook can be applied at all (wrong Penumbra library,
unsupported format, or missing columns).

## Safety model

* Scanning is read-only.
* Proposal changes are held in app memory and app-owned session files until you apply.
* Apply is guarded by a verified backup and a dry run.
* Protected mods cannot be changed.
* Physical mod assets remain untouched.
* Restore Backup rolls back a previous operation.

## Where the app stores its data

* `%LocalAppData%\PenumbraOrganizer\`
* Sessions, settings, and logs live there.
* Manual Penumbra config backups and diagnostic packages default to a folder under
  `%LocalAppData%\PenumbraOrganizer\`, but both prompt you to choose a save location (and offer
  to open it afterward) each time you create one.

## Screenshots

Screenshots are not included yet.

## Requirements

* Windows 10 or Windows 11 x64
* XIVLauncher/Dalamud with Penumbra for real scanning
* no separate .NET installation
* internet not required for core local features

Linux is supported by running the app under Wine/Proton alongside the game. Discovery
finds XIVLauncher.Core's config at `~/.xlcore/pluginConfigs/Penumbra.json`. This path has
not yet been validated on a real Linux install.

## Known limitations

The current beta does not yet include:

* per-mod metadata editing (out of scope — edit metadata in-game)
* drag-and-drop
* collection editing
* `.pmp` handling

## Reporting issues

Please include:

* Penumbra Organizer version
* Windows version
* Penumbra version
* what you clicked
* the plain-language error
* sanitized logs when requested

Do not upload:

* mod files
* private paid mods
* your entire Penumbra configuration
* personal paths without redaction
* credentials

## Development

```powershell
dotnet restore
dotnet build .\PenumbraOrganizer.sln
dotnet test .\PenumbraOrganizer.sln
.\scripts\publish-release.ps1
```

Technology:

* .NET 8
* WPF
* xUnit
* ClosedXML (workbook export/import)

Documentation:

* [Project context](docs/PROJECT_CONTEXT.md)
* [Architecture](docs/ARCHITECTURE.md)
* [Code guide](docs/CODE_GUIDE.md)
* [Penumbra discovery](docs/PENUMBRA_DISCOVERY.md)
* [Safety and rollback](docs/SAFETY_AND_ROLLBACK.md)
* [Backup and rollback format](docs/BACKUP_AND_ROLLBACK_FORMAT.md)
* [Dry run and apply format](docs/DRY_RUN_AND_APPLY_FORMAT.md)
* [Compatibility model](docs/COMPATIBILITY_MODEL.md)
* [Organizer session format](docs/ORGANIZER_SESSION_FORMAT.md)
* [Sort order and metadata handoff](docs/HANDOFF_SORT_ORDER_AND_METADATA.md)
* [Mod category overhaul handoff (in progress)](docs/HANDOFF_MOD_CATEGORY_OVERHAUL.md)

## Disclaimer

Penumbra Organizer is an unofficial third-party project. It is not affiliated with Square Enix, XIVLauncher, Dalamud, or the Penumbra maintainers.

Users are responsible for respecting mod authors' licenses and distribution rules. Do not redistribute paid or restricted mods.

## License

MIT License. See [LICENSE](LICENSE).
