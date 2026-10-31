using System.Text.Json;
using DotnetSdkTui.Services;
using static DotnetSdkTui.Services.InstallGuard;

namespace DotnetSdkTui.Tests.Services;

public class InstallGuardTests
{
    private const string Root = "/Users/jane/Library/Application Support/dotnet";

    // --- Spec matching (mirrors what dotnetup's garbage collection keeps) ---

    [Theory]
    [InlineData("10.0.403", "10.0.403", true)]
    [InlineData("10.0.403", "10.0.401", false)]
    [InlineData("10", "10.0.403", true)]
    [InlineData("10", "9.0.314", false)]
    [InlineData("10.0", "10.0.401", true)]
    [InlineData("10.0", "10.1.100", false)]
    [InlineData("11.0", "11.0.100-rc.1.25451.107", true)]
    [InlineData("10.0.4xx", "10.0.403", true)]
    [InlineData("10.0.4xx", "10.0.304", false)]
    [InlineData("11.0.1xx", "11.0.100-rc.1.25451.107", true)]
    [InlineData("latest", "9.0.0", true)]
    [InlineData("latest", "10.0.0-rc.1.25451.107", false)]
    [InlineData("lts", "8.0.0", true)]
    [InlineData("lts", "9.0.0", false)]
    [InlineData("lts", "10.0.0-rc.1.25451.107", false)]
    [InlineData("preview", "10.0.0-rc.1.25451.107", true)]
    [InlineData("sts", "9.0.0", false)]
    public void SpecMatches_FollowsDotnetUpSemantics(string spec, string version, bool expected)
    {
        Assert.Equal(expected, SpecMatches(spec, version));
    }

    [Fact]
    public void ResolveKept_ChannelKeepsOnlyNewestMatch()
    {
        var installs = new[] { Sdk("10.0.400"), Sdk("10.0.403"), Sdk("10.0.401"), Sdk("9.0.314") };

        Installation? kept = ResolveKept(new InstallSpec("SDK", "10.0", "Explicit", Root), installs);

        Assert.Equal(Sdk("10.0.403"), kept);
    }

    // --- Command parsing ---

    [Fact]
    public void ParseCommand_ReadsTargetsAndQuotedInstallPath()
    {
        ParsedCommand cmd = ParseCommand($"runtime uninstall aspnetcore@9.0.0 --source all --install-path \"{Root}\"");

        Assert.Equal(CommandKind.Uninstall, cmd.Kind);
        Assert.Equal("runtime", cmd.Family);
        Assert.Equal(["aspnetcore@9.0.0"], cmd.Targets);
        Assert.Equal(Root, cmd.InstallPath);
    }

    [Theory]
    [InlineData("list --format Json")]
    [InlineData("update")]
    [InlineData("sdk update")]
    public void ParseCommand_OtherCommands_AreNotGuarded(string args)
    {
        Assert.Equal(CommandKind.None, ParseCommand(args).Kind);
    }

    [Theory]
    [InlineData("ASP.NET Core", "9.0.0", "runtime uninstall aspnetcore@9.0.0")]
    [InlineData("Runtime", "9.0.0", "runtime uninstall 9.0.0")]
    [InlineData("Windows Desktop", "9.0.0", "runtime uninstall windowsdesktop@9.0.0")]
    [InlineData("SDK", "10.0.403", "sdk uninstall 10.0.403")]
    public void UninstallArgsFor_TargetsTheExactComponentAndVersion(string component, string version, string expected)
    {
        Assert.Equal(expected, UninstallArgsFor(component, version));
    }

    // --- End-to-end scenarios against a simulated dotnetup ---

    // The reported bug: 10.0.400/10.0.401 tracked only by the "10.0" channel, install 10.0.403,
    // then uninstall the .NET 11 RC — the older 10.0 SDKs must survive.
    [Fact]
    public async Task ReportedScenario_InstallThenUninstallRc_KeepsOlderSdks()
    {
        var dotnetup = new FakeDotnetUp(channels: new() { ["10.0"] = "10.0.403" });
        dotnetup.Install("SDK", "10.0.400", spec: "10.0");
        dotnetup.Install("SDK", "10.0.401", spec: "10.0");
        dotnetup.Install("SDK", "11.0.100-rc.1.25451.107", spec: "11.0");

        Assert.Equal(0, await dotnetup.RunGuardedAsync("sdk install 10.0"));
        Assert.Equal(0, await dotnetup.RunGuardedAsync("sdk uninstall 11.0.100-rc.1.25451.107"));

        Assert.Equal(["10.0.400", "10.0.401", "10.0.403"], dotnetup.InstalledVersions("SDK"));
    }

