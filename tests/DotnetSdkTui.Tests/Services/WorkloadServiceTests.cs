using DotnetSdkTui.Services;

namespace DotnetSdkTui.Tests.Services;

/// <summary>
/// Guards <see cref="WorkloadTableParser"/> and <see cref="WorkloadService.DeriveFeatureBand"/>
/// against regressions. The fixtures are real captured output from a .NET 10.0.301 SDK; if a
/// future SDK changes the format, these tests fail fast rather than the Workloads panel
/// silently going blank.
/// </summary>
public class WorkloadServiceTests
{
    // Real captured output from `dotnet workload list` on macOS with SDK 10.0.301,
    // no workloads installed. Includes preamble, header, dashed separator, and the
    // trailing "Use `dotnet workload search`..." hint line.
    private const string EmptyList = """

Workload version: 10.0.300-manifests.b0c14421

Installed Workload Id      Manifest Version      Installation Source
--------------------------------------------------------------------

Use `dotnet workload search` to find additional workloads to install.
""";

    // Synthesized `dotnet workload list` output with two installed workloads.
    // Column widths mirror the real CLI: id (25) + version (22) + source.
    private const string PopulatedList = """

Workload version: 10.0.300-manifests.b0c14421

Installed Workload Id      Manifest Version      Installation Source
--------------------------------------------------------------------
maui                       10.0.0/10.0.100       SDK 10.0.300
wasm-tools                 10.0.0/10.0.100       SDK 10.0.300

Use `dotnet workload search` to find additional workloads to install.
""";

    // Real captured output from `dotnet workload search` on SDK 10.0.301.
    private const string SearchOutput = """

Workload ID                     Description
----------------------------------------------------------------------------------------
android                         .NET SDK Workload for building Android applications.
maui                            .NET MAUI SDK for all platforms
maui-android                    .NET MAUI SDK for Android
wasm-tools                      .NET WebAssembly build tools
wasm-tools-net8                 .NET WebAssembly build tools for net8.0
""";

    [Fact]
    public void ParseTable_EmptyInstalledList_YieldsNoRows()
    {
        List<string[]> rows = WorkloadTableParser.ParseTable(EmptyList, "Installed Workload Id").ToList();
        Assert.Empty(rows);
    }

