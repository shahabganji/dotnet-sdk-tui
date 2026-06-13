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

        // Prefix matching on version or channel for both SDKs and runtimes
        List<AvailableSdk> prefixMatches = CreateAllResults(channels)
            .Where(sdk => sdk.Version.StartsWith(prefixQuery, StringComparison.OrdinalIgnoreCase)
                || sdk.ChannelVersion.StartsWith(prefixQuery, StringComparison.OrdinalIgnoreCase)
                || sdk.Version.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return SortResults(prefixMatches);
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
            // SDKs first, then runtimes
            int typeCompare = string.Compare(left.Component, right.Component, StringComparison.OrdinalIgnoreCase);
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

    private static int CompareSdkVersions(string left, string right)
    {
        ParseVersion(left, out int[] leftSegments, out string leftSuffix);
        ParseVersion(right, out int[] rightSegments, out string rightSuffix);

        int segmentCount = Math.Max(leftSegments.Length, rightSegments.Length);
        for (int index = 0; index < segmentCount; index++)
        {
            int leftSegment = index < leftSegments.Length ? leftSegments[index] : 0;
            int rightSegment = index < rightSegments.Length ? rightSegments[index] : 0;
            int comparison = leftSegment.CompareTo(rightSegment);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        bool leftHasSuffix = !string.IsNullOrEmpty(leftSuffix);
        bool rightHasSuffix = !string.IsNullOrEmpty(rightSuffix);

        if (leftHasSuffix != rightHasSuffix)
        {
            return leftHasSuffix ? -1 : 1;
        }

        return string.Compare(leftSuffix, rightSuffix, StringComparison.OrdinalIgnoreCase);
    }

    private static void ParseVersion(string version, out int[] segments, out string suffix)
    {
        string[] dashParts = version.Split('-', 2, StringSplitOptions.TrimEntries);
        suffix = dashParts.Length > 1 ? dashParts[1] : string.Empty;
        segments = dashParts[0]
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static part => int.TryParse(part, out int value) ? value : 0)
            .ToArray();
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