    // Control: the same steps without the guard reproduce the data loss, proving the scenario is real.
    [Fact]
    public async Task ReportedScenario_WithoutGuard_LosesOlderSdks()
    {
        var dotnetup = new FakeDotnetUp(channels: new() { ["10.0"] = "10.0.403" });
        dotnetup.Install("SDK", "10.0.400", spec: "10.0");
        dotnetup.Install("SDK", "10.0.401", spec: "10.0");
        dotnetup.Install("SDK", "11.0.100-rc.1.25451.107", spec: "11.0");

        await dotnetup.RunAsync("sdk install 10.0");
        await dotnetup.RunAsync("sdk uninstall 11.0 --source all");

        Assert.Equal(["10.0.403"], dotnetup.InstalledVersions("SDK"));
    }

    [Fact]
    public async Task Uninstall_OlderPatchUnderChannel_RemovesOnlyThatPatch()
    {
        var dotnetup = new FakeDotnetUp();
        dotnetup.Install("SDK", "10.0.400", spec: "10.0");
        dotnetup.Install("SDK", "10.0.403", spec: "10.0");
        dotnetup.Install("SDK", "9.0.314", spec: "9.0.3xx");

        Assert.Equal(0, await dotnetup.RunGuardedAsync("sdk uninstall 10.0.400"));

        Assert.Equal(["9.0.314", "10.0.403"], dotnetup.InstalledVersions("SDK"));
    }

    [Fact]
    public async Task Uninstall_VersionKeptByChannelAndLatest_RemovesOnlyThatVersion()
    {
        var dotnetup = new FakeDotnetUp();
        dotnetup.Install("SDK", "10.0.400", spec: "10.0.400");
        dotnetup.Install("SDK", "10.0.403", spec: "10.0");
        dotnetup.AddSpec("SDK", "latest");
        dotnetup.Install("SDK", "9.0.203", spec: "9.0");
        dotnetup.Install("SDK", "9.0.314", spec: "9.0");

        Assert.Equal(0, await dotnetup.RunGuardedAsync("sdk uninstall 10.0.403"));

        Assert.Equal(["9.0.203", "9.0.314", "10.0.400"], dotnetup.InstalledVersions("SDK"));
    }

    [Fact]
    public async Task Uninstall_AspNetCore_KeepsCoreRuntimeOfSameVersionAndSdks()
    {
        var dotnetup = new FakeDotnetUp();
        dotnetup.Install("Runtime", "9.0.0", spec: "9.0");
        dotnetup.Install("ASPNETCore", "9.0.0", spec: "9.0");
        dotnetup.Install("SDK", "9.0.314", spec: "9.0");

        Assert.Equal(0, await dotnetup.RunGuardedAsync(UninstallArgsFor("ASP.NET Core", "9.0.0")));

        Assert.Empty(dotnetup.InstalledVersions("ASPNETCore"));
        Assert.Equal(["9.0.0"], dotnetup.InstalledVersions("Runtime"));
        Assert.Equal(["9.0.314"], dotnetup.InstalledVersions("SDK"));
    }

    // A runtime installed as part of an SDK has no installation record of its own; uninstalling it
    // must not run anything (the old resolver could fall back to removing a whole channel).
    [Fact]
    public async Task Uninstall_RuntimeBundledWithSdk_RunsNothing()
    {
        var dotnetup = new FakeDotnetUp();
        dotnetup.Install("SDK", "10.0.403", spec: "10.0");

        Assert.Equal(1, await dotnetup.RunGuardedAsync("runtime uninstall 10.0.4"));

        Assert.Empty(dotnetup.Executed);
        Assert.Contains(dotnetup.Log, l => l.Contains("uninstall that SDK", StringComparison.Ordinal));
        Assert.Equal(["10.0.403"], dotnetup.InstalledVersions("SDK"));
    }

    // Installing a newer runtime re-points "latest" — the version it used to keep must stay.
    [Fact]
    public async Task InstallRuntime_RepointingLatest_KeepsPreviousRuntimeThroughLaterUninstall()
    {
        var dotnetup = new FakeDotnetUp();
        dotnetup.Install("Runtime", "9.0.0", spec: "latest");
        dotnetup.Install("Runtime", "8.0.0", spec: "8.0.0");

        Assert.Equal(0, await dotnetup.RunGuardedAsync("runtime install 10.0.0"));
        Assert.Equal(0, await dotnetup.RunGuardedAsync("runtime uninstall 8.0.0"));

        Assert.Equal(["9.0.0", "10.0.0"], dotnetup.InstalledVersions("Runtime"));
    }

