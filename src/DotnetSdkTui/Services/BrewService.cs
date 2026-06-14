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

    /// <summary>Checks whether the brew command is available on the system PATH.</summary>
    public static bool IsInstalled() => ProcessRunner.IsCommandAvailable("brew");

    /// <summary>
    /// Lists installed formulae via <c>brew list --versions --formula</c>.
    /// Each line has the format: <c>NAME VERSION [VERSION...]</c>.
    /// </summary>
    public static async Task<List<BrewPackage>> ListInstalledAsync(CancellationToken ct = default)
    {
        ProcessResult result = await ProcessRunner.RunAsync("brew", "list --versions --formula", ct: ct);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
            return [];

        var packages = new List<BrewPackage>();
        foreach (string rawLine in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = rawLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;

            string name = parts[0];
            // Last token is the most recently installed version
            string? version = parts.Length > 1 ? parts[^1] : null;
            packages.Add(new BrewPackage(name, version, version, null, true));
        }

        packages.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return packages;
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
