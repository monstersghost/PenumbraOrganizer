# Diagnostic Export

`Create Diagnostic Package` generates a privacy-conscious support bundle.

## Included

* app version
* Windows version
* Penumbra version when known
* redacted configuration and mod-library paths
* validation summaries
* operation summaries
* sanitized logs

## Excluded

* mod assets
* live Penumbra databases
* backups
* credentials
* API keys
* absolute user profile paths

## Sanitization

The exporter replaces sensitive paths with markers such as:

* `[profile]`
* `[penumbra-state]`
* `[penumbra-config]`
* `[mod-library]`
* `[mod]`

The package contains summary documents only. It does not bundle live state files.
