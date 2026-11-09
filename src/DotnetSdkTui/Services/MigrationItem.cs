namespace DotnetSdkTui.Services;

/// <summary>
/// An SDK or runtime installed outside dotnetup — unmanaged in the active dotnet folder, or in an
/// "Other location" folder — that can be migrated to dotnetup (or, for other locations, deleted).
/// </summary>
/// <param name="ListedPath">The path <c>dotnet --list-sdks/--list-runtimes</c> printed for it.</param>
/// <param name="Framework">Shared framework name for runtimes (e.g. Microsoft.NETCore.App); null for SDKs.</param>
internal sealed record MigrationItem(
    string Version, string Channel, string ListedPath, bool IsOtherLocation, string? Framework = null)
{
    public bool IsSdk => Framework is null;

    /// <summary>The dotnet folder the item lives in.</summary>
    public string Root => InstallRemoval.DotnetRootOf(ListedPath);

    public string Display => IsSdk ? $"SDK {Version}" : $"{Framework} {Version}";
}

/// <summary>A dotnetup migration staged by a view: the dotnetup arguments plus what is being migrated.</summary>
internal sealed record MigrationRequest(string Args, string? Note, IReadOnlyList<MigrationItem> Items);

internal static class MigrationTargets
{
    /// <summary>
    /// The dotnetup-managed SDK that replaces <paramref name="item"/> after a migration: the same version when
    /// dotnetup installed it, else the newest managed SDK in its channel (bulk migration installs the latest
    /// patch). Null when there is none.
    /// </summary>
    internal static Models.SdkInfo? PickTargetSdk(MigrationItem item, IEnumerable<Models.SdkInfo> tracked)
    {
        List<Models.SdkInfo> sdks = tracked
            .Where(t => t.Component.Equals("SDK", StringComparison.OrdinalIgnoreCase))
            .ToList();
        return sdks.FirstOrDefault(t => t.Version.Equals(item.Version, StringComparison.OrdinalIgnoreCase))
            ?? sdks.Where(t => t.Version.StartsWith(item.Channel + ".", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(t => t.Version, Comparer<string>.Create(SdkSearchService.CompareSdkVersions))
                .FirstOrDefault();
    }
}
