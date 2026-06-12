using Spectre.Console.Rendering;

namespace DotnetSdkTui.Views;

public enum KeyResult
{
    Handled,
    NotHandled,
    Quit
}

public interface IView
{
    string Name { get; }
    string Icon { get; }
    IRenderable Render();
    string GetStatusHints();
    Task<KeyResult> HandleKeyAsync(ConsoleKeyInfo key);
    Task ActivateAsync();
}
