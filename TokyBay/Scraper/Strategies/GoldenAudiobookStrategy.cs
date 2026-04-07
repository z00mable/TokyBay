using Spectre.Console;
using System.Text.RegularExpressions;
using TokyBay.Models;
using TokyBay.Scraper.Base;
using TokyBay.Scraper.Configuration;
using TokyBay.Services;

namespace TokyBay.Scraper.Strategies
{
    public partial class GoldenAudiobookStrategy(
        IAnsiConsole console,
        IHttpService httpUtil,
        ISettingsService settingsService,
        ScraperConfig? config = null) : BaseScraperStrategy(console, httpUtil, settingsService, config)
    {
        // Matches <source> tags with type="audio/mpeg" in either attribute order
        [GeneratedRegex(@"<source\b[^>]*type=""audio/mpeg""[^>]*>", RegexOptions.IgnoreCase)]
        private static partial Regex AudioSourceTagRegex();

        [GeneratedRegex(@"\bsrc=""([^""]+)""")]
        private static partial Regex SrcAttributeRegex();

        public override bool CanHandle(string bookUrl)
        {
            return bookUrl.Contains("goldenaudiobook.net", StringComparison.OrdinalIgnoreCase);
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

                var chapterUrls = AudioSourceTagRegex()
                    .Matches(html)
                    .Select(m => SrcAttributeRegex().Match(m.Value))
                    .Where(m => m.Success)
                    .Select(m => m.Groups[1].Value)
                    .ToList();

                if (chapterUrls.Count == 0)
                {
                    return null;
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
    }
}
