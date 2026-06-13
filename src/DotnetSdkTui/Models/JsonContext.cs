using System.Text.Json.Serialization;

namespace DotnetSdkTui.Models;

/// <summary>
/// Source-generated JSON serialization context for dotnetup and dotnet CLI models.
/// </summary>
[JsonSerializable(typeof(SdkListResponse))]
[JsonSerializable(typeof(DotnetUpInfo))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class AppJsonContext : JsonSerializerContext
{
}
