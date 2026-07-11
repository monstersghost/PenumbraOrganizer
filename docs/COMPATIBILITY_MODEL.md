# Compatibility model

`IPenumbraCompatibilityService` evaluates whether a scanned Penumbra installation is still safe to write.

## Inputs

- installed Penumbra version
- scanned Penumbra version
- application version
- metadata schema fingerprints
- recognized and unknown metadata structures

## Statuses

- `Compatible`
- `VersionChangedSchemaKnown`
- `VersionChangedNeedsReview`
- `UnknownSchema`
- `PenumbraNotFound`
- `ConfigurationInvalid`

## Schema fingerprints

Fingerprints are based on structure, not values:

- property names
- broad JSON value types
- object vs array roots
- required known properties
- optional known properties

Classifications:

- additive optional change
- missing known required field
- type change
- root structure change
- unrecognized file type

Only missing required fields, type changes, root changes, and unsupported writable structures block apply in milestone 1.
