# Contributing

Thanks for helping improve Penumbra Organizer.

## Before You Start

This project is an early alpha. Please keep changes focused, conservative, and safe for ordinary users.

Do not add code that:

* writes to live Penumbra files without the planned validation, backup, and rollback pipeline
* moves physical mod directories
* edits FFXIV game files
* parses or repacks `.pmp` packages as part of the organizer workflow
* includes private mod data, exported inventories, user sessions, logs, credentials, or local paths

## Development Setup

```powershell
dotnet restore
dotnet build .\PenumbraOrganizer.sln
dotnet test .\PenumbraOrganizer.sln
```

## Pull Requests

Please include:

* what changed
* why it changed
* safety impact
* tests run
* screenshots only when they contain no private paths or mod information

Keep UI wording beginner-friendly. Avoid exposing raw metadata, schema details, or stack traces in normal user flows.
