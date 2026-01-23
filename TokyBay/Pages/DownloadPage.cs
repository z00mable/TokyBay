using Spectre.Console;
using TokyBay.Models;
using TokyBay.Services;

namespace TokyBay.Pages
{
    public class DownloadPage(
        IAnsiConsole console,
        IPageService pageService,
        ISettingsService settingsService,
        DownloadService downloadService)
    {
        private readonly IAnsiConsole _console = console;
        private readonly IPageService _pageService = pageService;
        private readonly UserSettings _settings = settingsService.GetSettings();
        private readonly DownloadService _downloadService = downloadService;

        public async Task ShowAsync()
        {
            _console.Clear();
            _pageService.DisplayHeader();

            DisplaySupportedSites();
            DisplayDownloadPath();

            while (true)
            {
                _console.WriteLine();
                var (url, cancelled) = await _pageService.DisplayAskAsync<string>("Enter URL:");

                if (cancelled)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(url))
                {
                    _console.MarkupLine("[red]URL cannot be empty! Try again.[/]");
                    continue;
                }

                await ProcessDownloadAsync(url);
                return;
            }
        }

        private void DisplaySupportedSites()
        {
            _console.MarkupLine("[grey]Supported audiobook sites:[/]");

            var supportedDomains = _downloadService.GetSupportedDomains();
            foreach (var domain in supportedDomains)
            {
                _console.MarkupLine($" - https://{domain}/");
            }

            _console.WriteLine();
        }

        private void DisplayDownloadPath()
        {
            _console.MarkupLine($"[grey]Audiobook will be saved in:[/] {_settings.DownloadPath}");
        }

        private async Task ProcessDownloadAsync(string url)
        {
            try
            {
                await _downloadService.DownloadAsync(url);
            }
            catch (NotSupportedException ex)
            {
                _console.MarkupLine($"[red]{ex.Message}[/]");
                _console.WriteLine();
                _console.MarkupLine("[yellow]Please use one of the supported sites listed above.[/]");
                _console.MarkupLine("Press any key to continue");
                Console.ReadKey(true);
            }
            catch (Exception ex)
            {
                _console.MarkupLine($"[red]Download failed: {ex.Message}[/]");
                _console.MarkupLine("Press any key to continue");
                Console.ReadKey(true);
            }
        }
    }
}