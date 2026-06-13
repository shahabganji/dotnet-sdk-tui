using System.Text.Json.Serialization;

namespace DotnetSdkTui.Models;

/// <summary>
/// Represents a single .NET SDK or runtime installation reported by dotnetup or dotnet CLI.
/// </summary>
public sealed record SdkInfo(
    [property: JsonPropertyName("component")] string Component,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("installRoot")] string InstallRoot,
    [property: JsonPropertyName("architecture")] string Architecture)
{
    /// <summary>Gets whether this SDK is currently installed on the local machine.</summary>
    public bool IsInstalled => true;

    /// <summary>Gets a human-readable display name for the component type.</summary>
    public string DisplayComponent => Component.ToUpperInvariant() switch
    {
        "SDK" => ".NET SDK",
        "RUNTIME" => "Runtime",
        "ASPNETCORE" => "ASP.NET Core",
        "WINDOWSDESKTOP" => "Windows Desktop",
        _ => Component
    };
}

/// <summary>
/// Represents the JSON response from <c>dotnetup list --format json</c>.
/// </summary>
public sealed record SdkListResponse(
    [property: JsonPropertyName("installations")] List<SdkInfo> Installations);
