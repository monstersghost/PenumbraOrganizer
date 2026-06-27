namespace PenumbraOrganizer.Infrastructure.Apply;

using PenumbraOrganizer.Core.Models;

internal sealed record ApplyPackageDocument(
    ApplyOperation? Operation,
    ApplyResult? Result);
