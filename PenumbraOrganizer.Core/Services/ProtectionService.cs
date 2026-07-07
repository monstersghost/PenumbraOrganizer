namespace PenumbraOrganizer.Core.Services;

using PenumbraOrganizer.Core.Interfaces;

public sealed class ProtectionService : IProtectionService
{
    public bool IsProtectedPath(string currentVirtualFolder) => false;
}
