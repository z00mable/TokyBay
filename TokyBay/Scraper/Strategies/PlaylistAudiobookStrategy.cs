using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.Text.RegularExpressions;
using TokyBay.Models;
using TokyBay.Scraper.Base;
using TokyBay.Scraper.Configuration;
using TokyBay.Services;

namespace TokyBay.Scraper.Strategies
{
    public partial class PlaylistAudiobookStrategy(
        IAnsiConsole console,
        IHttpService httpService,
        ISettingsService settingsService,
        ScraperConfig? config = null) : BaseScraperStrategy(console, httpService, settingsService, config)
    {
        [GeneratedRegex(@"data-playlist='([^']+)'", RegexOptions.Singleline)]
        private static partial Regex DataPlaylistRegex();

        public override bool CanHandle(string bookUrl)
        {
            return bookUrl.Contains("hdaudiobooks.net", StringComparison.OrdinalIgnoreCase);
        }

        public override async Task DownloadBookAsync(string bookUrl)
        {
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
                    ctx.Status("Fetching page...");
                    metadata = await GetChapterUrlsAsync(bookUrl, ctx);
                });

            return metadata;
        }

        private async Task<SimpleAudiobookMetadata?> GetChapterUrlsAsync(string bookUrl, StatusContext ctx)
        {
            try
            {
                var response = await _httpService.GetAsync(bookUrl);
                response.EnsureSuccessStatusCode();

                var html = await response.Content.ReadAsStringAsync();

                var match = DataPlaylistRegex().Match(html);
                if (!match.Success)
                    return null;

                var json = System.Net.WebUtility.HtmlDecode(match.Groups[1].Value);
                var playlist = JArray.Parse(json);

                var chapterUrls = playlist
                    .Select(t => t["src"]?.ToString())
                    .Where(src => !string.IsNullOrEmpty(src))
                    .Select(src => src!)
                    .ToList();

                if (chapterUrls.Count == 0)
                    return null;

                var result = new SimpleAudiobookMetadata
                {
                    Title = CleanupBookTitle(ExtractTitleFromH1(html)),
                    ChapterUrls = chapterUrls
                };
                await EnrichFromFirstTrackTagsAsync(result, ctx);
                ExtractCommonMetadata(html, result);
                return result;
            }
            catch (Exception ex)
            {
                _console.MarkupLine($"[red]Error fetching chapter URLs: {ex.Message}[/]");
                return null;
            }
        }
    }
}
