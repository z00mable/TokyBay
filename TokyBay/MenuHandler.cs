using Spectre.Console;

namespace TokyBay
{
    public static class MenuHandler
    {
        public static async Task ShowMainMenu(string? customDownloadFolder)
        {
            string[] options = { "Search book", "Download from URL", "Exit" };
            while (true)
            {
                var selection = DisplayMenu("Choose action:", options);
                switch (selection)
                {
                    case "Search book":
                        await BookSearcher.PromptSearchBook();
                        break;
                    case "Download from URL":
                        await Downloader.GetInput(customDownloadFolder);
                        break;
                    case "Exit":
                        return;
                }
            }
        }

        public static string DisplayMenu(string prompt, string[] options)
        {
            AnsiConsole.Clear();
            AnsiConsole.MarkupLine("[blue italic]Welcome to TokyBay[/]");
            AnsiConsole.WriteLine();
            return AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[grey]{prompt}[/]")
                    .PageSize(20)
                    .MoreChoicesText("[grey](Move up and down to reveal more titles)[/]")
                    .AddChoices(options)
            );
        }
    }
}
