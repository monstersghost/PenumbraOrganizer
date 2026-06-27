namespace PenumbraOrganizer.Core.Services;

using System.Text;
using System.Text.Json;
using PenumbraOrganizer.Core.Models;

public static class SchemaFingerprintService
{
    public static SchemaFingerprint Create(string fileName, string json, IReadOnlySet<string>? requiredProperties = null)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var sb = new StringBuilder();
            BuildFingerprint(doc.RootElement, sb);
            var notes = new List<string>();
            if (requiredProperties is not null && doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var required in requiredProperties)
                {
                    if (!doc.RootElement.TryGetProperty(required, out _))
                        notes.Add($"Missing required property: {required}");
                }
            }

            var difference = notes.Count > 0 ? SchemaDifferenceKind.MissingKnownRequiredField : SchemaDifferenceKind.None;
            return new SchemaFingerprint(fileName, sb.ToString(), difference, notes);
        }
        catch (JsonException ex)
        {
            return new SchemaFingerprint(fileName, "invalid-json", SchemaDifferenceKind.RootStructureChange, new[] { ex.Message });
        }
    }

    public static SchemaDifferenceKind Compare(string baseline, string candidate)
    {
        if (baseline.Equals(candidate, StringComparison.Ordinal))
            return SchemaDifferenceKind.None;
        if (candidate.StartsWith(baseline, StringComparison.Ordinal))
            return SchemaDifferenceKind.AdditiveOptionalChange;
        return SchemaDifferenceKind.TypeChange;
    }

    private static void BuildFingerprint(JsonElement element, StringBuilder builder)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                builder.Append('{');
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    builder.Append(property.Name);
                    builder.Append(':');
                    BuildFingerprint(property.Value, builder);
                    builder.Append(';');
                }
                builder.Append('}');
                break;
            case JsonValueKind.Array:
                builder.Append('[');
                if (element.GetArrayLength() > 0)
                    BuildFingerprint(element[0], builder);
                builder.Append(']');
                break;
            case JsonValueKind.String:
                builder.Append("string");
                break;
            case JsonValueKind.Number:
                builder.Append("number");
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                builder.Append("bool");
                break;
            case JsonValueKind.Null:
                builder.Append("null");
                break;
            default:
                builder.Append(element.ValueKind.ToString());
                break;
        }
    }
}
