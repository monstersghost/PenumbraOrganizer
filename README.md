# Penumbra Organizer

A beginner-friendly, unofficial Windows utility for viewing and organizing Penumbra virtual mod folders without physically moving mod files.

## Current status

**Early alpha preview**

The current build includes:

* automatic Penumbra discovery
* distinction between Penumbra state directory and mod-library root
* installed-mod scanning
* current Penumbra-folder inventory
* manual in-memory folder proposals
* organization strategy selection
* selected-row and bulk proposal actions
* proposed folder creation and rename
* protected mods
* undo and redo
* organizer session saving
* Review Changes validation
* external AI inventory export

This alpha does **not** apply changes to Penumbra yet. It previews and validates organization proposals only.

## What the application does not do

Penumbra Organizer does not:

* modify FFXIV game files
* move physical mod directories
* rewrite textures, models, animations, sounds, or VFX
* edit `.pmp` packages
* require AI
* require command-line knowledge
* currently apply virtual-folder changes in this alpha

## Download

Download the latest package from [GitHub Releases](../../releases).

1. Download `PenumbraOrganizer-v0.1.0-alpha-win-x64.zip`.
2. Extract the ZIP.
3. Double-click `PenumbraOrganizer.exe`.
4. Windows SmartScreen may warn about an unsigned early build. Check that the file came from this repository's Releases page before running it.

The app is self-contained and requires no separate .NET installation.

## How to use the current alpha

1. Open the app.
2. Let it detect Penumbra.
3. Confirm the displayed:
   * Penumbra state directory
   * Mod library root
4. Scan installed mods.
5. Open Organize.
6. Choose:
   * Start manually
   * By creator
   * By mod type
   * By type and creator
   * By creator and type
   * Keep current layout
   * Custom
   * External AI review
7. Create proposed folders and assign selected mods.
8. Use Undo or Redo as needed.
9. Open Review Changes.
10. Remember that this alpha previews and validates organization proposals but does not yet apply them.

## External AI workflow

AI is optional. The app can export a sanitized ZIP that users upload to their AI provider. The AI can suggest virtual folders only.

A future app version will import and validate returned proposals. The current alpha creates the export package but does not yet provide a GUI import workflow.

## Safety model

* Scanning is read-only.
* Proposal changes are held in app memory and app-owned session files.
* Protected mods cannot be changed.
* Physical mod assets remain untouched.
* Later Apply support will require validation and a verified backup.

## Screenshots

Screenshots are not included yet.

## Requirements

* Windows 10 or Windows 11 x64
* XIVLauncher/Dalamud with Penumbra for real scanning
* no separate .NET installation
* internet not required for core local features

## Known limitations

The current alpha does not yet include:

* applying changes to Penumbra
* live dry run
* verified backup
* post-Apply verification
* rollback
* GUI AI proposal import
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

Documentation:

* [Project context](docs/PROJECT_CONTEXT.md)
* [Architecture](docs/ARCHITECTURE.md)
* [Penumbra discovery](docs/PENUMBRA_DISCOVERY.md)
* [Safety and rollback](docs/SAFETY_AND_ROLLBACK.md)
* [Compatibility model](docs/COMPATIBILITY_MODEL.md)
* [Organizer session format](docs/ORGANIZER_SESSION_FORMAT.md)

## Disclaimer

Penumbra Organizer is an unofficial third-party project. It is not affiliated with Square Enix, XIVLauncher, Dalamud, or the Penumbra maintainers.

Users are responsible for respecting mod authors' licenses and distribution rules. Do not redistribute paid or restricted mods.

## License

MIT License. See [LICENSE](LICENSE).
