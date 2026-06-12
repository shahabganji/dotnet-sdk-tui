using Spectre.Console;
using Spectre.Console.Rendering;
using DotnetSdkTui.Models;
using DotnetSdkTui.Services;
using DotnetSdkTui.Theme;

namespace DotnetSdkTui.Views;

public sealed class ProjectView : IView
{
    public string Name => "Project";
    public string Icon => "🔥";

    private List<ProjectInfo> _projects = [];
    private int _selectedProject;
    private bool _running;
    private string? _lastAction;
    private readonly List<string> _outputLines = [];
    private int _outputScroll;
    private ProcessResult? _lastResult;

    public bool IsRunning => _running;

    public Task ActivateAsync()
    {
        _projects = ProjectDetector.Detect();
        _selectedProject = 0;
        return Task.CompletedTask;
    }

    public IRenderable Render()
    {
        var parts = new List<IRenderable>();

        if (_projects.Count == 0)
        {
            parts.Add(MarioTheme.Muted("No .sln, .slnx, or .csproj files detected in current directory."));
            parts.Add(Text.Empty);
            parts.Add(MarioTheme.Info($"Current: {Directory.GetCurrentDirectory()}"));
            return MarioTheme.ContentPanel("Project Actions", new Rows(parts));
        }

        // Project selector
        if (_projects.Count > 1)
        {
            parts.Add(new Markup($"[{MarioTheme.Yellow} bold]Projects:[/]"));
            for (int i = 0; i < _projects.Count; i++)
            {
                string prefix = i == _selectedProject ? "►" : " ";
                string style = i == _selectedProject ? $"{MarioTheme.Yellow} bold" : MarioTheme.White;
                parts.Add(new Markup($"  [{style}]{prefix} {Markup.Escape(_projects[i].FileName)}[/]"));
            }
            parts.Add(Text.Empty);
        }
        else
        {
            parts.Add(new Markup($"[{MarioTheme.Yellow} bold]Project:[/] [{MarioTheme.White}]{Markup.Escape(_projects[0].FileName)}[/]"));
            parts.Add(Text.Empty);
        }

        // Action bar
        if (!_running)
        {
            var actions = new Markup($"[{MarioTheme.Blue}]r[/]:[{MarioTheme.White}]Restore[/]  " +
                                     $"[{MarioTheme.Blue}]b[/]:[{MarioTheme.White}]Build[/]  " +
                                     $"[{MarioTheme.Blue}]t[/]:[{MarioTheme.White}]Test[/]  " +
                                     $"[{MarioTheme.Blue}]n[/]:[{MarioTheme.White}]Run[/]  " +
                                     $"[{MarioTheme.Blue}]p[/]:[{MarioTheme.White}]Publish[/]");
            parts.Add(actions);
            parts.Add(Text.Empty);
        }

        // Output panel
        if (_running)
        {
            parts.Add(new Markup($"[{MarioTheme.Yellow}]Running {Markup.Escape(_lastAction ?? "")}...[/]"));
            parts.Add(Text.Empty);
        }

        if (_outputLines.Count > 0)
        {
            int maxLines = 20;
            int start = Math.Max(0, _outputLines.Count - maxLines - _outputScroll);
            int end = Math.Min(_outputLines.Count, start + maxLines);

            var outputParts = new List<IRenderable>();
            for (int i = start; i < end; i++)
            {
                string line = _outputLines[i];
                string color = line.StartsWith("ERR|") ? MarioTheme.Red : MarioTheme.Gray;
                string text = line.StartsWith("ERR|") ? line[4..] : line;
                outputParts.Add(new Markup($"[{color}]{Markup.Escape(text)}[/]"));
            }

            var outputPanel = new Panel(new Rows(outputParts))
                .Header($"[{MarioTheme.Brown}] Output [/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(new Color(200, 76, 9))
                .Expand();

            parts.Add(outputPanel);
        }

        // Result summary
        if (_lastResult is not null && !_running)
        {
            if (_lastResult.ExitCode == 0)
                parts.Add(MarioTheme.Success($"{_lastAction} completed in {_lastResult.Duration.TotalSeconds:F1}s"));
            else
                parts.Add(MarioTheme.Error($"{_lastAction} failed (exit code {_lastResult.ExitCode}) in {_lastResult.Duration.TotalSeconds:F1}s"));
        }

        return MarioTheme.ContentPanel("Project Actions", new Rows(parts));
    }

    public string GetStatusHints()
    {
        if (_projects.Count == 0) return "No project detected";
        if (_running) return $"Running {_lastAction}...  (output streaming)";
        if (_projects.Count > 1)
            return "←→:Select project  r:Restore  b:Build  t:Test  n:Run  p:Publish  c:Clear";
        return "r:Restore  b:Build  t:Test  n:Run  p:Publish  c:Clear";
    }

    public async Task<KeyResult> HandleKeyAsync(ConsoleKeyInfo key)
    {
        if (_running) return KeyResult.NotHandled;

        if (_projects.Count == 0) return KeyResult.NotHandled;

        switch (key.Key)
        {
            case ConsoleKey.LeftArrow when _projects.Count > 1:
                _selectedProject = Math.Max(0, _selectedProject - 1);
                return KeyResult.Handled;

            case ConsoleKey.RightArrow when _projects.Count > 1:
                _selectedProject = Math.Min(_projects.Count - 1, _selectedProject + 1);
                return KeyResult.Handled;

            case ConsoleKey.R:
                await RunActionAsync("Restore", DotnetCliService.RestoreAsync);
                return KeyResult.Handled;

            case ConsoleKey.B:
                await RunActionAsync("Build", DotnetCliService.BuildAsync);
                return KeyResult.Handled;

            case ConsoleKey.T:
                await RunActionAsync("Test", DotnetCliService.TestAsync);
                return KeyResult.Handled;

            case ConsoleKey.N:
                await RunActionAsync("Run", DotnetCliService.RunAsync);
                return KeyResult.Handled;

            case ConsoleKey.P:
                await RunActionAsync("Publish", DotnetCliService.PublishAsync);
                return KeyResult.Handled;

            case ConsoleKey.C:
                _outputLines.Clear();
                _lastResult = null;
                _lastAction = null;
                _outputScroll = 0;
                return KeyResult.Handled;

            case ConsoleKey.UpArrow:
                _outputScroll = Math.Min(_outputScroll + 1, Math.Max(0, _outputLines.Count - 5));
                return KeyResult.Handled;

            case ConsoleKey.DownArrow:
                _outputScroll = Math.Max(0, _outputScroll - 1);
                return KeyResult.Handled;

            default:
                return KeyResult.NotHandled;
        }
    }

    private async Task RunActionAsync(string actionName, Func<ProjectInfo, CancellationToken, Task<ProcessResult>> action)
    {
        var project = _projects[_selectedProject];
        _running = true;
        _lastAction = actionName;
        _outputLines.Clear();
        _outputScroll = 0;
        _lastResult = null;

        try
        {
            // Use callback-based runner for live output in the panel
            string verb = actionName.ToLowerInvariant();
            string projectArg = ProjectDetector.GetDotnetArgument(project);
            string workingDir = Path.GetDirectoryName(project.FilePath) ?? Directory.GetCurrentDirectory();

            string cmd = ProcessRunner.IsCommandAvailable("dotnetup") ? "dotnetup" : "dotnet";
            string args = ProcessRunner.IsCommandAvailable("dotnetup")
                ? $"dotnet {verb} {projectArg}"
                : $"{verb} {projectArg}";

            _lastResult = await ProcessRunner.RunWithCallbackAsync(
                cmd, args,
                line => _outputLines.Add(line),
                errLine => _outputLines.Add($"ERR|{errLine}"),
                workingDir);
        }
        catch (Exception ex)
        {
            _outputLines.Add($"ERR|{ex.Message}");
            _lastResult = new ProcessResult(-1, "", ex.Message, TimeSpan.Zero);
        }
        finally
        {
            _running = false;
        }
    }
}
