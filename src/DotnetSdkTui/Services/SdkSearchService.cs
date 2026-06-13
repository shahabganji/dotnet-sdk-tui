using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace DotnetSdkTui.Services;

public sealed record AvailableSdk(string Version, string ChannelVersion, string SupportPhase, bool IsLatest);

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

    public static async Task<List<AvailableSdk>> SearchAvailableSdksAsync(string query, CancellationToken ct = default)
    {
        List<ChannelInfo> channels = await GetChannelsAsync(ct);
        string normalizedQuery = query.Trim();

        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return SortResults(CreateLatestSdkResults(channels));
        }

        string search = normalizedQuery.ToLowerInvariant();
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

        if (search is "latest" or "lts")
        {
            IEnumerable<ChannelInfo> filteredChannels = channels.Where(static channel => IsSupportedChannel(channel.SupportPhase));
            if (search == "lts")
            {
                filteredChannels = filteredChannels.Where(static channel => IsLtsChannel(channel.ChannelVersion));
            }

            return SortResults(CreateLatestSdkResults(filteredChannels));
        }

        List<AvailableSdk> prefixMatches = CreateLatestSdkResults(channels)
            .Where(sdk => sdk.Version.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase))
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