    [Fact]
    public async Task Install_PinFails_MainCommandIsNotRun()
    {
        var dotnetup = new FakeDotnetUp { FailInstallsOf = "10.0.401" };
        dotnetup.Install("SDK", "10.0.401", spec: "10.0");

        int exit = await dotnetup.RunGuardedAsync("sdk install 9.0.314");

        Assert.NotEqual(0, exit);
        Assert.DoesNotContain(dotnetup.Executed, a => a.StartsWith("sdk install 9.0.314", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Uninstall_UnreadableList_IsCancelled()
    {
        var dotnetup = new FakeDotnetUp { ListUnavailable = true };
        dotnetup.Install("SDK", "10.0.403", spec: "10.0");

        Assert.Equal(1, await dotnetup.RunGuardedAsync("sdk uninstall 10.0.403"));

        Assert.Empty(dotnetup.Executed);
        Assert.Equal(["10.0.403"], dotnetup.InstalledVersions("SDK"));
    }

    private static Installation Sdk(string version) => new("SDK", version, Root);

    /// <summary>
    /// In-memory dotnetup: install adds an installation + spec without garbage collection;
    /// uninstall removes the spec then keeps only each remaining spec's newest match — the
    /// behaviour observed from dotnetup 0.2.0.
    /// </summary>
    private sealed class FakeDotnetUp(Dictionary<string, string>? channels = null)
    {
        private readonly List<InstallSpec> _specs = [];
        private readonly List<Installation> _installations = [];
        private readonly Dictionary<string, string> _channels = channels ?? [];

        public List<string> Executed { get; } = [];
        public List<string> Log { get; } = [];
        public string? FailInstallsOf { get; init; }
        public bool ListUnavailable { get; init; }

        public void Install(string component, string version, string spec)
        {
            _installations.Add(new Installation(component, version, Root));
            AddSpec(component, spec);
        }

        public void AddSpec(string component, string spec)
        {
            if (!_specs.Any(s => s.Component == component && s.VersionOrChannel == spec))
                _specs.Add(new InstallSpec(component, spec, "Explicit", Root));
        }

        public List<string> InstalledVersions(string component) => _installations
            .Where(i => i.Component == component)
            .Select(i => i.Version)
            .OrderBy(v => v, Comparer<string>.Create((a, b) => SdkSearchService.CompareSdkVersions(a, b)))
            .ToList();

        public Task<int> RunGuardedAsync(string args) =>
            InstallGuard.RunGuardedAsync(args, RunAsync, () => Task.FromResult(ListUnavailable ? null : ToJson()), Log.Add);

        public Task<int> RunAsync(string args)
        {
            Executed.Add(args);
            ParsedCommand cmd = ParseCommand(args);
            string source = SourceOf(args);

            foreach (string target in cmd.Targets)
            {
                (string component, string value) = SplitTarget(cmd.Family, target);
                if (cmd.Kind == CommandKind.Install)
                {
                    if (FailInstallsOf == value)
                        return Task.FromResult(1);
                    string version = _channels.TryGetValue(value, out string? resolved) ? resolved : value;
                    if (!_installations.Any(i => i.Component == component && i.Version == version))
                        _installations.Add(new Installation(component, version, Root));
                    AddSpec(component, value);
                }
                else
                {
                    int removed = _specs.RemoveAll(s => s.Component == component && s.VersionOrChannel == value
                        && (source == "all" || s.Source.Equals(source, StringComparison.OrdinalIgnoreCase)));
                    if (removed == 0)
                        return Task.FromResult(1);
                    CollectGarbage();
                }
            }

            return Task.FromResult(0);
        }

        private void CollectGarbage()
        {
            var keep = _specs.Select(s => ResolveKept(s, _installations)).Where(i => i is not null).ToHashSet();
            _installations.RemoveAll(i => !keep.Contains(i));
        }

        private static string SourceOf(string args)
        {
            string[] tokens = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int index = Array.IndexOf(tokens, "--source");
            return index >= 0 && index + 1 < tokens.Length ? tokens[index + 1].ToLowerInvariant() : "explicit";
        }

        private string ToJson() => JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["installSpecs"] = _specs.Select(s => new Dictionary<string, string>
            {
                ["component"] = s.Component,
                ["versionOrChannel"] = s.VersionOrChannel,
                ["source"] = s.Source,
                ["installRoot"] = s.InstallRoot,
            }).ToList(),
            ["installations"] = _installations.Select(i => new Dictionary<string, string>
            {
                ["component"] = i.Component,
                ["version"] = i.Version,
                ["installRoot"] = i.InstallRoot,
            }).ToList(),
        });
    }
}
