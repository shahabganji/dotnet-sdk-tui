using System.Linq;
using BenchmarkDotNet.Attributes;
using DotnetSdkTui.Models;
using DotnetSdkTui.Services;

namespace DotnetSdkTui.Benchmarks;

/// <summary>Sorting a realistic list of SDK versions — exercises the comparator O(n log n) times.</summary>
[MemoryDiagnoser]
[ShortRunJob]
public class VersionSortBenchmarks
{
    private string[] _versions = [];

    [GlobalSetup]
    public void Setup()
    {
        var list = new List<string>();
        foreach (int major in new[] { 6, 7, 8, 9, 10, 11 })
            foreach (int band in new[] { 1, 2, 3, 4 })
                foreach (int patch in new[] { 0, 3, 12, 28, 100, 203, 305, 408 })
                    list.Add($"{major}.0.{band}{patch:00}");
        // a few pre-releases to exercise the suffix path
        list.Add("11.0.100-preview.5.26302.115");
        list.Add("11.0.100-rc.1");
        list.Add("12.0.100-preview.1");
        _versions = [.. list];

        // Equivalence: both comparators must produce the same ordering.
        var a = (string[])_versions.Clone();
        var b = (string[])_versions.Clone();
        System.Array.Sort(a, Baselines.CompareSdkVersions);
        System.Array.Sort(b, SdkSearchService.CompareSdkVersions);
        if (!a.SequenceEqual(b))
            throw new System.InvalidOperationException("Version comparator mismatch between baseline and optimized.");
    }

    [Benchmark(Baseline = true)]
    public string[] Sort_Baseline()
    {
        var copy = (string[])_versions.Clone();
        System.Array.Sort(copy, Baselines.CompareSdkVersions);
        return copy;
    }

    [Benchmark]
    public string[] Sort_Optimized()
    {
        var copy = (string[])_versions.Clone();
        System.Array.Sort(copy, SdkSearchService.CompareSdkVersions);
        return copy;
    }
}

/// <summary>Parsing the text output of <c>dotnet --list-sdks</c>.</summary>
[MemoryDiagnoser]
[ShortRunJob]
public class SdkListParseBenchmarks
{
    private string _output = "";

    [GlobalSetup]
    public void Setup()
    {
        var lines = new List<string>();
        foreach (int major in new[] { 6, 7, 8, 9, 10 })
            foreach (int band in new[] { 1, 2, 3 })
                lines.Add($"{major}.0.{band}05 [/usr/local/share/dotnet/sdk]");
        lines.Add("11.0.100-preview.5.26302.115 [/Users/x/Library/Application Support/dotnet/sdk]");
        _output = string.Join('\n', lines) + "\n";

        var baseline = Baselines.ParseDotnetSdkList(_output);
        var optimized = DotnetUpService.ParseDotnetSdkList(_output);
        if (baseline.Count != optimized.Count
            || !baseline.Select(s => s.Version + "|" + s.InstallRoot)
                        .SequenceEqual(optimized.Select(s => s.Version + "|" + s.InstallRoot)))
            throw new System.InvalidOperationException("SDK list parser mismatch between baseline and optimized.");
    }

    [Benchmark(Baseline = true)]
    public List<SdkInfo> Parse_Baseline() => Baselines.ParseDotnetSdkList(_output);

    [Benchmark]
    public List<SdkInfo> Parse_Optimized() => DotnetUpService.ParseDotnetSdkList(_output);
}

/// <summary>Classifying each installed SDK as managed/unmanaged against dotnetup's tracked roots.</summary>
[MemoryDiagnoser]
[ShortRunJob]
public class ManagedRootBenchmarks
{
    private string[] _installRoots = [];
    private List<string> _managedRoots = [];

    [GlobalSetup]
    public void Setup()
    {
        _managedRoots =
        [
            "/Users/x/Library/Application Support/dotnet",
            "/Users/x/.dotnet",
        ];
        _installRoots =
        [
            "/Users/x/Library/Application Support/dotnet/sdk",
            "/usr/local/share/dotnet/sdk",
            "/Users/x/.dotnet/sdk",
            "/usr/local/share/dotnet/shared/Microsoft.NETCore.App",
            "/opt/dotnet/sdk",
            "/Users/x/Library/Application Support/dotnet-preview/sdk",
        ];

        foreach (string root in _installRoots)
        {
            if (Baselines.IsManagedInstallRoot(root, _managedRoots)
                != DotnetUpService.IsManagedInstallRoot(root, _managedRoots))
                throw new System.InvalidOperationException("Managed-root classifier mismatch between baseline and optimized.");
        }
    }

    [Benchmark(Baseline = true)]
    public int Classify_Baseline()
    {
        int managed = 0;
        foreach (string root in _installRoots)
            if (Baselines.IsManagedInstallRoot(root, _managedRoots))
                managed++;
        return managed;
    }

    [Benchmark]
    public int Classify_Optimized()
    {
        int managed = 0;
        foreach (string root in _installRoots)
            if (DotnetUpService.IsManagedInstallRoot(root, _managedRoots))
                managed++;
        return managed;
    }
}
