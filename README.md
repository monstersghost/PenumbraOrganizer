# Penumbra Organizer

A beginner-friendly, unofficial Windows utility for viewing and organizing Penumbra virtual mod folders without physically moving mod files.

## Current status

**Workbook-first beta**

The current build includes:

* automatic Penumbra discovery
* distinction between Penumbra state directory and mod-library root
* installed-mod scanning
* workbook export beside the portable executable
* workbook import with hidden-key validation
* reviewable virtual-folder diffs
* protected mods
* verified manual backups
* one-button Backup and Apply
* post-Apply verification
* rollback from Backups after restart

The current beta applies supported virtual-folder changes to `mod_data.db` only. Physical mod files are not moved.

## What the application does not do

Penumbra Organizer does not:

* modify FFXIV game files
* move physical mod directories
* rewrite textures, models, animations, sounds, or VFX
* edit `.pmp` packages
* require command-line knowledge
* write `organization.json`

## Download

Download the latest package from [GitHub Releases](../../releases).

1. Download `PenumbraOrganizer-v0.2.0-beta-win-x64.zip`.
2. Extract the ZIP.
3. Double-click `PenumbraOrganizer.exe`.
4. Windows SmartScreen may warn about an unsigned early build. Check that the file came from this repository's Releases page before running it.

The app is self-contained and requires no separate .NET installation.

## How to use the current beta

1. Open the app.
2. Let it detect Penumbra.
3. Scan installed mods.
4. Export the workbook.
5. Edit `mod type`, `protected`, and `destination` in Excel.
6. Import the workbook.
7. Review the proposed changes.
8. Click `Backup and Apply`.
9. Use Backups to restore a previous operation if needed.

## Safety model

* Scanning is read-only.
* Workbook edits are validated against a hidden internal key before import.
* Protected mods cannot be changed.
* Physical mod assets remain untouched.
* Apply creates a fresh verified backup immediately before writing.
* Rollback restores exact backup bytes and skips conflicts by default.

## Screenshots

Screenshots are not included yet.

## Requirements

* Windows 10 or Windows 11 x64
* XIVLauncher/Dalamud with Penumbra for real scanning
* no separate .NET installation
* internet not required for core local features

## Known limitations

The current beta still does not include:

* physical mod movement
* collection editing
* `.pmp` handling
* `organization.json` writes

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

Documentation:

* [Project context](docs/PROJECT_CONTEXT.md)
* [Architecture](docs/ARCHITECTURE.md)
* [Penumbra discovery](docs/PENUMBRA_DISCOVERY.md)
* [Safety and rollback](docs/SAFETY_AND_ROLLBACK.md)
* [Compatibility model](docs/COMPATIBILITY_MODEL.md)
* [Organizer session format](docs/ORGANIZER_SESSION_FORMAT.md)
* [Rollback foundation handoff](docs/HANDOFF_ROLLBACK_FOUNDATION.md)

## Disclaimer

Penumbra Organizer is an unofficial third-party project. It is not affiliated with Square Enix, XIVLauncher, Dalamud, or the Penumbra maintainers.

Users are responsible for respecting mod authors' licenses and distribution rules. Do not redistribute paid or restricted mods.

## License

MIT License. See [LICENSE](LICENSE).
