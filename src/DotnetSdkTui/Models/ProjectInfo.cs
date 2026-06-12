namespace DotnetSdkTui.Models;

public enum ProjectType
{
    Solution,
    SolutionX,
    CSharpProject
}

public sealed record ProjectInfo(string FilePath, ProjectType ProjectType)
{
    public string FileName => Path.GetFileName(FilePath);
}
