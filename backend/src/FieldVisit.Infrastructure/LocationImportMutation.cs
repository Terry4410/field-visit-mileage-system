using System.Text.Json;

namespace FieldVisit.Infrastructure;

/// <summary>Versioned, applied business evidence, independent of notification settings.</summary>
public static class LocationImportMutation
{
    public sealed record Mutation(int LocationId, string TransitionKind, string? PriorApprovalStatus, bool EnteredPending);
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static JsonElement Source(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return (root.TryGetProperty("envelopeVersion", out var version) && version.GetInt32() == 1
            ? root.GetProperty("source") : root).Clone();
    }

    public static bool IsAppliedEnvelope(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("envelopeVersion", out var version) && version.GetInt32() == 1
            && document.RootElement.TryGetProperty("appliedMutation", out _);
    }

    public static string Applied(string originalJson, Mutation mutation)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("envelopeVersion", 1);
            writer.WritePropertyName("source");
            writer.WriteRawValue(Source(originalJson).GetRawText());
            writer.WritePropertyName("appliedMutation");
            JsonSerializer.Serialize(writer, mutation, Options);
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
