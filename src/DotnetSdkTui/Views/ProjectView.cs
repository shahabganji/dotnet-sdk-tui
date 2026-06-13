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

    public bool NeedsLiveUpdate => _running;
    public bool IsTextInputActive => false;

    public Task ActivateAsync()
    {
        if (_projects.Count == 0)
        {
            _projects = ProjectDetector.Detect();
            _selectedProject = 0;
        }
        return Task.CompletedTask;
    }

    public IRenderable Render(bool focused)
    {
        var parts = new List<IRenderable>();

        if (_projects.Count == 0)
        {
            parts.Add(MarioTheme.Muted("No .sln, .slnx, or .csproj files detected."));
            parts.Add(MarioTheme.Info($"Dir: {Directory.GetCurrentDirectory()}"));
        }
        else
        {
            // Project selector
            if (_projects.Count > 1)
            {
                var projParts = new List<string>();
                for (int i = 0; i < _projects.Count; i++)
                {
                    string prefix = i == _selectedProject ? "►" : " ";
                    string style = i == _selectedProject ? $"{MarioTheme.Yellow} bold" : MarioTheme.White;
                    projParts.Add($"[{style}]{prefix} {Markup.Escape(_projects[i].FileName)}[/]");
                }
                parts.Add(new Markup(string.Join("  ", projParts)));
            }
            else
            {
                parts.Add(new Markup($"[{MarioTheme.Yellow} bold]Project:[/] [{MarioTheme.White}]{Markup.Escape(_projects[0].FileName)}[/]"));
            }

            // Action bar
            if (!_running)
            {
                parts.Add(new Markup($"[{MarioTheme.Blue}]r[/]:[{MarioTheme.White}]Restore[/]  " +
                                     $"[{MarioTheme.Blue}]b[/]:[{MarioTheme.White}]Build[/]  " +
                                     $"[{MarioTheme.Blue}]t[/]:[{MarioTheme.White}]Test[/]  " +
                                     $"[{MarioTheme.Blue}]n[/]:[{MarioTheme.White}]Run[/]  " +
                                     $"[{MarioTheme.Blue}]p[/]:[{MarioTheme.White}]Publish[/]"));
            }
        }

        // Running indicator
        if (_running)
        {
            parts.Add(new Markup($"[{MarioTheme.Yellow}]Running {Markup.Escape(_lastAction ?? "")}...[/]"));
        }

        // Output panel - ALWAYS render inside the section
        if (_outputLines.Count > 0)
        {
            int maxLines = 12;
            int start = Math.Max(0, _outputLines.Count - maxLines - _outputScroll);
            int end = Math.Min(_outputLines.Count, start + maxLines);

            var outputParts = new List<IRenderable>();
            for (int i = start; i < end; i++)
            {
                string line = _outputLines[i];
                string color = line.StartsWith("ERR|") ? ThemeManager.OutputError : ThemeManager.OutputText;
                string text = line.StartsWith("ERR|") ? line[4..] : line;
                outputParts.Add(new Markup($"[{color}]{Markup.Escape(text)}[/]"));
            }

            parts.Add(new Panel(new Rows(outputParts))
                .Header($"[{MarioTheme.Brown}] Output ({_outputLines.Count} lines) [/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(ThemeManager.TableBorderColor)
                .Expand());
        }

        // Result summary
        if (_lastResult is not null && !_running)
        {
            if (_lastResult.ExitCode == 0)
                parts.Add(MarioTheme.Success($"{_lastAction} completed in {_lastResult.Duration.TotalSeconds:F1}s"));
            else
                parts.Add(MarioTheme.Error($"{_lastAction} failed (exit code {_lastResult.ExitCode}) in {_lastResult.Duration.TotalSeconds:F1}s"));
        }

        string focusIndicator = focused ? $"[{MarioTheme.Green} bold]●[/] " : $"[{MarioTheme.Gray}]○[/] ";
        return new Panel(new Rows(parts))
            .Header($"{focusIndicator}[{MarioTheme.Yellow} bold]🔥 Project[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(focused ? ThemeManager.PanelBorderColor : ThemeManager.TableBorderColor)
            .Expand();
    }

    public string GetStatusHints()
    {
        if (_projects.Count == 0) return "No project detected";
        if (_running) return $"Running {_lastAction}...  (streaming)";
        if (_projects.Count > 1)
            return "←→:Select  r:Restore  b:Build  t:Test  n:Run  p:Publish  c:Clear";
        return "r:Restore  b:Build  t:Test  n:Run  p:Publish  c:Clear";
    }

    public async Task<KeyResult> HandleKeyAsync(ConsoleKeyInfo key)
    {
        if (_running)
        {
            // Allow scrolling output during execution
            if (key.Key == ConsoleKey.UpArrow)
            {
                _outputScroll = Math.Min(_outputScroll + 1, Math.Max(0, _outputLines.Count - 5));
                return KeyResult.Handled;
            }
            if (key.Key == ConsoleKey.DownArrow)
            {
                _outputScroll = Math.Max(0, _outputScroll - 1);
                return KeyResult.Handled;
            }
            return KeyResult.NotHandled;
        }

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
                await RunActionAsync("Restore", "restore");
                return KeyResult.Handled;

            case ConsoleKey.B:
                await RunActionAsync("Build", "build");
                return KeyResult.Handled;

            case ConsoleKey.T:
                await RunActionAsync("Test", "test");
                return KeyResult.Handled;

            case ConsoleKey.N:
                await RunActionAsync("Run", "run");
                return KeyResult.Handled;

            case ConsoleKey.P:
                await RunActionAsync("Publish", "publish");
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

    private async Task RunActionAsync(string actionName, string verb)
    {
        var project = _projects[_selectedProject];

        if (verb == "run" && project.ProjectType != ProjectType.CSharpProject)
        {
            _outputLines.Clear();
            _outputLines.Add("ERR|dotnet run is only supported for .csproj projects.");
            _lastResult = new ProcessResult(-1, "", "Not supported", TimeSpan.Zero);
            _lastAction = actionName;
            return;
        }

        _running = true;
        _lastAction = actionName;
        _outputLines.Clear();
        _outputScroll = 0;
        _lastResult = null;

        try
        {
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
