namespace PenumbraOrganizer.Core.Services;

using PenumbraOrganizer.Core.Interfaces;

public sealed class ProtectionService : IProtectionService
{
    public bool IsProtectedPath(string currentVirtualFolder)
    {
        if (string.IsNullOrWhiteSpace(currentVirtualFolder))
            return false;

        var normalized = currentVirtualFolder.Trim().Replace('\\', '/').Trim('/');
        var firstSegment = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

        return firstSegment.Equals("0", StringComparison.OrdinalIgnoreCase)
               || firstSegment.Equals("Protected", StringComparison.OrdinalIgnoreCase)
               || firstSegment.Equals("Locked", StringComparison.OrdinalIgnoreCase)
               || firstSegment.Equals("Do Not Move", StringComparison.OrdinalIgnoreCase);
    }
}
