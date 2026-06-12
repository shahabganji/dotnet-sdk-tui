using System.Text.Json.Serialization;

namespace DotnetSdkTui.Models;

public sealed record SdkInfo(
    [property: JsonPropertyName("component")] string Component,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("installRoot")] string InstallRoot,
    [property: JsonPropertyName("architecture")] string Architecture)
{
    public bool IsInstalled => true;

    public string DisplayComponent => Component switch
    {
        "sdk" => ".NET SDK",
        "runtime" => "Runtime",
        "aspnetcore" => "ASP.NET Core",
        "windowsdesktop" => "Windows Desktop",
        _ => Component
    };
}

public sealed record SdkListResponse(
    [property: JsonPropertyName("installations")] List<SdkInfo> Installations);
