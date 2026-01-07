using Spectre.Console;

namespace TokyBay
{
    public static class MenuHandler
    {
        public static async Task ShowMainMenu()
        {
            const string search = "Search book on Tokybook.com";
            const string download = "Download from URL";
            const string settings = "Settings";
            const string exit = "Exit";

            string[] options = { search, download, settings, exit };

            while (true)
            {
                var selection = DisplayMenu("Choose action:", options);
                switch (selection)
                {
                    case search:
                        await BookSearcher.PromptSearchBook();
                        break;
                    case download:
                        await GetUrlInput();
                        break;
                    case settings:
                        await SettingsMenu.GetSettings();
                        break;
                    case exit:
                        return;
                }
            }
        }

        public static async Task GetUrlInput()
        {
            AnsiConsole.Clear();
            Constants.ShowHeader();
            AnsiConsole.MarkupLine($"[grey]Supported audiobook sites:[/]");
            AnsiConsole.MarkupLine($" - https://tokybook.com/");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[grey]Audiobook will be saved in:[/] {SettingsMenu.UserSettings.DownloadPath}");
            while (true)
            {
                AnsiConsole.WriteLine();
                var url = AnsiConsole.Ask<string>("Enter URL:");
                switch (url)
                {
                    case { } when url.StartsWith("https://tokybook.com/"):
                        await Scraper.Tokybook.GetChapters(url);
                        break;
                    default:
                        AnsiConsole.MarkupLine("[red]Invalid URL! Try again.[/]");
                        continue;
                }
            }
        }

        public static string DisplayMenu(string prompt, string[] options)
        {
            AnsiConsole.Clear();
            Constants.ShowHeader();
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
