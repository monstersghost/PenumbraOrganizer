namespace PenumbraOrganizer.Infrastructure.Apply;

using LiteDB;
using PenumbraOrganizer.Core.Models;

internal sealed record PenumbraModDataEntry(
    string Id,
    string Folder,
    BsonDocument Document);

internal sealed record PenumbraModDataState(
    string DatabasePath,
    DryRunSourceFileSnapshot SourceFile,
    SchemaFingerprint SchemaFingerprint,
    IReadOnlyDictionary<string, PenumbraModDataEntry> Entries,
    int RecordCount);
