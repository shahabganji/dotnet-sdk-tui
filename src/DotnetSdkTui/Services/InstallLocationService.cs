namespace DotnetSdkTui.Services;

/// <summary>
/// Decides when dotnetup SDK/runtime commands need install-root pinning so they target the same
/// root the active <c>dotnet</c> CLI reads.
/// </summary>
/// <remarks>
/// dotnetup 0.2.0+ defaults new installs to its own root (e.g.
/// <c>~/Library/Application Support/dotnetup/dotnet</c>). When PATH/DOTNET_ROOT point at a
/// different root, those installs are invisible to the active <c>dotnet</c>. dsm therefore pins
/// install/uninstall commands to the active root via <c>--install-path</c> when a user-writable
/// one exists. For installs, <c>--set-default-install</c> is also added so PATH/DOTNET_ROOT stay
/// in sync. Arguments that already carry an explicit <c>--install-path</c> are passed through
/// untouched — dsm does not second-guess an explicitly located command.
/// </remarks>
public static class InstallLocationService
{
    /// <summary>
    /// Appends install-root flags for dotnetup SDK/runtime commands:
    /// <list type="bullet">
    /// <item><description><c>sdk/runtime install</c>: adds <c>--install-path</c> and <c>--set-default-install</c></description></item>
    /// <item><description><c>sdk/runtime uninstall</c>: adds <c>--install-path</c> only</description></item>
    /// </list>
    /// Returns every other command's arguments unchanged, and any arguments that already carry
    /// <c>--install-path</c> untouched.
    /// </summary>
    public static string AugmentInstallArgs(string command, string arguments) =>
        ShouldAugmentInstall(command, arguments)
            ? AugmentInstallArgs(command, arguments, GetActiveInstallRoot())
            : ShouldAugmentUninstall(command, arguments)
                ? AugmentUninstallArgs(arguments, GetActiveInstallRoot())
                : arguments;

    /// <summary>
    /// Core of <see cref="AugmentInstallArgs(string,string)"/> with an explicit install root.
    /// A null <paramref name="installRoot"/> lets dotnetup choose its default path.
    /// </summary>
    public static string AugmentInstallArgs(string command, string arguments, string? installRoot)
    {
        if (ShouldAugmentUninstall(command, arguments))
            return AugmentUninstallArgs(arguments, installRoot);
        if (!ShouldAugmentInstall(command, arguments))
            return arguments;

        string result = arguments;
        if (installRoot is not null)
            result += $" --install-path \"{installRoot}\"";
        if (!result.Contains("--set-default-install", StringComparison.OrdinalIgnoreCase))
            result += " --set-default-install";
        return result;
    }

    /// <summary>
    /// Core uninstall augmentation with an explicit install root. A null root leaves uninstall
    /// arguments untouched so dotnetup can fall back to its own resolution.
    /// </summary>
    public static string AugmentUninstallArgs(string arguments, string? installRoot)
    {
        if (!IsUninstallCommand(arguments)
            || arguments.Contains("--install-path", StringComparison.OrdinalIgnoreCase)
            || installRoot is null)
            return arguments;

        return $"{arguments} --install-path \"{installRoot}\"";
    }

    /// <summary>
    /// Resolves the root of the currently-active dotnet installation: the directory of the
    /// <c>dotnet</c> muxer found on PATH, falling back to <c>DOTNET_ROOT</c>. Returns null when
    /// no root exists or the root lies outside the user profile — system-wide installs
    /// (e.g. <c>/usr/local/share/dotnet</c>) cannot take dotnetup's user-level installs.
    /// </summary>
    public static string? GetActiveInstallRoot() =>
        ResolveActiveInstallRoot(
            Environment.GetEnvironmentVariable("DOTNET_ROOT"),
            FindDotnetMuxerOnPath(),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Directory.Exists);

