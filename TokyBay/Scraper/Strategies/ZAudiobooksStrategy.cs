using Spectre.Console;
using TokyBay.Models;
using TokyBay.Scraper.Base;
using TokyBay.Scraper.Configuration;
using TokyBay.Services;

namespace TokyBay.Scraper.Strategies
{
    public class ZAudiobooksStrategy(
        IAnsiConsole console,
        IHttpService httpUtil,
        ISettingsService settingsService,
        ScraperConfig? config = null) : BaseScraperStrategy(console, httpUtil, settingsService, config)
    {
        private const string BaseUrl = "https://files01.freeaudiobooks.top/audio/";

        public override bool CanHandle(string bookUrl)
        {
            return bookUrl.Contains("freeaudiobooks", StringComparison.OrdinalIgnoreCase) ||
                   bookUrl.Contains("zaudiobooks", StringComparison.OrdinalIgnoreCase);
        }

        public override async Task DownloadBookAsync(string bookUrl)
        {
            _console.MarkupLine("[yellow]Warning: freeaudiobooks.top and zaudiobooks.com are currently experiencing downtime and may be unreachable.[/]");
            _console.WriteLine();

            var metadata = await FetchMetadataAsync(bookUrl);
            if (metadata == null || metadata.ChapterUrls.Count == 0)
            {
                ShowErrorMessage("No valid tracks found");
                return;
            }

            SetFFmpegPath();

            var folderPath = PrepareOutputFolder(metadata.Title);

            _console.MarkupLine($"[green]Found {metadata.ChapterUrls.Count} tracks[/]");
            _console.MarkupLine($"[blue]Parallel downloads:[/] {_config.MaxParallelDownloads}");
            _console.MarkupLine($"[blue]Parallel conversions:[/] {_config.MaxParallelConversions}");

            await ProcessDirectFilesInParallelAsync(metadata, folderPath);

            ShowCompletionMessage(folderPath);
        }

        private async Task<SimpleAudiobookMetadata?> FetchMetadataAsync(string bookUrl)
        {
            SimpleAudiobookMetadata? metadata = null;

            await _console.Status()
                .SpinnerStyle(Style.Parse("blue bold"))
                .StartAsync("Preparing download...", async ctx =>
                {
                    ctx.Status("Getting title and chapters...");
                    metadata = await GetChapterUrlsAsync(bookUrl);
                });

            return metadata;
        }

        private async Task<SimpleAudiobookMetadata?> GetChapterUrlsAsync(string bookUrl)
        {
            try
            {
                var response = await _httpUtil.GetAsync(bookUrl);
                response.EnsureSuccessStatusCode();

                var html = await response.Content.ReadAsStringAsync();
                var lines = html.Split('\n');
                var startIndex = Array.FindIndex(lines, line => line.Contains("tracks = ["));

                if (startIndex == -1)
                {
                    return null;
                }

                var chapterUrls = new List<string>();
                bool skipThisTrack = false;

                for (int i = startIndex; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();

                    if (line.Contains("\"name\"") && line.Contains("welcome"))
                    {
                        skipThisTrack = true;
                    }

                    if (line.Contains("\"chapter_link_dropbox\""))
                    {
                        if (skipThisTrack)
                        {
                            skipThisTrack = false;
                            continue;
                        }

                        var url = ExtractUrl(line);
                        if (!string.IsNullOrEmpty(url))
                        {
                            var fullUrl = url.StartsWith("http") ? url : BaseUrl + url;
                            chapterUrls.Add(fullUrl);
                        }
                    }

                    if (line.Contains("],"))
                    {
                        break;
                    }
                }

                return new SimpleAudiobookMetadata
                {
                    Title = ExtractTitleFromH1(html),
                    ChapterUrls = chapterUrls
                };
            }
            catch (Exception ex)
            {
                _console.MarkupLine($"[red]Error fetching chapter URLs: {ex.Message}[/]");
                return null;
            }
        }

        private static string ExtractUrl(string line)
        {
            var startQuote = line.IndexOf(':') + 1;
            return line[startQuote..]
                .Trim()
                .Trim('"', ',', ' ')
                .Replace("\\", "");
        }
    }
}
