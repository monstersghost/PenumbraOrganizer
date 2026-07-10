namespace PenumbraOrganizer.Core.Classification;

public static class HeliosphereModDetector
{
    private const string DirectoryPrefix = "hs-";
    private const string MetaFileName = "heliosphere.json";

    public static bool IsHeliosphereManaged(string directoryName, IEnumerable<string> jsonFileNames)
        => (!string.IsNullOrWhiteSpace(directoryName) && directoryName.StartsWith(DirectoryPrefix, StringComparison.OrdinalIgnoreCase))
           || jsonFileNames.Any(fileName => fileName.Equals(MetaFileName, StringComparison.OrdinalIgnoreCase));
}