    /// <summary>
    /// Core of <see cref="GetActiveInstallRoot"/> with explicit environment probes.
    /// The muxer is authoritative when its directory exists — <c>dotnet --list-sdks</c> reads
    /// from the muxer's own root, not <c>DOTNET_ROOT</c> — even when that disqualifies the
    /// result: installing anywhere else would be invisible to the active dotnet.
    /// </summary>
    public static string? ResolveActiveInstallRoot(
        string? dotnetRootEnv,
        string? muxerPath,
        string userHome,
        Func<string, bool> directoryExists)
    {
        string? root = null;

        if (!string.IsNullOrWhiteSpace(muxerPath))
        {
            // Path.GetDirectoryName would rewrite separators to the host platform's; trim to
            // the last separator instead so the root keeps the muxer path's own separators.
            ReadOnlySpan<char> muxer = muxerPath.AsSpan().Trim();
            int lastSeparator = muxer.LastIndexOfAny('/', '\\');
            if (lastSeparator > 0)
            {
                string muxerDir = Normalize(muxer[..lastSeparator].ToString());
                if (muxerDir.Length > 0 && directoryExists(muxerDir))
                    root = muxerDir;
            }
        }

        if (root is null)
        {
            string envRoot = Normalize(dotnetRootEnv);
            if (envRoot.Length > 0 && directoryExists(envRoot))
                root = envRoot;
        }

        if (root is null)
            return null;

        string home = Normalize(userHome);
        bool underHome = root.Equals(home, StringComparison.OrdinalIgnoreCase)
            || (root.Length > home.Length
                && root[home.Length] is '/' or '\\'
                && root.StartsWith(home, StringComparison.OrdinalIgnoreCase));

        return underHome ? root : null;
    }

    /// <summary>
    /// True when the command is a dotnetup install whose arguments carry no explicit
    /// <c>--install-path</c> — the only case augmentation (and the install-root probe) applies to.
    /// </summary>
    private static bool ShouldAugmentInstall(string command, string arguments) =>
        command.Equals("dotnetup", StringComparison.OrdinalIgnoreCase)
        && IsInstallCommand(arguments)
        && !arguments.Contains("--install-path", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when the command is a dotnetup uninstall whose arguments carry no explicit
    /// <c>--install-path</c>.
    /// </summary>
    private static bool ShouldAugmentUninstall(string command, string arguments) =>
        command.Equals("dotnetup", StringComparison.OrdinalIgnoreCase)
        && IsUninstallCommand(arguments)
        && !arguments.Contains("--install-path", StringComparison.OrdinalIgnoreCase);

    /// <summary>Matches <c>sdk install</c> and <c>runtime install</c> at the start of the arguments.</summary>
    private static bool IsInstallCommand(string arguments)
    {
        ReadOnlySpan<char> args = arguments.AsSpan().TrimStart();
        foreach (string verb in (ReadOnlySpan<string>)["sdk install", "runtime install"])
        {
            if (args.StartsWith(verb, StringComparison.OrdinalIgnoreCase)
                && (args.Length == verb.Length || char.IsWhiteSpace(args[verb.Length])))
                return true;
        }
        return false;
    }

    /// <summary>Matches <c>sdk uninstall</c> and <c>runtime uninstall</c> at the start of the arguments.</summary>
    private static bool IsUninstallCommand(string arguments)
    {
        ReadOnlySpan<char> args = arguments.AsSpan().TrimStart();
        foreach (string verb in (ReadOnlySpan<string>)["sdk uninstall", "runtime uninstall"])
        {
            if (args.StartsWith(verb, StringComparison.OrdinalIgnoreCase)
                && (args.Length == verb.Length || char.IsWhiteSpace(args[verb.Length])))
                return true;
        }
        return false;
    }

    /// <summary>Trims surrounding whitespace and trailing path separators for stable comparison.</summary>
    private static string Normalize(string? path) =>
        path?.AsSpan().Trim().TrimEnd("/\\").ToString() ?? "";

    /// <summary>
    /// Finds the <c>dotnet</c> muxer on PATH, resolving symlinks (e.g. Homebrew shims) so the
    /// containing directory reflects the real install root.
    /// </summary>
    private static string? FindDotnetMuxerOnPath()
    {
        string fileName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        string path = Environment.GetEnvironmentVariable("PATH") ?? "";

        foreach (string dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string candidate = Path.Combine(dir, fileName);
            if (!File.Exists(candidate))
                continue;

            try
            {
                return File.ResolveLinkTarget(candidate, returnFinalTarget: true)?.FullName ?? candidate;
            }
            catch (Exception)
            {
                // Best-effort probe: any failure to resolve the link (permissions, hostile PATH
                // entry, exotic filesystem) must degrade to the unresolved path, never propagate
                // into command execution.
                return candidate;
            }
        }

        return null;
    }
}
