using DotnetSdkTui.Services;

namespace DotnetSdkTui.Tests.Services;

public sealed class InstallRemovalTests : IDisposable
{
    private readonly string _root;

    public InstallRemovalTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dsm-removal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    // --- Fixture: a dotnet folder laid out like the installers do ---

    private void Sdk(string version, string netCore, string aspNetCore)
    {
        string tfm = "net" + string.Join('.', netCore.Split('.').Take(2));
        string dir = Path.Combine(_root, "sdk", version);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "dotnet.runtimeconfig.json"),
            $$"""{ "runtimeOptions": { "tfm": "{{tfm}}", "framework": { "name": "Microsoft.NETCore.App", "version": "{{netCore}}" } } }""");
        File.WriteAllText(Path.Combine(dir, "Microsoft.NETCoreSdk.BundledVersions.props"),
            $"""
             <Project>
               <PropertyGroup><BundledNETCoreAppPackageVersion>{netCore}</BundledNETCoreAppPackageVersion></PropertyGroup>
               <ItemGroup>
                 <KnownFrameworkReference Include="Microsoft.AspNetCore.App" TargetFramework="net6.0" LatestRuntimeFrameworkVersion="6.0.36" />
                 <KnownFrameworkReference Include="Microsoft.AspNetCore.App" TargetFramework="{tfm}" LatestRuntimeFrameworkVersion="{aspNetCore}" />
               </ItemGroup>
             </Project>
             """);

        Runtime("Microsoft.NETCore.App", netCore);
        Runtime("Microsoft.AspNetCore.App", aspNetCore);
        Dir("host", "fxr", netCore);
        Dir("packs", "Microsoft.NETCore.App.Ref", netCore);
        Dir("packs", "Microsoft.NETCore.App.Host.osx-arm64", netCore);
        Dir("packs", "Microsoft.AspNetCore.App.Ref", aspNetCore);
        Dir("templates", aspNetCore);
    }

    private void Runtime(string framework, string version) => Dir("shared", framework, version);

    private string Dir(params string[] parts)
    {
        string path = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "payload.bin"), "x");
        return path;
    }

    private string FileAt(params string[] parts)
    {
        string path = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    private void CliHomeFiles()
    {
        File.WriteAllText(Path.Combine(_root, "dotnet"), "muxer");
        FileAt("LICENSE.txt");
        FileAt("8.0.100.dotnetFirstUseSentinel");
        Dir("tools", "dotnet-ef");
        Dir("toolResolverCache", "1");
        Dir("packs", "NETStandard.Library.Ref", "2.1.0");
    }

    private bool Exists(params string[] parts)
    {
        string path = Path.Combine([_root, .. parts]);
        return Directory.Exists(path) || File.Exists(path);
    }

    private RemovalResult PlanAndRun(string[] sdks, RuntimeRef[]? runtimes = null, Func<string, bool>? guard = null)
    {
        RemovalPlan plan = InstallRemoval.Plan(_root, sdks, runtimes ?? [], guard);
        RemovalResult result = InstallRemoval.Execute(plan);
        Assert.Empty(result.Errors);
        return result;
    }

    // --- Bundles ---

    [Fact]
    public void ReadBundle_ReadsRuntimeAndAspNetCoreForTheSdksOwnTfm()
    {
        Sdk("9.0.100-preview.6.24328.19", "9.0.0-preview.6.24327.7", "9.0.0-preview.6.24328.4");

        SdkBundle b = InstallRemoval.ReadBundle(_root, "9.0.100-preview.6.24328.19");

        Assert.Equal("9.0.0-preview.6.24327.7", b.NetCore);
        Assert.Equal("9.0.0-preview.6.24328.4", b.AspNetCore);
        Assert.Equal("9.0.100-preview.6", b.Band);
    }

    [Theory]
    [InlineData("/home/u/.dotnet/sdk", "/home/u/.dotnet")]
    [InlineData("/home/u/.dotnet/shared/Microsoft.NETCore.App", "/home/u/.dotnet")]
    [InlineData("/home/u/.dotnet/shared/Microsoft.NETCore.App/", "/home/u/.dotnet")]
    public void DotnetRootOf_ListedPath_ReturnsFolder(string listed, string expected)
    {
        Assert.Equal(expected, InstallRemoval.DotnetRootOf(listed));
    }

    // --- SDK removal ---

    [Fact]
    public void RemovingLastSdk_RemovesBundledRuntimesHostAndPacks_ButKeepsCliHomeFiles()
    {
        CliHomeFiles();
        Sdk("8.0.100", "8.0.0", "8.0.0");

        RemovalPlan plan = InstallRemoval.Plan(_root, ["8.0.100"], []);
        Assert.True(plan.RemovesWholeInstall);
        Assert.Empty(InstallRemoval.Execute(plan).Errors);

        Assert.False(Exists("sdk"));
        Assert.False(Exists("shared"));
        Assert.False(Exists("host"));
        Assert.False(Exists("packs"));
        Assert.False(Exists("templates"));
        Assert.False(Exists("dotnet"));
        Assert.False(Exists("LICENSE.txt"));
        Assert.True(Exists("tools", "dotnet-ef"));
        Assert.True(Exists("toolResolverCache"));
        Assert.True(Exists("8.0.100.dotnetFirstUseSentinel"));
    }

    [Fact]
    public void RemovingOneSdk_KeepsTheOtherSdksBundleAndTheMuxer()
    {
        CliHomeFiles();
        Sdk("7.0.306", "7.0.9", "7.0.9");
        Sdk("8.0.100", "8.0.0", "8.0.0");

        RemovalPlan plan = InstallRemoval.Plan(_root, ["7.0.306"], []);
        Assert.False(plan.RemovesWholeInstall);
        Assert.Empty(InstallRemoval.Execute(plan).Errors);

        Assert.False(Exists("sdk", "7.0.306"));
        Assert.False(Exists("shared", "Microsoft.NETCore.App", "7.0.9"));
        Assert.False(Exists("shared", "Microsoft.AspNetCore.App", "7.0.9"));
        Assert.False(Exists("host", "fxr", "7.0.9"));
        Assert.False(Exists("packs", "Microsoft.NETCore.App.Ref", "7.0.9"));
        Assert.False(Exists("templates", "7.0.9"));

        Assert.True(Exists("sdk", "8.0.100"));
        Assert.True(Exists("shared", "Microsoft.NETCore.App", "8.0.0"));
        Assert.True(Exists("host", "fxr", "8.0.0"));
        Assert.True(Exists("packs", "Microsoft.NETCore.App.Ref", "8.0.0"));
        Assert.True(Exists("packs", "NETStandard.Library.Ref", "2.1.0"));
        Assert.True(Exists("dotnet"));
    }

    [Fact]
    public void RemovingSdk_KeepsRuntimeAnotherSdkShipsWith()
    {
        Sdk("8.0.100", "8.0.0", "8.0.0");
        Sdk("8.0.101", "8.0.0", "8.0.0");

        RemovalPlan plan = InstallRemoval.Plan(_root, ["8.0.100"], []);
        InstallRemoval.Execute(plan);

        Assert.False(Exists("sdk", "8.0.100"));
        Assert.True(Exists("shared", "Microsoft.NETCore.App", "8.0.0"));
        Assert.True(Exists("templates", "8.0.0"));
        Assert.Contains(plan.Kept, k => k.Contains("8.0.101"));
    }

    [Fact]
    public void RemovingSdk_KeepsUnrelatedStandaloneRuntime()
    {
        Sdk("8.0.100", "8.0.0", "8.0.0");
        Runtime("Microsoft.NETCore.App", "6.0.36");
        Dir("host", "fxr", "6.0.36");

        PlanAndRun(["8.0.100"]);

        Assert.True(Exists("shared", "Microsoft.NETCore.App", "6.0.36"));
        Assert.True(Exists("host", "fxr", "6.0.36"));
        Assert.False(Exists("host", "fxr", "8.0.0"));
    }

    // --- Runtime removal ---

    [Fact]
    public void RemovingRuntimeAnSdkShipsWith_IsRefused()
    {
        Sdk("8.0.100", "8.0.0", "8.0.0");
        var runtime = new RuntimeRef(InstallRemoval.NetCoreApp, "8.0.0");

        RemovalPlan plan = InstallRemoval.Plan(_root, [], [runtime]);

        Assert.True(plan.IsEmpty);
        Assert.Contains(plan.Kept, k => k.Contains("SDK 8.0.100"));
        Assert.Equal(["8.0.100"], InstallRemoval.FindBundlingSdks(_root, runtime));
    }

    [Fact]
    public void RemovingStandaloneRuntime_KeepsTheLastHostFxr()
    {
        File.WriteAllText(Path.Combine(_root, "dotnet"), "muxer");
        Runtime("Microsoft.NETCore.App", "6.0.36");
        Runtime("Microsoft.NETCore.App", "7.0.20");
        Dir("host", "fxr", "6.0.36");

        PlanAndRun([], [new RuntimeRef(InstallRemoval.NetCoreApp, "6.0.36")]);

        Assert.False(Exists("shared", "Microsoft.NETCore.App", "6.0.36"));
        Assert.True(Exists("host", "fxr", "6.0.36"));
        Assert.True(Exists("dotnet"));
    }

    // --- Workloads ---

    [Fact]
    public void RemovingSdk_GarbageCollectsItsBandsWorkloadPacksButNotSharedOnes()
    {
        Sdk("8.0.100", "8.0.0", "8.0.0");
        Sdk("9.0.100", "9.0.0", "9.0.0");
        FileAt("metadata", "workloads", "8.0.100", "InstalledWorkloads", "wasm-tools");
        FileAt("metadata", "workloads", "9.0.100", "InstalledWorkloads", "wasm-tools");
        // Only band 8.0.100 uses this pack version …
        Dir("packs", "Microsoft.NET.Runtime.WebAssembly.Sdk", "8.0.1");
        FileAt("metadata", "workloads", "InstalledPacks", "v1", "Microsoft.NET.Runtime.WebAssembly.Sdk", "8.0.1", "8.0.100");
        // … while this one is used by both bands.
        Dir("packs", "Microsoft.NET.Sdk.WebAssembly.Pack", "9.0.0");
        FileAt("metadata", "workloads", "InstalledPacks", "v1", "Microsoft.NET.Sdk.WebAssembly.Pack", "9.0.0", "8.0.100");
        FileAt("metadata", "workloads", "InstalledPacks", "v1", "Microsoft.NET.Sdk.WebAssembly.Pack", "9.0.0", "9.0.100");
        Dir("sdk-manifests", "8.0.100", "microsoft.net.workload.mono.toolchain.current", "8.0.1");
        Dir("sdk-manifests", "9.0.100", "microsoft.net.workload.mono.toolchain.current", "9.0.0");

        PlanAndRun(["8.0.100"]);

        Assert.False(Exists("metadata", "workloads", "8.0.100"));
        Assert.False(Exists("packs", "Microsoft.NET.Runtime.WebAssembly.Sdk"));
        Assert.False(Exists("sdk-manifests", "8.0.100"));
        Assert.True(Exists("metadata", "workloads", "9.0.100", "InstalledWorkloads", "wasm-tools"));
        Assert.True(Exists("packs", "Microsoft.NET.Sdk.WebAssembly.Pack", "9.0.0"));
        Assert.True(Exists("metadata", "workloads", "InstalledPacks", "v1", "Microsoft.NET.Sdk.WebAssembly.Pack", "9.0.0", "9.0.100"));
        Assert.False(Exists("metadata", "workloads", "InstalledPacks", "v1", "Microsoft.NET.Sdk.WebAssembly.Pack", "9.0.0", "8.0.100"));
        Assert.True(Exists("sdk-manifests", "9.0.100"));
    }

    [Fact]
    public void RemovingSdk_KeepsWorkloadRuntimePacksAtItsRuntimeVersion()
    {
        Sdk("9.0.101", "9.0.1", "9.0.1");
        Sdk("9.0.102", "9.0.2", "9.0.2");
        FileAt("metadata", "workloads", "9.0.100", "InstalledWorkloads", "wasm-tools");
        Dir("packs", "Microsoft.NETCore.App.Runtime.Mono.browser-wasm", "9.0.2");
        FileAt("metadata", "workloads", "InstalledPacks", "v1", "Microsoft.NETCore.App.Runtime.Mono.browser-wasm", "9.0.2", "9.0.100");

        PlanAndRun(["9.0.102"]);

        Assert.False(Exists("packs", "Microsoft.NETCore.App.Ref", "9.0.2"));
        Assert.True(Exists("packs", "Microsoft.NETCore.App.Runtime.Mono.browser-wasm", "9.0.2"));
    }

    [Fact]
    public void RemovingSdk_KeepsBandAnotherInstallUsesAsUserLocal()
    {
        Sdk("8.0.100", "8.0.0", "8.0.0");
        FileAt("metadata", "workloads", "8.0.100", "InstalledWorkloads", "maui");
        Dir("packs", "Microsoft.Maui.Sdk", "8.0.3");
        FileAt("metadata", "workloads", "InstalledPacks", "v1", "Microsoft.Maui.Sdk", "8.0.3", "8.0.100");

        RemovalPlan plan = InstallRemoval.Plan(_root, ["8.0.100"], [], band => band == "8.0.100");
        InstallRemoval.Execute(plan);

        Assert.False(Exists("sdk"));
        Assert.True(Exists("metadata", "workloads", "8.0.100", "InstalledWorkloads", "maui"));
        Assert.True(Exists("packs", "Microsoft.Maui.Sdk", "8.0.3"));
        Assert.False(Exists("packs", "Microsoft.NETCore.App.Ref"));
    }

    [Fact]
    public void SamePath_ResolvesSymlinkedFolders()
    {
        if (OperatingSystem.IsWindows()) return;
        string real = Dir("real", "dotnet");
        Directory.CreateSymbolicLink(Path.Combine(_root, "alias"), Path.Combine(_root, "real"));

        Assert.True(InstallRemoval.SamePath(Path.Combine(_root, "alias", "dotnet"), real));
        Assert.False(InstallRemoval.SamePath(Path.Combine(_root, "alias"), real));
    }

    [Fact]
    public void Execute_RefusesPathsThroughASymlinkedFolder()
    {
        if (OperatingSystem.IsWindows()) return;
        string target = Path.Combine(Path.GetTempPath(), "dsm-shared-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(target, "Microsoft.NETCore.App", "8.0.0"));
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(_root, "shared"), target);
            var plan = new RemovalPlan(Path.GetFullPath(_root));
            plan.Paths.Add(Path.Combine(_root, "shared", "Microsoft.NETCore.App", "8.0.0"));

            RemovalResult result = InstallRemoval.Execute(plan);

            Assert.Equal(0, result.Deleted);
            Assert.Single(result.Errors);
            Assert.True(Directory.Exists(Path.Combine(target, "Microsoft.NETCore.App", "8.0.0")));
        }
        finally
        {
            Directory.Delete(target, recursive: true);
        }
    }

    [Fact]
    public void ReadInstalledIdsFromDisk_FollowsUserLocalMarker()
    {
        FileAt("metadata", "workloads", "8.0.100", "InstalledWorkloads", "wasm-tools");
        FileAt("metadata", "workloads", "8.0.100", "userlocal");
        string cliHome = Path.Combine(_root, "clihome");
        Directory.CreateDirectory(Path.Combine(cliHome, "metadata", "workloads", "8.0.100", "InstalledWorkloads"));
        File.WriteAllText(Path.Combine(cliHome, "metadata", "workloads", "8.0.100", "InstalledWorkloads", "maui"), "");

        IReadOnlyList<string>? ids = WorkloadService.ReadInstalledIdsFromDisk(_root, "8.0.100", cliHome);

        Assert.Equal(["maui", "wasm-tools"], ids);
    }

    // --- Execute safety ---

    [Fact]
    public void Execute_RefusesPathsOutsideTheFolder()
    {
        string outside = Path.Combine(Path.GetTempPath(), "dsm-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            var plan = new RemovalPlan(_root);
            plan.Paths.Add(outside);
            plan.Paths.Add(Path.Combine(_root, "..", Path.GetFileName(outside)));

            RemovalResult result = InstallRemoval.Execute(plan);

            Assert.Equal(0, result.Deleted);
            Assert.Equal(2, result.Errors.Count);
            Assert.True(Directory.Exists(outside));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void Execute_DeletesSymlinkButNotItsTarget()
    {
        if (OperatingSystem.IsWindows()) return;
        string target = Path.Combine(Path.GetTempPath(), "dsm-target-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "keep.txt"), "");
        try
        {
            Directory.CreateDirectory(Path.Combine(_root, "sdk"));
            string link = Path.Combine(_root, "sdk", "8.0.100");
            Directory.CreateSymbolicLink(link, target);
            var plan = new RemovalPlan(Path.GetFullPath(_root));
            plan.Paths.Add(link);

            RemovalResult result = InstallRemoval.Execute(plan);

            Assert.Empty(result.Errors);
            Assert.False(Directory.Exists(link));
            Assert.True(File.Exists(Path.Combine(target, "keep.txt")));
        }
        finally
        {
            Directory.Delete(target, recursive: true);
        }
    }
}
