using DotnetSdkTui.Models;

namespace DotnetSdkTui.Services;

/// <summary>
/// Service for interacting with the Homebrew (brew) CLI.
/// Mirrors <see cref="DotnetUpService"/>: it never installs anything directly —
/// install/uninstall are surfaced as command tuples the App runs interactively.
/// </summary>
public static class BrewService
{
    /// <summary>Max search results to enrich + display (brew's catalog is huge).</summary>
    public const int MaxSearchResults = 25;

    /// <summary>
    /// Environment for non-interactive brew runs: disables "Ask mode" so install/upgrade
    /// don't prompt "Do you want to proceed? [y/n]" — the user already chose the action.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> NonInteractiveEnv =
        new Dictionary<string, string> { ["HOMEBREW_NO_ASK"] = "1" };

    /// <summary>Whether Homebrew is supported on this platform (macOS only).</summary>
    public static bool IsSupported() => OperatingSystem.IsMacOS();

    /// <summary>Checks whether brew is supported and available on the system PATH.</summary>
    public static bool IsInstalled() => IsSupported() && ProcessRunner.IsCommandAvailable("brew");

    /// <summary>
    /// Lists installed formulae via <c>brew list --versions --formula</c>.
    /// Each line has the format: <c>NAME VERSION [VERSION...]</c>.
    /// </summary>
    public static async Task<List<BrewPackage>> ListInstalledAsync(CancellationToken ct = default)
    {
        ProcessResult result = await ProcessRunner.RunAsync("brew", "list --versions --formula", ct: ct);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
            return [];

        // Latest-available versions for formulae that have an update pending.
        Dictionary<string, string> outdated = await GetOutdatedAsync(ct);

        var packages = new List<BrewPackage>();
        foreach (string rawLine in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = rawLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;

            string name = parts[0];
            // Last token is the most recently installed version
            string? installed = parts.Length > 1 ? parts[^1] : null;
            // If outdated, the latest available differs; otherwise it equals the installed version.
            string? latest = outdated.TryGetValue(name, out string? newer) ? newer : installed;
            packages.Add(new BrewPackage(name, installed, latest, null, true));
        }

        packages.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return packages;
    }

    /// <summary>
    /// Maps formula name → latest available version for installed formulae that are outdated,
    /// via <c>brew outdated --json=v2</c>. Returns an empty map on failure (best-effort).
    /// </summary>
    private static async Task<Dictionary<string, string>> GetOutdatedAsync(CancellationToken ct)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            BrewOutdatedResponse? outdated = await ProcessRunner.RunJsonAsync(
                "brew", "outdated --json=v2 --formula", AppJsonContext.Default.BrewOutdatedResponse, ct: ct);
            if (outdated?.Formulae is null)
                return map;

            foreach (BrewOutdated f in outdated.Formulae)
            {
                if (!string.IsNullOrWhiteSpace(f.Name) && !string.IsNullOrWhiteSpace(f.CurrentVersion))
                    map[f.Name] = f.CurrentVersion;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* best-effort: treat all as up-to-date */ }

        return map;
    }

    /// <summary>
    /// Searches available formulae via <c>brew search</c>, then enriches the top results
    /// with version and description via <c>brew info --json=v2</c>.
    /// </summary>
    public static async Task<List<BrewPackage>> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        // Restrict to formulae: keeps `brew info --formula` enrichment reliable
        // (casks in the result set would make the batched info call fail).
        ProcessResult search = await ProcessRunner.RunAsync("brew", $"search --formula {query}", ct: ct);
        if (search.ExitCode != 0 || string.IsNullOrWhiteSpace(search.Output))
            return [];

        var names = new List<string>();
        foreach (string rawLine in search.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Skip section headers like "==> Formulae"
            if (rawLine.StartsWith("==>", StringComparison.Ordinal))
                continue;
            names.Add(rawLine);
            if (names.Count >= MaxSearchResults)
                break;
        }

        if (names.Count == 0)
            return [];

        // Enrich with version + description + installed status (best-effort).
        var byName = await GetInfoAsync(names, ct);

        var results = new List<BrewPackage>(names.Count);
        foreach (string name in names)
        {
            if (byName.TryGetValue(name, out BrewPackage? pkg))
                results.Add(pkg);
            else
                results.Add(new BrewPackage(name, null, null, null, false));
        }
        return results;
    }

    /// <summary>The command to install a formula (run interactively by the App).</summary>
    public static (string Command, string Args) InstallCommand(string name) => ("brew", $"install {name}");

    /// <summary>The command to uninstall a formula (run interactively by the App).</summary>
    public static (string Command, string Args) UninstallCommand(string name) => ("brew", $"uninstall {name}");

    /// <summary>The command to upgrade a formula to its latest version (run interactively by the App).</summary>
    public static (string Command, string Args) UpgradeCommand(string name) => ("brew", $"upgrade {name}");

    /// <summary>
    /// Fetches version/description/installed-state for a set of formulae via
    /// <c>brew info --json=v2</c>. Returns an empty map on any failure (best-effort enrichment).
    /// </summary>
    private static async Task<Dictionary<string, BrewPackage>> GetInfoAsync(List<string> names, CancellationToken ct)
    {
        var map = new Dictionary<string, BrewPackage>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string args = "info --json=v2 --formula " + string.Join(' ', names);
            BrewInfoResponse? info = await ProcessRunner.RunJsonAsync("brew", args, AppJsonContext.Default.BrewInfoResponse, ct: ct);
            if (info?.Formulae is null)
                return map;

            foreach (BrewFormula f in info.Formulae)
            {
                if (string.IsNullOrWhiteSpace(f.Name))
                    continue;

                string? installedVersion = f.Installed is { Count: > 0 } ? f.Installed[^1].Version : null;
                map[f.Name] = new BrewPackage(
                    f.Name,
                    installedVersion,
                    f.Versions?.Stable,
                    f.Desc,
                    installedVersion is not null);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* best-effort: fall back to names only */ }

        return map;
    }
}
