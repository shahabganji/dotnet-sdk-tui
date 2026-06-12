using System.Text.Json.Serialization;

namespace DotnetSdkTui.Models;

public sealed record DotnetUpInfo(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("commit")] string Commit,
    [property: JsonPropertyName("architecture")] string Architecture,
    [property: JsonPropertyName("rid")] string Rid,
    [property: JsonPropertyName("installations")] List<SdkInfo>? Installations);
