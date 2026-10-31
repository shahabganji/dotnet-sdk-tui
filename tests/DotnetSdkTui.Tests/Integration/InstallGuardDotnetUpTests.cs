using System.Text.Json;
using System.Text.Json.Nodes;
using DotnetSdkTui.Services;
using Xunit.Abstractions;

namespace DotnetSdkTui.Tests.Integration;

/// <summary>
/// Runs <see cref="InstallGuard"/> against the real dotnetup in an isolated sandbox
/// (<c>--install-path</c> + <c>--manifest-path</c>), so the user's own installs are never touched.
/// Skipped when dotnetup isn't available or <c>DSM_SKIP_DOTNETUP_TESTS=1</c>.
/// </summary>
[Trait("Category", "Integration")]
public class InstallGuardDotnetUpTests(DotnetUpSandboxFixture fixture, ITestOutputHelper output)
    : IClassFixture<DotnetUpSandboxFixture>
{
    private static readonly object Runtime8 = Spec("Runtime", "8.0.0");
    private static readonly object Channel9 = Spec("Runtime", "9.0");

    // Runtime 9.0.0 is only reachable through the "9.0" channel, which now resolves to 9.0.1 —
    // the same shape as the reported bug (10.0.400/401 orphaned by installing 10.0.403).
    [DotnetUpFact]
    public async Task GuardedUninstall_KeepsVersionOrphanedByChannel()
    {
        using Sandbox sandbox = fixture.CreateSandbox(output, [Runtime8, Channel9]);

        Assert.Equal(0, await sandbox.RunGuardedAsync("runtime uninstall 8.0.0"));

        Assert.Equal(["9.0.0", "9.0.1"], await sandbox.TrackedAsync("Runtime"));
        Assert.False(sandbox.RuntimeOnDisk("8.0.0"));
        Assert.True(sandbox.RuntimeOnDisk("9.0.0"));
        Assert.True(sandbox.RuntimeOnDisk("9.0.1"));
    }

    // Control: without the guard dotnetup's cleanup deletes the orphaned 9.0.0.
    [DotnetUpFact]
    public async Task UnguardedUninstall_DeletesVersionOrphanedByChannel()
    {
        using Sandbox sandbox = fixture.CreateSandbox(output, [Runtime8, Channel9]);

        Assert.Equal(0, await sandbox.RunAsync($"runtime uninstall 8.0.0 --source all {sandbox.InstallPathArg}"));

        Assert.False(sandbox.RuntimeOnDisk("9.0.0"));
    }

    [DotnetUpFact]
    public async Task GuardedUninstall_OfChannelLatest_RemovesOnlyThatVersion()
    {
        using Sandbox sandbox = fixture.CreateSandbox(output, [Runtime8, Channel9]);

        Assert.Equal(0, await sandbox.RunGuardedAsync("runtime uninstall 9.0.1"));

        Assert.Equal(["8.0.0", "9.0.0"], await sandbox.TrackedAsync("Runtime"));
        Assert.False(sandbox.RuntimeOnDisk("9.0.1"));
        Assert.True(sandbox.RuntimeOnDisk("9.0.0"));
        Assert.True(sandbox.RuntimeOnDisk("8.0.0"));
    }

    [DotnetUpFact]
    public async Task GuardedUninstall_AspNetCore_KeepsCoreRuntimeOfSameVersion()
    {
        using Sandbox sandbox = fixture.CreateSandbox(output,
            [Runtime8, Spec("Runtime", "9.0.0"), Spec("ASPNETCore", "9.0.0")], includeAspNetCore: true);

        Assert.Equal(0, await sandbox.RunGuardedAsync(InstallGuard.UninstallArgsFor("ASP.NET Core", "9.0.0")));

        Assert.Empty(await sandbox.TrackedAsync("ASPNETCore"));
        // 9.0.1 had no spec at all — the guard pins it rather than letting cleanup delete it.
        Assert.Equal(["8.0.0", "9.0.0", "9.0.1"], await sandbox.TrackedAsync("Runtime"));
        Assert.False(Directory.Exists(sandbox.SharedDir("Microsoft.AspNetCore.App", "9.0.0")));
        Assert.True(sandbox.RuntimeOnDisk("9.0.0"));
    }

    // Pins written by a guarded install protect other versions even from a later, unguarded cleanup.
    [DotnetUpFact]
    public async Task GuardedInstall_PinsOtherVersions_SoLaterCleanupKeepsThem()
    {
        using Sandbox sandbox = fixture.CreateSandbox(output, [Runtime8, Channel9]);

        Assert.Equal(0, await sandbox.RunGuardedAsync("runtime install 8.0.0"));
        Assert.Equal(0, await sandbox.RunAsync($"runtime uninstall 8.0.0 --source all {sandbox.InstallPathArg}"));

        Assert.Equal(["9.0.0", "9.0.1"], await sandbox.TrackedAsync("Runtime"));
        Assert.True(sandbox.RuntimeOnDisk("9.0.0"));
        Assert.True(sandbox.RuntimeOnDisk("9.0.1"));
    }

    private static object Spec(string component, string versionOrChannel) => new Dictionary<string, object?>
    {
        ["component"] = component,
        ["versionOrChannel"] = versionOrChannel,
        ["installSource"] = "Explicit",
        ["globalJsonPath"] = null,
    };

    public sealed class Sandbox(string dotnetUp, string dir, ITestOutputHelper output) : IDisposable
    {
        public string Root { get; } = Path.Combine(dir, "root");
        public string Manifest { get; } = Path.Combine(dir, "m.json");
        public string InstallPathArg => $"--install-path \"{Root}\"";

        public Task<int> RunGuardedAsync(string args) => InstallGuard.RunGuardedAsync(
            $"{args} {InstallPathArg}", RunAsync, ReadListAsync, output.WriteLine);

        public async Task<int> RunAsync(string args)
        {
            ProcessResult result = await ProcessRunner.RunAsync(dotnetUp, $"{args} --manifest-path \"{Manifest}\"");
            output.WriteLine($"$ dotnetup {args} -> {result.ExitCode}\n{result.Output}{result.Error}");
            return result.ExitCode;
        }

        public async Task<List<string>> TrackedAsync(string component)
        {
            InstallGuard.ManifestState state = InstallGuard.ParseListJson((await ReadListAsync())!)!;
            return state.Installations
                .Where(i => i.Component == component)
                .Select(i => i.Version)
                .Order(Comparer<string>.Create(SdkSearchService.CompareSdkVersions))
                .ToList();
        }

        public string SharedDir(string framework, string version) => Path.Combine(Root, "shared", framework, version);

        public bool RuntimeOnDisk(string version) => Directory.Exists(SharedDir("Microsoft.NETCore.App", version));

        private async Task<string?> ReadListAsync()
        {
            ProcessResult result = await ProcessRunner.RunAsync(dotnetUp, $"list --format Json --manifest-path \"{Manifest}\"");
            return result.ExitCode == 0 ? result.Output : null;
        }

        public void Dispose()
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}

/// <summary>
/// Downloads runtimes 8.0.0, 9.0.0, 9.0.1 and ASP.NET Core 9.0.0 once into a cached baseline
/// (reused across test runs); each test gets its own copy with a custom manifest.
/// </summary>
public sealed class DotnetUpSandboxFixture : IAsyncLifetime
{
    private static readonly string Baseline = Path.Combine(Path.GetTempPath(), "dsm-it-dotnetup-baseline-v1");
    private static readonly string[] BaselineVersions = ["8.0.0", "9.0.0", "9.0.1"];

    public async Task InitializeAsync()
    {
        string? dotnetUp = DotnetUpFactAttribute.Locate();
        if (dotnetUp is null || await IsBaselineReadyAsync(dotnetUp))
            return;

        if (Directory.Exists(Baseline))
            Directory.Delete(Baseline, recursive: true);
        Directory.CreateDirectory(Baseline);

        string root = Path.Combine(Baseline, "root");
        string manifest = Path.Combine(Baseline, "m.json");
        ProcessResult result = await ProcessRunner.RunAsync(dotnetUp,
            $"runtime install {string.Join(' ', BaselineVersions)} aspnetcore@9.0.0 " +
            $"--install-path \"{root}\" --manifest-path \"{manifest}\" --no-progress");
        if (result.ExitCode != 0 || !await IsBaselineReadyAsync(dotnetUp))
            throw new InvalidOperationException($"Could not prepare dotnetup baseline:\n{result.Output}{result.Error}");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public InstallGuardDotnetUpTests.Sandbox CreateSandbox(ITestOutputHelper output, IEnumerable<object> specs, bool includeAspNetCore = false)
    {
        string dir = Path.Combine(Path.GetTempPath(), "dsm-it-" + Guid.NewGuid().ToString("N")[..8]);
        CopyDirectory(Baseline, dir);
        File.Delete(Path.Combine(dir, "m.json.sha256"));

        string manifestPath = Path.Combine(dir, "m.json");
        JsonNode manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        JsonNode dotnetRoot = manifest["dotnetRoots"]![0]!;
        dotnetRoot["path"] = Path.Combine(dir, "root");
        dotnetRoot["installSpecs"] = JsonSerializer.SerializeToNode(specs.ToList());

        if (!includeAspNetCore)
        {
            // Drop ASP.NET Core entirely so each Microsoft.NETCore.App folder belongs to exactly one installation.
            JsonArray installations = dotnetRoot["installations"]!.AsArray();
            foreach (JsonNode? node in installations.Where(n => (string?)n!["component"] == "ASPNETCore").ToList())
                installations.Remove(node);
            Directory.Delete(Path.Combine(dir, "root", "shared", "Microsoft.AspNetCore.App"), recursive: true);
        }

        File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return new InstallGuardDotnetUpTests.Sandbox(DotnetUpFactAttribute.Locate()!, dir, output);
    }

    private static async Task<bool> IsBaselineReadyAsync(string dotnetUp)
    {
        string manifest = Path.Combine(Baseline, "m.json");
        if (!File.Exists(manifest))
            return false;

        ProcessResult result = await ProcessRunner.RunAsync(dotnetUp, $"list --format Json --manifest-path \"{manifest}\"");
        InstallGuard.ManifestState? state = result.ExitCode == 0 ? InstallGuard.ParseListJson(result.Output) : null;
        return state is not null
            && BaselineVersions.All(v => state.Installations.Any(i => i.Component == "Runtime" && i.Version == v))
            && state.Installations.Any(i => i.Component == "ASPNETCore" && i.Version == "9.0.0");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (string sub in Directory.GetDirectories(source))
            CopyDirectory(sub, Path.Combine(destination, Path.GetFileName(sub)));
    }
}

/// <summary>Skips the test unless a dotnetup executable is available.</summary>
public sealed class DotnetUpFactAttribute : FactAttribute
{
    public DotnetUpFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("DSM_SKIP_DOTNETUP_TESTS") == "1")
            Skip = "DSM_SKIP_DOTNETUP_TESTS=1";
        else if (Locate() is null)
            Skip = "dotnetup is not installed";
    }

    public static string? Locate()
    {
        string exe = OperatingSystem.IsWindows() ? "dotnetup.exe" : "dotnetup";
        string home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnetup", exe);
        if (File.Exists(home))
            return home;

        return (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir, exe))
            .FirstOrDefault(File.Exists);
    }
}
