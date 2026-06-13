using System.Text.Json.Serialization;

namespace DotnetSdkTui.Models;

/// <summary>
/// Represents the JSON response from <c>dotnetup --info --format json</c>.
/// </summary>
public sealed record DotnetUpInfo(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("commit")] string Commit,
    [property: JsonPropertyName("architecture")] string Architecture,
    [property: JsonPropertyName("rid")] string Rid,
    [property: JsonPropertyName("installations")] List<SdkInfo>? Installations);
