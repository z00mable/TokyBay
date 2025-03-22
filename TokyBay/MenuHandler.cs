using Spectre.Console;

namespace TokyBay
{
    public static class MenuHandler
    {
        public static async Task ShowMainMenu()
        {
            string[] options = { "Search book", "Download from URL", "Settings", "Exit" };
            while (true)
            {
                var selection = DisplayMenu("Choose action:", options);
                switch (selection)
                {
                    case "Search book":
                        await BookSearcher.PromptSearchBook();
                        break;
                    case "Download from URL":
                        await Downloader.GetInput();
                        break;
                    case "Settings":
                        await SettingsMenu.GetSettings();
                        break;
                    case "Exit":
                        return;
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
