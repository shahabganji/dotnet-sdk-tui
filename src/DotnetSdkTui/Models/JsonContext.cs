using System.Text.Json.Serialization;

namespace DotnetSdkTui.Models;

/// <summary>
/// Source-generated JSON serialization context for dotnetup and dotnet CLI models.
/// </summary>
[JsonSerializable(typeof(SdkListResponse))]
[JsonSerializable(typeof(DotnetUpInfo))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class AppJsonContext : JsonSerializerContext
{
}

/// <summary>Represents the type of a detected project file.</summary>
public enum ProjectType
{
    /// <summary>A .sln solution file.</summary>
    Solution,

    /// <summary>A .slnx solution file.</summary>
    SolutionX,

    /// <summary>A .csproj project file.</summary>
    CSharpProject
}

/// <summary>
/// Describes a detected project file on disk.
/// </summary>
/// <param name="FilePath">Full path to the project or solution file.</param>
/// <param name="ProjectType">The type of project file detected.</param>
public sealed record ProjectInfo(string FilePath, ProjectType ProjectType)
{
    /// <summary>Gets just the file name portion of the path.</summary>
    public string FileName => Path.GetFileName(FilePath);
}
