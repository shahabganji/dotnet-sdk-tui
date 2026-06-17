using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace DotnetSdkTui.Services;

/// <summary>
/// Represents an available SDK or runtime version discovered from the .NET release metadata.
/// </summary>
public sealed record AvailableSdk(string Version, string ChannelVersion, string SupportPhase, bool IsLatest, string Component = "SDK");

/// <summary>
/// Fetches .NET release metadata from Microsoft's CDN and searches available SDK/runtime versions.
/// </summary>
internal static class SdkSearchService
{
    private const string ReleasesIndexUrl = "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json";
    private static readonly HttpClient HttpClient = new();

    internal static async Task<List<ChannelInfo>> GetChannelsAsync(CancellationToken ct = default)
    {
        string json = await HttpClient.GetStringAsync(ReleasesIndexUrl, ct);
        ReleaseIndex? releaseIndex = JsonSerializer.Deserialize(json, SdkSearchJsonContext.Default.ReleaseIndex);
        return releaseIndex?.ReleasesIndex ?? [];
    }

    /// <summary>
    /// Searches for available SDK and runtime versions matching the given query.
    /// Handles dots in version strings, supports keywords (latest, lts, preview),
    /// and returns both SDK and runtime results.
    /// </summary>
    public static async Task<List<AvailableSdk>> SearchAvailableSdksAsync(string query, CancellationToken ct = default)
    {
        List<ChannelInfo> channels = await GetChannelsAsync(ct);
        string normalizedQuery = query.Trim();

        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return SortResults(CreateAllResults(channels));
        }

        string search = normalizedQuery.ToLowerInvariant();

        // Keyword matching
        if ("latest".StartsWith(search, StringComparison.OrdinalIgnoreCase) && search.Length >= 2)
        {
            var supportedChannels = channels.Where(static channel => IsSupportedChannel(channel.SupportPhase));
            return SortResults(CreateAllResults(supportedChannels));
        }

        if ("lts".StartsWith(search, StringComparison.OrdinalIgnoreCase) && search.Length >= 2)
        {
            var ltsChannels = channels
                .Where(static channel => IsSupportedChannel(channel.SupportPhase))
                .Where(static channel => IsLtsChannel(channel.ChannelVersion));
            return SortResults(CreateAllResults(ltsChannels));
        }