    [Fact]
    public void ParseTable_PopulatedInstalledList_ParsesEveryRow()
    {
        List<string[]> rows = WorkloadTableParser.ParseTable(PopulatedList, "Installed Workload Id").ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "maui",       "10.0.0/10.0.100", "SDK 10.0.300" }, rows[0]);
        Assert.Equal(new[] { "wasm-tools", "10.0.0/10.0.100", "SDK 10.0.300" }, rows[1]);
    }

    [Fact]
    public void ParseTable_Search_ParsesIdAndDescription()
    {
        List<string[]> rows = WorkloadTableParser.ParseTable(SearchOutput, "Workload ID").ToList();

        Assert.Equal(5, rows.Count);
        Assert.Equal("android",         rows[0][0]);
        Assert.Equal(".NET SDK Workload for building Android applications.", rows[0][1]);
        Assert.Equal("maui",             rows[1][0]);
        Assert.Equal(".NET MAUI SDK for all platforms", rows[1][1]);
        Assert.Equal("wasm-tools-net8",  rows[4][0]);
        Assert.Equal(".NET WebAssembly build tools for net8.0", rows[4][1]);
    }

    [Fact]
    public void ParseTable_MissingHeaderKeyword_YieldsNoRows()
    {
        List<string[]> rows = WorkloadTableParser.ParseTable(SearchOutput, "This Header Does Not Exist").ToList();
        Assert.Empty(rows);
    }

    [Fact]
    public void ParseTable_SkipsTrailingHintLine()
    {
        // The "Use `dotnet workload search` ..." line lives below the data rows in real output.
        // It must not be parsed as a data row.
        List<string[]> rows = WorkloadTableParser.ParseTable(PopulatedList, "Installed Workload Id").ToList();
        Assert.DoesNotContain(rows, r => r[0].StartsWith("Use "));
    }

    [Fact]
    public void IsSeparatorLine_RecognizesDashRuns()
    {
        Assert.True(WorkloadTableParser.IsSeparatorLine("---------------"));
        Assert.True(WorkloadTableParser.IsSeparatorLine("   --------------   "));
        Assert.False(WorkloadTableParser.IsSeparatorLine(""));
        Assert.False(WorkloadTableParser.IsSeparatorLine("maui                       10.0.0"));
    }

    [Theory]
    [InlineData("10.0.301", "10.0.300")]
    [InlineData("10.0.109", "10.0.100")]
    [InlineData("8.0.422",  "8.0.400")]
    [InlineData("9.0.305",  "9.0.300")]
    [InlineData("7.0.410",  "7.0.400")]
    public void DeriveFeatureBand_MapsPatchToHundreds(string sdk, string expectedBand)
    {
        Assert.Equal(expectedBand, WorkloadService.DeriveFeatureBand(sdk));
    }

    [Theory]
    [InlineData("11.0.100-preview.5.26302.115", "11.0.100-preview.5")]
    [InlineData("11.0.100-rc.1.26123.4",        "11.0.100-rc.1")]
    public void DeriveFeatureBand_PreservesPreviewSuffix(string sdk, string expectedBand)
    {
        Assert.Equal(expectedBand, WorkloadService.DeriveFeatureBand(sdk));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void DeriveFeatureBand_BlankInput_ReturnsUnknown(string sdk)
    {
        Assert.Equal("unknown", WorkloadService.DeriveFeatureBand(sdk));
    }

    [Fact]
    public void DeriveFeatureBand_MalformedInput_ReturnsInputUnchanged()
    {
        Assert.Equal("not-a-version", WorkloadService.DeriveFeatureBand("not-a-version"));
        Assert.Equal("10.0.xyz",      WorkloadService.DeriveFeatureBand("10.0.xyz"));
    }

    [Fact]
    public void FindColumnStarts_ThreeColumns_ReturnsThreeStarts()
    {
        //                       0         1         2         3
        //                       0123456789012345678901234567890123456789
        int[] starts = WorkloadTableParser.FindColumnStarts(
                              "Installed Workload Id      Manifest Version      Installation Source");
        Assert.Equal(3, starts.Length);
        Assert.Equal(0, starts[0]);
    }

    [Fact]
    public void SplitByColumns_TruncatesToColumnBoundaries()
    {
        int[] cols = new[] { 0, 25, 47 };
        string line = "maui                       10.0.0/10.0.100       SDK 10.0.300";
        string[] cells = WorkloadTableParser.SplitByColumns(line, cols);
        Assert.Equal("maui",             cells[0]);
        Assert.Equal("10.0.0/10.0.100",  cells[1]);
        Assert.Equal("SDK 10.0.300",     cells[2]);
    }

    [Theory]
    [InlineData("workload-set")]
    [InlineData("  workload-set  ")]
    [InlineData("workload-set\n")]
    public void LastNonEmptyLine_SingleValue_ReturnsTrimmed(string input)
    {
        Assert.Equal("workload-set", WorkloadService.LastNonEmptyLine(input));
    }

    [Fact]
    public void LastNonEmptyLine_FirstRunBanner_ReturnsOnlyLastLine()
    {
        // Real first-run experience banner captured on a freshly installed SDK, followed
        // by the actual command output (`manifests` in this case). LastNonEmptyLine must
        // skip the banner and return only the trailing value.
        string banner = """
Welcome to .NET 9.0!
---------------------
SDK Version: 9.0.203

----------------
Installed an ASP.NET Core HTTPS development certificate.
To trust the certificate, run 'dotnet dev-certs https --trust'
Learn about HTTPS: https://aka.ms/dotnet-https
----------------
Write your first app: https://aka.ms/dotnet-hello-world
Find out what's new: https://aka.ms/dotnet-whats-new
Explore documentation: https://aka.ms/dotnet-docs
Report issues and find source on GitHub: https://github.com/dotnet/core
Use 'dotnet --help' to see available commands or visit: https://aka.ms/dotnet-cli
--------------------------------------------------------------------------------------
manifests
""";
        Assert.Equal("manifests", WorkloadService.LastNonEmptyLine(banner));
    }

    [Fact]
    public void LastNonEmptyLine_EmptyOrWhitespace_ReturnsNull()
    {
        Assert.Null(WorkloadService.LastNonEmptyLine(""));
        Assert.Null(WorkloadService.LastNonEmptyLine("   \n\n  "));
    }

    [Fact]
    public void EnsureSdkPinDirectory_WritesGlobalJson()
    {
        string dir = WorkloadService.EnsureSdkPinDirectory("8.0.422");
        string file = Path.Combine(dir, "global.json");

        Assert.True(File.Exists(file));
        string content = File.ReadAllText(file);
        Assert.Contains("\"version\":\"8.0.422\"", content);
        Assert.Contains("\"rollForward\":\"disable\"", content);
    }

    [Fact]
    public void EnsureSdkPinDirectory_IsIdempotent()
    {
        string dir1 = WorkloadService.EnsureSdkPinDirectory("10.0.301");
        DateTime firstWrite = File.GetLastWriteTimeUtc(Path.Combine(dir1, "global.json"));

        System.Threading.Thread.Sleep(50);
        string dir2 = WorkloadService.EnsureSdkPinDirectory("10.0.301");
        DateTime secondWrite = File.GetLastWriteTimeUtc(Path.Combine(dir2, "global.json"));

        Assert.Equal(dir1, dir2);
        Assert.Equal(firstWrite, secondWrite);
    }

    [Fact]
    public void EnsureSdkPinDirectory_DifferentSdksGetDifferentDirs()
    {
        string a = WorkloadService.EnsureSdkPinDirectory("8.0.422");
        string b = WorkloadService.EnsureSdkPinDirectory("10.0.301");
        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void EnsureSdkPinDirectory_BlankInput_Throws(string? sdk)
    {
        Assert.Throws<ArgumentException>(() => WorkloadService.EnsureSdkPinDirectory(sdk!));
    }
}
