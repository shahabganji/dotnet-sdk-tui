namespace DotnetSdkTui.Services;

/// <summary>
/// Decides where dotnetup should place new SDK/runtime installs so they land in the root the
/// active <c>dotnet</c> CLI reads, keeping them visible to <c>dotnet --list-sdks</c>.
/// </summary>
/// <remarks>
/// dotnetup 0.2.0+ defaults new installs to its own root (e.g.
/// <c>~/Library/Application Support/dotnetup/dotnet</c>). When PATH/DOTNET_ROOT point at a
/// different root, those installs are invisible to the active <c>dotnet</c>. dsm therefore pins
/// installs to the active root via <c>--install-path</c> when a user-writable one exists, and
/// otherwise lets dotnetup use its default; in both cases <c>--set-default-install</c> keeps the
/// environment wiring (PATH/DOTNET_ROOT) in sync. Arguments that already carry an explicit
/// <c>--install-path</c> are passed through completely untouched — dsm does not second-guess an
/// explicitly located install, not even to add <c>--set-default-install</c>.
/// </remarks>
public static class InstallLocationService
{
    /// <summary>
    /// Appends <c>--install-path</c> and <c>--set-default-install</c> to dotnetup
    /// <c>sdk install</c>/<c>runtime install</c> arguments; returns every other command's
    /// arguments unchanged, and arguments that already carry <c>--install-path</c> untouched.
    /// </summary>
    public static string AugmentInstallArgs(string command, string arguments) =>
        ShouldAugment(command, arguments)
            ? AugmentInstallArgs(command, arguments, GetActiveInstallRoot())
            : arguments;

    /// <summary>
    /// Core of <see cref="AugmentInstallArgs(string,string)"/> with an explicit install root.
    /// A null <paramref name="installRoot"/> lets dotnetup choose its default path.
    /// </summary>
    public static string AugmentInstallArgs(string command, string arguments, string? installRoot)
    {
        if (!ShouldAugment(command, arguments))
            return arguments;

        string result = arguments;
        if (installRoot is not null)
            result += $" --install-path \"{installRoot}\"";
        if (!result.Contains("--set-default-install", StringComparison.OrdinalIgnoreCase))
            result += " --set-default-install";
        return result;
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
    private static bool ShouldAugment(string command, string arguments) =>
        command.Equals("dotnetup", StringComparison.OrdinalIgnoreCase)
        && IsInstallCommand(arguments)
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
