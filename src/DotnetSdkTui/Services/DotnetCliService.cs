using DotnetSdkTui.Models;

namespace DotnetSdkTui.Services;

public static class DotnetCliService
{
    public static Task<ProcessResult> RestoreAsync(ProjectInfo project, CancellationToken ct = default) =>
        ExecuteAsync("restore", project, ct);

    public static Task<ProcessResult> BuildAsync(ProjectInfo project, CancellationToken ct = default) =>
        ExecuteAsync("build", project, ct);

    public static Task<ProcessResult> TestAsync(ProjectInfo project, CancellationToken ct = default) =>
        ExecuteAsync("test", project, ct);

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

    public static Task<ProcessResult> PublishAsync(ProjectInfo project, CancellationToken ct = default) =>
        ExecuteAsync("publish", project, ct);

    private static Task<ProcessResult> ExecuteAsync(string verb, ProjectInfo project, CancellationToken ct)
    {
        string projectArgument = ProjectDetector.GetDotnetArgument(project);
        string workingDirectory = Path.GetDirectoryName(project.FilePath) ?? Directory.GetCurrentDirectory();

        if (ProcessRunner.IsCommandAvailable("dotnetup"))
        {
            return ProcessRunner.RunWithLiveOutputAsync("dotnetup", $"dotnet {verb} {projectArgument}", workingDirectory, ct);
        }

        return ProcessRunner.RunWithLiveOutputAsync("dotnet", $"{verb} {projectArgument}", workingDirectory, ct);
    }
}
