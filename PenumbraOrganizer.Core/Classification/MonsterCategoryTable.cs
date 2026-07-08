namespace PenumbraOrganizer.Core.Classification;

using System.Text.Json;

public static class MonsterCategoryTable
{
    private const string ResourceName = "PenumbraOrganizer.Core.Classification.MonsterCategoryTable.json";

    private static readonly Lazy<IReadOnlyDictionary<string, ModCategory>> Table = new(Load);

    public static bool TryGetCategory(string modelId, out ModCategory category)
        => Table.Value.TryGetValue(modelId, out category);

    private static IReadOnlyDictionary<string, ModCategory> Load()
    {
        using var stream = typeof(MonsterCategoryTable).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"{ResourceName} embedded resource was not found.");

        using var document = JsonDocument.Parse(stream);
        var result = new Dictionary<string, ModCategory>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in document.RootElement.EnumerateObject())
        {
            var value = entry.Value.GetString();
            if (value is not null && Enum.TryParse<ModCategory>(value, out var category))
                result[entry.Name] = category;
        }

        return result;
    }
}
