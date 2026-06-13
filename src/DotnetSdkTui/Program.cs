namespace DotnetSdkTui;

/// <summary>
/// Entry point for the .NET SDK Manager TUI application.
/// </summary>
public static class Program
{
    /// <summary>
    /// Parses command-line arguments and starts the TUI application.
    /// Supports <c>--version</c>/<c>-v</c> and <c>--no-splash</c> flags.
    /// </summary>
    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0] is "--version" or "-v")
        {
            Console.WriteLine("dotnet-sdk-tui 0.1.0");
            return 0;
        }

        if (args.Length > 0 && args[0] is "--no-splash")
        {
            Console.CancelKeyPress += (_, e) => e.Cancel = false;
            var app = new App(skipSplash: true);
            await app.RunAsync();
            return 0;
        }

        Console.CancelKeyPress += (_, e) => e.Cancel = false;
        var application = new App();
        await application.RunAsync();
        return 0;
    }
}
