namespace DotnetSdkTui.Models;

/// <summary>
/// Represents a single .NET workload row shown in the Workloads panel.
/// Merges "installed" state (from <c>dotnet workload list</c>) with "available"
/// state (from <c>dotnet workload search</c>) into one record.
/// </summary>
public sealed record WorkloadInfo(
    string Id,
    string Description,
    bool IsInstalled,
    string? InstalledManifestVersion,
    string? InstallationSource,
    bool UpdateAvailable);

/// <summary>
/// Snapshot of the workload host environment, rendered in the panel header
/// so users always know which SDK / feature band / update mode is in effect.
/// </summary>
public sealed record WorkloadEnv(
    string WorkloadVersion,
    string UpdateMode,
    string ActiveSdkVersion,
    string ActiveFeatureBand);
