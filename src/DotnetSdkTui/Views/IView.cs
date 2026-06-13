using Spectre.Console.Rendering;

namespace DotnetSdkTui.Views;

/// <summary>Indicates how a key press was handled by a view.</summary>
public enum KeyResult
{
    /// <summary>The key was handled and consumed.</summary>
    Handled,

    /// <summary>The key was not handled by this view.</summary>
    NotHandled,

    /// <summary>The key triggered an application quit.</summary>
    Quit
}

/// <summary>Identifies which section is currently focused.</summary>
public enum ActiveSection
{
    /// <summary>SDKs section.</summary>
    Sdks,

    /// <summary>Search section.</summary>
    Search,

    /// <summary>Project section.</summary>
    Project,

    /// <summary>Setup section.</summary>
    Setup
}

/// <summary>
/// Defines the contract for a TUI section/view that can render content,
/// handle keyboard input, and report its state.
/// </summary>
public interface IView
{
    /// <summary>Gets the display name of this view.</summary>
    string Name { get; }

    /// <summary>Gets the icon character for this view.</summary>
    string Icon { get; }

    /// <summary>Renders the view content, optionally highlighting when focused.</summary>
    /// <param name="focused">Whether this view currently has keyboard focus.</param>
    IRenderable Render(bool focused);

    /// <summary>Returns hint text describing available keyboard shortcuts for this view.</summary>
    string GetStatusHints();

    /// <summary>Handles a keyboard event and returns how it was processed.</summary>
    Task<KeyResult> HandleKeyAsync(ConsoleKeyInfo key);

    /// <summary>Called when this view receives focus or needs to initialize its data.</summary>
    Task ActivateAsync();

    /// <summary>Whether this view needs the render loop to poll for updates (e.g. during async operations).</summary>
    bool NeedsLiveUpdate { get; }

    /// <summary>Whether this view is currently capturing text input (prevents global key shortcuts).</summary>
    bool IsTextInputActive { get; }
}
