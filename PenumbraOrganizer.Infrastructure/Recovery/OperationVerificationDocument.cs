using System.Text.Json.Serialization;
using PenumbraOrganizer.Core.Models;

namespace PenumbraOrganizer.Infrastructure.Recovery;

internal sealed record OperationVerificationDocument
{
    [JsonPropertyName("backupVerification")]
    public BackupVerificationResult? BackupVerification { get; init; }

    [JsonPropertyName("rollbackVerification")]
    public RollbackVerificationResult? RollbackVerification { get; init; }
}
