using DotnetSdkTui.Models;

namespace DotnetSdkTui.Services;

/// <summary>
/// Provides methods to execute common dotnet CLI project commands (restore, build, test, run, publish).
/// Prefers dotnetup when available for correct SDK hive resolution.
/// </summary>
public static class DotnetCliService
{
    /// <summary>Runs <c>dotnet restore</c> on the specified project.</summary>
    public static Task<ProcessResult> RestoreAsync(ProjectInfo project, CancellationToken ct = default) =>
        ExecuteAsync("restore", project, ct);

    /// <summary>Runs <c>dotnet build</c> on the specified project.</summary>
    public static Task<ProcessResult> BuildAsync(ProjectInfo project, CancellationToken ct = default) =>
        ExecuteAsync("build", project, ct);

    /// <summary>Runs <c>dotnet test</c> on the specified project.</summary>
    public static Task<ProcessResult> TestAsync(ProjectInfo project, CancellationToken ct = default) =>
        ExecuteAsync("test", project, ct);

    /// <summary>
    /// Runs <c>dotnet run</c> on the specified project.
    /// Only supported for <see cref="ProjectType.CSharpProject"/> files.
    /// </summary>
    public static Task<ProcessResult> RunAsync(ProjectInfo project, CancellationToken ct = default)
    {
        if (project.ProjectType != ProjectType.CSharpProject)
        {
            return Task.FromResult(new ProcessResult(
                -1,
                string.Empty,
                "dotnet run is only supported for .csproj projects.",
                TimeSpan.Zero));
        }

        return ExecuteAsync("run", project, ct);
    }

    /// <summary>Runs <c>dotnet publish</c> on the specified project.</summary>
    public static Task<ProcessResult> PublishAsync(ProjectInfo project, CancellationToken ct = default) =>
        ExecuteAsync("publish", project, ct);

    private static Task<ProcessResult> ExecuteAsync(string verb, ProjectInfo project, CancellationToken ct)
    {
        string projectArgument = ProjectDetector.GetDotnetArgument(project);
        string workingDirectory = Path.GetDirectoryName(project.FilePath) ?? Directory.GetCurrentDirectory();

        if (ProcessRunner.IsCommandAvailable("dotnetup"))
        {
            return ProcessRunner.RunWithCallbackAsync("dotnetup", $"dotnet {verb} {projectArgument}", null, null, workingDirectory, ct);
        }

        return ProcessRunner.RunWithCallbackAsync("dotnet", $"{verb} {projectArgument}", null, null, workingDirectory, ct);
    }
}
