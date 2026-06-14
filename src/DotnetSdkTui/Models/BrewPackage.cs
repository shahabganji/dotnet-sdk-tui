using System.Text.Json.Serialization;

namespace DotnetSdkTui.Models;

/// <summary>
/// Represents a Homebrew formula, either installed locally or discovered via search.
/// </summary>
/// <param name="Name">Formula name (e.g. "jq").</param>
/// <param name="InstalledVersion">Locally installed version, or null when not installed.</param>
/// <param name="LatestVersion">Latest stable version known to Homebrew, or null when unknown.</param>
/// <param name="Description">Short description, or null when unknown.</param>
/// <param name="IsInstalled">Whether the formula is currently installed.</param>
public sealed record BrewPackage(
    string Name,
    string? InstalledVersion,
    string? LatestVersion,
    string? Description,
    bool IsInstalled);

// ── JSON shapes for `brew info --json=v2 --formula <names>` ──────────────

internal sealed class BrewInfoResponse
{
    [JsonPropertyName("formulae")]
    public List<BrewFormula>? Formulae { get; set; }
}

internal sealed class BrewFormula
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("desc")]
    public string? Desc { get; set; }

    [JsonPropertyName("versions")]
    public BrewVersions? Versions { get; set; }

    [JsonPropertyName("installed")]
    public List<BrewInstalled>? Installed { get; set; }
}

internal sealed class BrewVersions
{
    [JsonPropertyName("stable")]
    public string? Stable { get; set; }
}

internal sealed class BrewInstalled
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }
}
