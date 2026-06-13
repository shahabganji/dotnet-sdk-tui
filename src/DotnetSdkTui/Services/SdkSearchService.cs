using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace DotnetSdkTui.Services;

/// <summary>
/// Represents an available SDK version discovered from the .NET release metadata.
/// </summary>
/// <param name="Version">The SDK version string (e.g. "10.0.300").</param>
/// <param name="ChannelVersion">The channel this SDK belongs to (e.g. "10.0").</param>
/// <param name="SupportPhase">The lifecycle support phase (active, maintenance, preview, eol).</param>
/// <param name="IsLatest">Whether this is the latest SDK in its channel.</param>
public sealed record AvailableSdk(string Version, string ChannelVersion, string SupportPhase, bool IsLatest);

/// <summary>
/// Fetches .NET release metadata from Microsoft's CDN and searches available SDK versions.
/// </summary>
internal static class SdkSearchService
{
    private const string ReleasesIndexUrl = "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json";
    private static readonly HttpClient HttpClient = new();

    /// <summary>
    /// Retrieves the list of all .NET release channels from the releases index.
    /// </summary>
    internal static async Task<List<ChannelInfo>> GetChannelsAsync(CancellationToken ct = default)
    {
        string json = await HttpClient.GetStringAsync(ReleasesIndexUrl, ct);
        ReleaseIndex? releaseIndex = JsonSerializer.Deserialize(json, SdkSearchJsonContext.Default.ReleaseIndex);
        return releaseIndex?.ReleasesIndex ?? [];
    }

    /// <summary>
    /// Searches for available SDK versions matching the given query.
    /// Supports exact channel match (e.g. "10.0"), keywords ("latest", "lts", "preview"),
    /// and version prefix matching (e.g. "9").
    /// </summary>
    public static async Task<List<AvailableSdk>> SearchAvailableSdksAsync(string query, CancellationToken ct = default)
    {
        List<ChannelInfo> channels = await GetChannelsAsync(ct);
        string normalizedQuery = query.Trim();

        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return SortResults(CreateLatestSdkResults(channels));
        }

        string search = normalizedQuery.ToLowerInvariant();

        // Keyword matching — also match partial keywords for live search
        if ("latest".StartsWith(search, StringComparison.OrdinalIgnoreCase) && search.Length >= 2)
        {
            var supportedChannels = channels.Where(static channel => IsSupportedChannel(channel.SupportPhase));
            return SortResults(CreateLatestSdkResults(supportedChannels));
        }

        if ("lts".StartsWith(search, StringComparison.OrdinalIgnoreCase) && search.Length >= 2)
        {
            var ltsChannels = channels
                .Where(static channel => IsSupportedChannel(channel.SupportPhase))
                .Where(static channel => IsLtsChannel(channel.ChannelVersion));
            return SortResults(CreateLatestSdkResults(ltsChannels));
        }

        if ("preview".StartsWith(search, StringComparison.OrdinalIgnoreCase) && search.Length >= 2)
        {
            var previewChannels = channels
                .Where(channel => string.Equals(channel.SupportPhase, "preview", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(channel.SupportPhase, "go-live", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(channel.SupportPhase, "rc", StringComparison.OrdinalIgnoreCase));
            return SortResults(CreateLatestSdkResults(previewChannels));
        }

        // Exact channel match (e.g. "10.0")
        List<ChannelInfo> channelMatches = channels
            .Where(channel => string.Equals(channel.ChannelVersion, normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (channelMatches.Count > 0)
        {
            List<AvailableSdk> results = [];
            foreach (ChannelInfo channel in channelMatches)
            {
                results.AddRange(await GetChannelSdkResultsAsync(channel, ct));
            }

            return SortResults(results);
        }

        // Prefix matching on version or channel (e.g. "9", "10")
        List<AvailableSdk> prefixMatches = CreateLatestSdkResults(channels)
            .Where(sdk => sdk.Version.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || sdk.ChannelVersion.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return SortResults(prefixMatches);
    }

    private static async Task<List<AvailableSdk>> GetChannelSdkResultsAsync(ChannelInfo channel, CancellationToken ct)
    {
        ChannelReleases? channelReleases = await GetJsonAsync(channel.ReleasesJsonUrl, SdkSearchJsonContext.Default.ChannelReleases, ct);
        if (channelReleases?.Releases is null || channelReleases.Releases.Count == 0)
        {
            return [];
        }

        HashSet<string> seenVersions = new(StringComparer.OrdinalIgnoreCase);
        List<AvailableSdk> results = [];

        foreach (ReleaseEntry release in channelReleases.Releases)
        {
            AddSdkResult(results, seenVersions, release.Sdk, channel);

            foreach (SdkEntry sdk in release.Sdks)
            {
                AddSdkResult(results, seenVersions, sdk, channel);
            }
        }

        return results;
    }

    private static void AddSdkResult(List<AvailableSdk> results, HashSet<string> seenVersions, SdkEntry? sdk, ChannelInfo channel)
    {
        if (sdk is null || string.IsNullOrWhiteSpace(sdk.Version) || !seenVersions.Add(sdk.Version))
        {
            return;
        }

        results.Add(new AvailableSdk(
            sdk.Version,
            channel.ChannelVersion,
            channel.SupportPhase,
            string.Equals(sdk.Version, channel.LatestSdk, StringComparison.OrdinalIgnoreCase)));
    }

    private static List<AvailableSdk> CreateLatestSdkResults(IEnumerable<ChannelInfo> channels)
    {
        return channels
            .Where(channel => !string.IsNullOrWhiteSpace(channel.LatestSdk))
            .Select(channel => new AvailableSdk(
                channel.LatestSdk,
                channel.ChannelVersion,
                channel.SupportPhase,
                true))
            .ToList();
    }

    private static async Task<T?> GetJsonAsync<T>(string url, JsonTypeInfo<T> jsonTypeInfo, CancellationToken ct)
    {
        string json = await HttpClient.GetStringAsync(url, ct);
        return JsonSerializer.Deserialize(json, jsonTypeInfo);
    }

    private static List<AvailableSdk> SortResults(List<AvailableSdk> results)
    {
        results.Sort(static (left, right) => CompareSdkVersions(right.Version, left.Version));
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
