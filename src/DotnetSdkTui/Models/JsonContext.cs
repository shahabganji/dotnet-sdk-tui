using System.Text.Json.Serialization;

namespace DotnetSdkTui.Models;

[JsonSerializable(typeof(SdkListResponse))]
[JsonSerializable(typeof(DotnetUpInfo))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class AppJsonContext : JsonSerializerContext
{
}
