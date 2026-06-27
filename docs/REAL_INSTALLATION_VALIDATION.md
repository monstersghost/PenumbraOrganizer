# Real Installation Validation

`Validate My Installation` is a user-authorized read-only workflow for checking a real Penumbra setup before any guarded Apply attempt.

## What it does

When explicitly triggered in the GUI, the app may:

* detect the Penumbra state directory
* detect the mod-library root
* read the installed Penumbra version
* scan installed mods from `mod_data.db`
* map proposed changes to authoritative `LocalModData` records
* create a fresh immutable dry run
* run exact-target permission checks
* detect likely FFXIV/XIVLauncher/Dalamud blockers

## What it does not do

This workflow does not:

* Apply automatically
* move physical mod directories
* modify mod assets
* edit collections
* access the real installation during automated tests

## Authorization boundary

The workflow requires explicit user action from the GUI.

If authorization is not explicit, validation is blocked.

## Output

The validation summary reports:

* mods scanned
* proposed changes
* records mapped
* missing records
* duplicate or ambiguous records
* protected mods
* unsupported structures
* write permission status
* running-process blockers
* whether the installation currently appears safe for Apply
