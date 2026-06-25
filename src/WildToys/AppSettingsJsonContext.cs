using System.Text.Json.Serialization;

namespace WildToys;

/// <summary>
/// Source-generated serialization metadata for <see cref="AppSettings"/>. Using a
/// <see cref="JsonSerializerContext"/> keeps settings persistence working under
/// PublishTrimmed (reflection-based serialization silently drops trimmed members).
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppSettings))]
internal partial class AppSettingsJsonContext : JsonSerializerContext
{
}
