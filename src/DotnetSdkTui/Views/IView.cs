using Spectre.Console.Rendering;

namespace DotnetSdkTui.Views;

/// <summary>Indicates how a key press was handled by a view.</summary>
public enum KeyResult
{
    Handled,
    NotHandled,
    Quit
}

/// <summary>
/// Defines the contract for a TUI section/view that can render content,
/// handle keyboard input, and report its state.
/// </summary>
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
