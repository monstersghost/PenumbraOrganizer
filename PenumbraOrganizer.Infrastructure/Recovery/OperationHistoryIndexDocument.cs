using System.Text.Json.Serialization;
using PenumbraOrganizer.Core.Models;

namespace PenumbraOrganizer.Infrastructure.Recovery;

internal sealed class OperationHistoryIndexDocument
{
    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("operations")]
    public required IReadOnlyList<OperationHistoryEntry> Operations { get; init; }
}
