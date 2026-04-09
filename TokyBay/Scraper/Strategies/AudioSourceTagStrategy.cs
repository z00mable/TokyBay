using Spectre.Console;
using System.Text.RegularExpressions;
using TokyBay.Models;
using TokyBay.Scraper.Base;
using TokyBay.Scraper.Configuration;
using TokyBay.Services;

namespace TokyBay.Scraper.Strategies
{
    public partial class AudioSourceTagStrategy(
        IAnsiConsole console,
        IHttpService httpService,
        ISettingsService settingsService,
        ScraperConfig? config = null) : BaseScraperStrategy(console, httpService, settingsService, config)
    {
        // Matches <source> tags with type="audio/mpeg" in either attribute order
        [GeneratedRegex(@"<source\b[^>]*type=""audio/mpeg""[^>]*>", RegexOptions.IgnoreCase)]
        private static partial Regex AudioSourceTagRegex();

        [GeneratedRegex(@"\bsrc=""([^""]+)""")]
        private static partial Regex SrcAttributeRegex();

        // Matches <a href="...mp3"> links
        [GeneratedRegex(@"<a\b[^>]*\bhref=""([^""]+\.mp3[^""]*?)""[^>]*>", RegexOptions.IgnoreCase)]
        private static partial Regex AnchorMp3Regex();

        public override bool CanHandle(string bookUrl)
        {
            return bookUrl.Contains("appaudiobooks.com", StringComparison.OrdinalIgnoreCase)
                || bookUrl.Contains("audiozaic.com", StringComparison.OrdinalIgnoreCase)
                || bookUrl.Contains("bigaudiobooks.net", StringComparison.OrdinalIgnoreCase)
                || bookUrl.Contains("bookaudiobook.net", StringComparison.OrdinalIgnoreCase)
                || bookUrl.Contains("findaudiobook.com", StringComparison.OrdinalIgnoreCase)
                || bookUrl.Contains("fulllengthaudiobooks.net", StringComparison.OrdinalIgnoreCase)
                || bookUrl.Contains("goldenaudiobook.net", StringComparison.OrdinalIgnoreCase)
                || bookUrl.Contains("hotaudiobooks.com", StringComparison.OrdinalIgnoreCase);
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

                var chapterUrls = ExtractFromSourceTags(html);

                if (chapterUrls.Count == 0)
                    chapterUrls = ExtractFromAnchorLinks(html);

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

        private static List<string> ExtractFromSourceTags(string html)
        {
            return AudioSourceTagRegex()
                .Matches(html)
                .Select(m => SrcAttributeRegex().Match(m.Value))
                .Where(m => m.Success)
                .Select(m => m.Groups[1].Value)
                .ToList();
        }

        private static List<string> ExtractFromAnchorLinks(string html)
        {
            return AnchorMp3Regex()
                .Matches(html)
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .ToList();
        }
    }
}
