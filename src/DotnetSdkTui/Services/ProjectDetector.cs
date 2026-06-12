using DotnetSdkTui.Models;

namespace DotnetSdkTui.Services;

public static class ProjectDetector
{
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

    public static bool HasProject(string? directory = null)
    {
        return Detect(directory).Count > 0;
    }

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