        if ("preview".StartsWith(search, StringComparison.OrdinalIgnoreCase) && search.Length >= 2)
        {
            var previewChannels = channels
                .Where(channel => string.Equals(channel.SupportPhase, "preview", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(channel.SupportPhase, "go-live", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(channel.SupportPhase, "rc", StringComparison.OrdinalIgnoreCase));
            return SortResults(CreateAllResults(previewChannels));
        }

        // "runtime" or "sdk" keyword filter
        if ("runtime".StartsWith(search, StringComparison.OrdinalIgnoreCase) && search.Length >= 3)
        {
            return SortResults(CreateAllResults(channels).Where(r => r.Component != "SDK").ToList());
        }

        // Normalize trailing dots for prefix matching (e.g. "10.0." -> "10.0")
        string prefixQuery = normalizedQuery.TrimEnd('.');

        // Find matching channels
        var matchingChannels = channels
            .Where(c => c.ChannelVersion.StartsWith(prefixQuery, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(c.LatestSdk) && c.LatestSdk.StartsWith(prefixQuery, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(c.LatestRuntime) && c.LatestRuntime.StartsWith(prefixQuery, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (matchingChannels.Count == 0)
        {
            // Fallback: try contains matching on latest results
            var containsMatches = CreateAllResults(channels)
                .Where(sdk => sdk.Version.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return SortResults(containsMatches);
        }

        // Fetch all SDK versions from matching channels for detailed results
        var tasks = matchingChannels
            .Where(c => !string.IsNullOrWhiteSpace(c.ReleasesJsonUrl))
            .Select(c => GetChannelDetailedResultsAsync(c, prefixQuery, ct));

        var allResults = await Task.WhenAll(tasks);
        var results = allResults.SelectMany(r => r).ToList();

        // If no detailed results (API issue), fall back to latest-only
        if (results.Count == 0)
        {
            results = CreateAllResults(matchingChannels)
                .Where(sdk => sdk.Version.StartsWith(prefixQuery, StringComparison.OrdinalIgnoreCase)
                    || sdk.ChannelVersion.StartsWith(prefixQuery, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return SortResults(results);
    }

    /// <summary>
    /// Fetches all SDK and runtime versions from a channel's releases.json.
    /// </summary>
    private static async Task<List<AvailableSdk>> GetChannelDetailedResultsAsync(
        ChannelInfo channel, string prefixQuery, CancellationToken ct)
    {
        try
        {
            string json = await HttpClient.GetStringAsync(channel.ReleasesJsonUrl, ct);
            var channelReleases = JsonSerializer.Deserialize(json, SdkSearchJsonContext.Default.ChannelReleases);

            if (channelReleases?.Releases is null)
                return [];

            var results = new List<AvailableSdk>();
            var seenVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var release in channelReleases.Releases)
            {
                // Add SDK versions
                if (release.Sdk is not null && !string.IsNullOrWhiteSpace(release.Sdk.Version)
                    && seenVersions.Add(release.Sdk.Version)
                    && release.Sdk.Version.StartsWith(prefixQuery, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new AvailableSdk(
                        release.Sdk.Version, channel.ChannelVersion, channel.SupportPhase,
                        string.Equals(release.Sdk.Version, channel.LatestSdk, StringComparison.OrdinalIgnoreCase),
                        "SDK"));
                }

                foreach (var sdk in release.Sdks)
                {
                    if (!string.IsNullOrWhiteSpace(sdk.Version)
                        && seenVersions.Add(sdk.Version)
                        && sdk.Version.StartsWith(prefixQuery, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new AvailableSdk(
                            sdk.Version, channel.ChannelVersion, channel.SupportPhase,
                            string.Equals(sdk.Version, channel.LatestSdk, StringComparison.OrdinalIgnoreCase),
                            "SDK"));
                    }
                }

                // Add runtime version from release
                if (!string.IsNullOrWhiteSpace(release.ReleaseVersion)
                    && seenVersions.Add($"rt-{release.ReleaseVersion}")
                    && release.ReleaseVersion.StartsWith(prefixQuery, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new AvailableSdk(
                        release.ReleaseVersion, channel.ChannelVersion, channel.SupportPhase,
                        string.Equals(release.ReleaseVersion, channel.LatestRuntime, StringComparison.OrdinalIgnoreCase),
                        "Runtime"));
                }
            }

            return results;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Creates results for both latest SDKs and latest runtimes from channels.
    /// </summary>
    private static List<AvailableSdk> CreateAllResults(IEnumerable<ChannelInfo> channels)
    {
        var results = new List<AvailableSdk>();

        foreach (var channel in channels)
        {
            if (!string.IsNullOrWhiteSpace(channel.LatestSdk))
            {
                results.Add(new AvailableSdk(
                    channel.LatestSdk,
                    channel.ChannelVersion,
                    channel.SupportPhase,
                    true,
                    "SDK"));
            }

            if (!string.IsNullOrWhiteSpace(channel.LatestRuntime))
            {
                results.Add(new AvailableSdk(
                    channel.LatestRuntime,
                    channel.ChannelVersion,
                    channel.SupportPhase,
                    true,
                    "Runtime"));
            }
        }

        return results;
    }

    private static List<AvailableSdk> SortResults(List<AvailableSdk> results)
    {
        results.Sort(static (left, right) =>
        {
            // Latest versions first, then SDKs before runtimes, then by version descending
            if (left.IsLatest != right.IsLatest)
                return left.IsLatest ? -1 : 1;
            int typeCompare = string.Compare(right.Component, left.Component, StringComparison.OrdinalIgnoreCase);
            if (typeCompare != 0) return typeCompare;
            return CompareSdkVersions(right.Version, left.Version);
        });
        return results;
    }

    private static bool IsSupportedChannel(string supportPhase)
    {
        return !string.Equals(supportPhase, "eol", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLtsChannel(string channelVersion)
    {
        ReadOnlySpan<char> majorPart = channelVersion.AsSpan();
        int dotIndex = majorPart.IndexOf('.');
        if (dotIndex >= 0)
        {
            majorPart = majorPart[..dotIndex];
        }

        return int.TryParse(majorPart, out int majorVersion) && majorVersion % 2 == 0;
    }

    /// <summary>
    /// Compares two version strings (e.g. "10.0.301" or "11.0.100-preview.5") numerically by
    /// dot-separated segment, then orders a release without a pre-release suffix ahead of one with.
    /// </summary>
    /// <remarks>
    /// Span-based and allocation-free — this runs as a sort comparator over potentially hundreds of
    /// versions, so it avoids the per-call <c>string[]</c>/<c>int[]</c> allocations of a Split-based parse.
    /// </remarks>
    internal static int CompareSdkVersions(string left, string right)
    {
        ReadOnlySpan<char> leftCore = left;
        ReadOnlySpan<char> rightCore = right;
        ReadOnlySpan<char> leftSuffix = default;
        ReadOnlySpan<char> rightSuffix = default;

        int leftDash = leftCore.IndexOf('-');
        if (leftDash >= 0)
        {
            leftSuffix = leftCore[(leftDash + 1)..].Trim();
            leftCore = leftCore[..leftDash];
        }

        int rightDash = rightCore.IndexOf('-');
        if (rightDash >= 0)
        {
            rightSuffix = rightCore[(rightDash + 1)..].Trim();
            rightCore = rightCore[..rightDash];
        }

        while (!leftCore.IsEmpty || !rightCore.IsEmpty)
        {
            int comparison = NextSegment(ref leftCore).CompareTo(NextSegment(ref rightCore));
            if (comparison != 0)
            {
                return comparison;
            }
        }

        bool leftHasSuffix = !leftSuffix.IsEmpty;
        bool rightHasSuffix = !rightSuffix.IsEmpty;

        if (leftHasSuffix != rightHasSuffix)
        {
            return leftHasSuffix ? -1 : 1;
        }

        return leftSuffix.CompareTo(rightSuffix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reads the next dot-separated numeric segment from a version core, advancing the span.</summary>
    private static int NextSegment(ref ReadOnlySpan<char> core)
    {
        if (core.IsEmpty)
        {
            return 0;
        }

        int dot = core.IndexOf('.');
        ReadOnlySpan<char> segment = dot >= 0 ? core[..dot] : core;
        core = dot >= 0 ? core[(dot + 1)..] : default;
        return int.TryParse(segment.Trim(), out int value) ? value : 0;
    }
}

internal sealed class ReleaseIndex
{
    public List<ChannelInfo> ReleasesIndex { get; init; } = [];
}

internal sealed class ChannelInfo
{
    public string ChannelVersion { get; init; } = string.Empty;
    public string LatestRelease { get; init; } = string.Empty;
    public string LatestSdk { get; init; } = string.Empty;
    public string LatestRuntime { get; init; } = string.Empty;
    public string SupportPhase { get; init; } = string.Empty;
    public string? EolDate { get; init; }

    [JsonPropertyName("releases.json")]
    public string ReleasesJsonUrl { get; init; } = string.Empty;
}

internal sealed class ChannelReleases
{
    public List<ReleaseEntry> Releases { get; init; } = [];
}

internal sealed class ReleaseEntry
{
    public string ReleaseVersion { get; init; } = string.Empty;
    public SdkEntry? Sdk { get; init; }
    public List<SdkEntry> Sdks { get; init; } = [];
}

internal sealed class SdkEntry
{
    public string Version { get; init; } = string.Empty;
}

[JsonSerializable(typeof(ReleaseIndex))]
[JsonSerializable(typeof(ChannelInfo))]
[JsonSerializable(typeof(ChannelReleases))]
[JsonSerializable(typeof(ReleaseEntry))]
[JsonSerializable(typeof(SdkEntry))]
[JsonSerializable(typeof(List<ChannelInfo>))]
[JsonSerializable(typeof(List<ReleaseEntry>))]
[JsonSerializable(typeof(List<SdkEntry>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.KebabCaseLower)]
internal partial class SdkSearchJsonContext : JsonSerializerContext
{
}
