# Penumbra Discovery

`IPenumbraDiscoveryService` discovers candidate Penumbra installations from local XIVLauncher data and manual user selection.

## Automatic search

The service checks known XIVLauncher-style roaming-data locations, including:

- `%AppData%\XIVLauncher\pluginConfigs\Penumbra.json`
- `%AppData%\XIVLauncher\pluginConfigs\Penumbra\`
- `%AppData%\XIVLauncher\installedPlugins\Penumbra\`

The preferred configuration source is `Penumbra.json`, because real Penumbra configuration includes `ModDirectory`.

## Evidence model

Each candidate installation returns:

- configuration path
- Penumbra config root
- configured mod root
- plugin assembly path
- plugin manifest path
- assembly/file/informational version when available
- confidence
- evidence list
- warnings

## Manual fallback

If detection fails or multiple candidates are valid, the app allows the user to choose:

- Penumbra config file
- Penumbra mod root
- Penumbra plugin assembly

Selections are validated structurally:

- config must parse as JSON and contain Penumbra-like fields
- mod root must exist and contain Penumbra-style mod metadata
- plugin directory must contain `Penumbra.dll` or `Penumbra.json`

## Unsupported paths

Paths that look like Wine or compatibility-layer Linux layouts are reported as unsupported in version 1. They may be inventoried later, but the app does not write to them in milestone 1.
