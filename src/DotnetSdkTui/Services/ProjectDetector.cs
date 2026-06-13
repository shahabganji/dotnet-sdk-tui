using DotnetSdkTui.Models;

namespace DotnetSdkTui.Services;

/// <summary>
/// Detects .sln, .slnx, and .csproj project files in a given directory.
/// </summary>
public static class ProjectDetector
{
    /// <summary>
    /// Scans the specified (or current) directory for project files, returning them
    /// ordered by priority: .sln &gt; .slnx &gt; .csproj.
    /// </summary>
    public static List<ProjectInfo> Detect(string? directory = null)
    {
        var targetDirectory = directory ?? Directory.GetCurrentDirectory();
        var projects = new List<ProjectInfo>();

        projects.AddRange(Directory.GetFiles(targetDirectory, "*.sln", SearchOption.TopDirectoryOnly)
            .Select(path => new ProjectInfo(path, ProjectType.Solution)));
        projects.AddRange(Directory.GetFiles(targetDirectory, "*.slnx", SearchOption.TopDirectoryOnly)
            .Select(path => new ProjectInfo(path, ProjectType.SolutionX)));
        projects.AddRange(Directory.GetFiles(targetDirectory, "*.csproj", SearchOption.TopDirectoryOnly)
            .Select(path => new ProjectInfo(path, ProjectType.CSharpProject)));

        return projects
            .OrderBy(static project => GetPriority(project.ProjectType))
            .ThenBy(static project => project.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Returns whether any project file exists in the given directory.</summary>
    public static bool HasProject(string? directory = null)
    {
        return Detect(directory).Count > 0;
    }

    /// <summary>
    /// Returns the appropriate <c>dotnet</c> CLI argument for the given project
    /// (e.g. <c>--project path</c> for .csproj, or the path directly for .sln/.slnx).
    /// </summary>
    public static string GetDotnetArgument(ProjectInfo project)
    {
        return project.ProjectType == ProjectType.CSharpProject
            ? $"--project {project.FilePath}"
            : project.FilePath;
    }

    private static int GetPriority(ProjectType projectType)
    {
        return projectType switch
        {
            ProjectType.Solution => 0,
            ProjectType.SolutionX => 1,
            ProjectType.CSharpProject => 2,
            _ => int.MaxValue
        };
    }
}
