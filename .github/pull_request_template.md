## Summary

Describe what changed and why.

## Safety

- [ ] Does not move physical mod folders
- [ ] Does not modify FFXIV game files
- [ ] Does not include private mod data, logs, sessions, exports, credentials, or local paths
- [ ] Keeps Apply/backup/rollback behavior accurate for the current alpha

## Tests

List the commands run:

```powershell
dotnet build .\PenumbraOrganizer.sln
dotnet test .\PenumbraOrganizer.sln
```
