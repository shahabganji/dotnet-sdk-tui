using Spectre.Console.Rendering;

namespace DotnetSdkTui.Views;

public enum KeyResult
{
    Handled,
    NotHandled,
    Quit
}

public enum ActiveSection
{
    Sdks,
    Search,
    Project,
    Setup
}

public interface IView
{
    string Name { get; }
    string Icon { get; }
    IRenderable Render(bool focused);
    string GetStatusHints();
    Task<KeyResult> HandleKeyAsync(ConsoleKeyInfo key);
    Task ActivateAsync();
    bool NeedsLiveUpdate { get; }
    bool IsTextInputActive { get; }
}
