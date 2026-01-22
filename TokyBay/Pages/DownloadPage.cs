using Spectre.Console;
using TokyBay.Models;
using TokyBay.Services;

namespace TokyBay.Pages
{
    public class DownloadPage(
        IAnsiConsole console,
        IPageService pageService,
        ISettingsService settingsService,
        Scraper.Tokybook tokybookScraper,
        Scraper.ZAudiobooks zaudiobooksScraper)
    {
        private readonly IAnsiConsole _console = console;
        private readonly IPageService _pageService = pageService;
        private readonly UserSettings _settings = settingsService.GetSettings();
        private readonly Scraper.Tokybook _tokybookScraper = tokybookScraper;
        private readonly Scraper.ZAudiobooks _zaudiobooksScraper = zaudiobooksScraper;

        public async Task ShowAsync()
        {
            _console.Clear();
            _pageService.DisplayHeader();

            _console.MarkupLine($"[grey]Supported audiobook sites:[/]");
            _console.MarkupLine($" - https://tokybook.com/");
            _console.MarkupLine($" - https://zaudiobooks.com/");
            _console.WriteLine();
            _console.MarkupLine($"[grey]Audiobook will be saved in:[/] {_settings.DownloadPath}");

            while (true)
            {
                _console.WriteLine();
                var (url, cancelled) = await _pageService.DisplayAskAsync<string>("Enter URL:");
                if (cancelled)
                {
                    return;
                }

                switch (url)
                {
                    case { } when url.Contains("tokybook.com"):
                        await _tokybookScraper.DownloadBookAsync(url);
                        return;
                    case { } when url.Contains("zaudiobooks.com"):
                        await _zaudiobooksScraper.DownloadBookAsync(url);
                        return;
                    default:
                        _console.MarkupLine("[red]Invalid URL! Try again.[/]");
                        continue;
                }
            }
        }
    }
}