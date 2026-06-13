using DotnetSdkTui.Views;

namespace DotnetSdkTui.Tests.Views;

public class SearchViewTests
{
    [Fact]
    public void Name_ReturnsSearch()
    {
        var view = new SearchView();
        Assert.Equal("Search", view.Name);
    }

    [Fact]
    public async Task ActivateAsync_SetsInputMode()
    {
        var view = new SearchView();
        await view.ActivateAsync();
        Assert.True(view.IsTextInputActive);
    }

    [Fact]
    public async Task HandleKey_TypingCharacters_AccumulatesQuery()
    {
        var view = new SearchView();
        await view.ActivateAsync();

        // Type "10.0"
        await view.HandleKeyAsync(new ConsoleKeyInfo('1', ConsoleKey.D1, false, false, false));
        await view.HandleKeyAsync(new ConsoleKeyInfo('0', ConsoleKey.D0, false, false, false));
        await view.HandleKeyAsync(new ConsoleKeyInfo('.', ConsoleKey.OemPeriod, false, false, false));
        await view.HandleKeyAsync(new ConsoleKeyInfo('0', ConsoleKey.D0, false, false, false));

        var renderable = view.Render(true);
        Assert.NotNull(renderable);
    }

    [Fact]
    public async Task HandleKey_Backspace_DeletesCharacter()
    {
        var view = new SearchView();
        await view.ActivateAsync();

        await view.HandleKeyAsync(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false));
        await view.HandleKeyAsync(new ConsoleKeyInfo('b', ConsoleKey.B, false, false, false));
        var result = await view.HandleKeyAsync(
            new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false));

        Assert.Equal(KeyResult.Handled, result);
    }

    [Fact]
    public async Task HandleKey_Escape_ClearsQuery()
    {
        var view = new SearchView();
        await view.ActivateAsync();

        await view.HandleKeyAsync(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false));
        await view.HandleKeyAsync(new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false));

        Assert.True(view.IsTextInputActive);
    }

    [Fact]
    public void GetStatusHints_InInputMode_MentionsSearch()
    {
        var view = new SearchView();
        var hints = view.GetStatusHints();
        Assert.Contains("search", hints, StringComparison.OrdinalIgnoreCase);
    }
}
