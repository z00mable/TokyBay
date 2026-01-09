using Spectre.Console;

namespace TokyBay.Pages
{
    public class DownloadPage
    {
        public static async Task ShowAsync()
        {
            Program.CustomAnsiConsole.Clear();
            MenuHandler.ShowHeader();
            Program.CustomAnsiConsole.MarkupLine($"[grey]Supported audiobook sites:[/]");
            Program.CustomAnsiConsole.MarkupLine($" - https://tokybook.com/");
            Program.CustomAnsiConsole.WriteLine();
            Program.CustomAnsiConsole.MarkupLine($"[grey]Audiobook will be saved in:[/] {SettingsPage.UserSettings.DownloadPath}");
            while (true)
            {
                Program.CustomAnsiConsole.WriteLine();
                var (url, cancelled) = await MenuHandler.DisplayAskAsync<string>("Enter URL:");
                if (cancelled)
                {
                    return;
                }

                switch (url)
                {
                    case { } when url.StartsWith("https://tokybook.com/"):
                        await Scraper.Tokybook.DownloadBookAsync(url);
                        break;
                    default:
                        Program.CustomAnsiConsole.MarkupLine("[red]Invalid URL! Try again.[/]");
                        continue;
                }
            }
        }
    }
}
