using System.Linq;
using DotnetSdkTui.Models;

namespace DotnetSdkTui.Benchmarks;

/// <summary>
/// Verbatim copies of the pre-optimization implementations, kept here so each benchmark can compare
/// the original allocation-heavy code against the shipped span-based version in a single run.
/// </summary>
internal static class Baselines
{
    // ── SdkSearchService.CompareSdkVersions (Split + LINQ + int[] allocations) ──
    public static int CompareSdkVersions(string left, string right)
    {
        ParseVersion(left, out int[] leftSegments, out string leftSuffix);
        ParseVersion(right, out int[] rightSegments, out string rightSuffix);

        int segmentCount = System.Math.Max(leftSegments.Length, rightSegments.Length);
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

        return string.Compare(leftSuffix, rightSuffix, System.StringComparison.OrdinalIgnoreCase);
    }

    private static void ParseVersion(string version, out int[] segments, out string suffix)
    {
        string[] dashParts = version.Split('-', 2, System.StringSplitOptions.TrimEntries);
        suffix = dashParts.Length > 1 ? dashParts[1] : string.Empty;
        segments = dashParts[0]
            .Split('.', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
            .Select(static part => int.TryParse(part, out int value) ? value : 0)
            .ToArray();
    }

    // ── DotnetUpService.ParseDotnetSdkList (Split into string[] + intermediate strings) ──
    public static List<SdkInfo> ParseDotnetSdkList(string output)
    {
        var installations = new List<SdkInfo>();

        foreach (string rawLine in output.Split('\n', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries))
        {
            int separatorIndex = rawLine.IndexOf(" [", System.StringComparison.Ordinal);
            int openBracketIndex = rawLine.IndexOf('[', System.StringComparison.Ordinal);
            int closeBracketIndex = rawLine.LastIndexOf(']');

            if (separatorIndex <= 0 || openBracketIndex < 0 || closeBracketIndex <= openBracketIndex)
            {
                continue;
            }

            string version = rawLine[..separatorIndex].Trim();
            string installRoot = rawLine[(openBracketIndex + 1)..closeBracketIndex].Trim();

            if (version.Length == 0 || installRoot.Length == 0)
            {
                continue;
            }

            installations.Add(new SdkInfo("SDK", version, installRoot, string.Empty));
        }

        return installations;
    }

    // ── DotnetUpService.IsManagedInstallRoot ("<root>/" probe string allocated per candidate) ──
    public static bool IsManagedInstallRoot(string installRoot, IEnumerable<string> managedRoots)
    {
        string normalized = NormalizeRoot(installRoot);
        if (normalized.Length == 0) return false;

        foreach (string root in managedRoots)
        {
            string r = NormalizeRoot(root);
            if (r.Length == 0) continue;

            if (normalized.Equals(r, System.StringComparison.OrdinalIgnoreCase))
                return true;

            if (normalized.StartsWith(r + '/', System.StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(r + '\\', System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string NormalizeRoot(string path) =>
        string.IsNullOrWhiteSpace(path) ? "" : path.Trim().TrimEnd('/', '\\');
}
