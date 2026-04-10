using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using TokyBay.Models;
using TokyBay.Scraper.Abstractions;
using TokyBay.Scraper.Configuration;
using TokyBay.Services;
using Xabe.FFmpeg;

namespace TokyBay.Scraper.Base
{
    public abstract partial class BaseScraperStrategy(
        IAnsiConsole console,
        IHttpService httpService,
        ISettingsService settingsService,
        ScraperConfig? config = null) : IScraperStrategy
    {
        protected readonly IAnsiConsole _console = console;
        protected readonly IHttpService _httpService = httpService;
        protected readonly UserSettings _settings = settingsService.GetSettings();
        protected readonly ScraperConfig _config = config ?? new ScraperConfig();

        public abstract Task DownloadBookAsync(string bookUrl);
        public abstract bool CanHandle(string bookUrl);

        [GeneratedRegex("[^A-Za-z0-9]+")]
        private static partial Regex SanitizeRegex();

        [GeneratedRegex(@"<h1[^>]*>(.*?)</h1>", RegexOptions.Singleline)]
        private static partial Regex H1Regex();

        [GeneratedRegex(@"<meta\s+[^>]*property=""og:image""[^>]*>", RegexOptions.IgnoreCase)]
        private static partial Regex OgImageTagRegex();

        [GeneratedRegex(@"<meta\s+[^>]*property=""og:description""[^>]*>", RegexOptions.IgnoreCase)]
        private static partial Regex OgDescriptionTagRegex();

        [GeneratedRegex(@"\bcontent=""([^""]+)""")]
        private static partial Regex ContentAttributeRegex();

        [GeneratedRegex(@"<script\s+type=""application/ld\+json""[^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
        private static partial Regex LdJsonRegex();

        [GeneratedRegex(@"<link\b[^>]*\bas=""image""[^>]*/?>", RegexOptions.IgnoreCase)]
        private static partial Regex PreloadImageTagRegex();

        [GeneratedRegex(@"\bhref=""([^""]+)""")]
        private static partial Regex HrefAttributeRegex();

        [GeneratedRegex(@"<[^>]+>")]
        private static partial Regex HtmlTagsRegex();

        [GeneratedRegex(@"\s*\[(Listen|Download)\]", RegexOptions.IgnoreCase)]
        private static partial Regex BracketNavLabelRegex();

        private static readonly string[] _headlineSuffixes =
            ["Audiobook", "Audio Book", "Free", "Online", "Streaming", "Download", "(AUDIOBOOK)"];

        protected static string SanitizeName(string fileName)
        {
            return SanitizeRegex().Replace(fileName, "_");
        }

        protected static string ExtractTitleFromH1(string html)
        {
            var match = H1Regex().Match(html);
            return match.Success
                ? WebUtility.HtmlDecode(match.Groups[1].Value.Trim())
                : "Unknown Title";
        }

        protected static string CleanupBookTitle(string headline)
        {
            if (string.IsNullOrEmpty(headline)) return headline;

            // Strip bracket navigation labels added by some sites (e.g. audiozaic.com: "[Listen][Download]")
            headline = BracketNavLabelRegex().Replace(headline, "").Trim();

            // Pattern: "Title [Audiobook] by Author" → title is everything before " by "
            var byIndex = headline.IndexOf(" by ", StringComparison.OrdinalIgnoreCase);
            if (byIndex > 0)
                return StripHeadlineSuffixes(headline[..byIndex].Trim());

            // Pattern: "Author – Title [Audiobook]" or "Title [Audiobook] – Author"
            string[] separators = [" \u2013 ", " \u2014 ", " - "];
            foreach (var sep in separators)
            {
                var idx = headline.IndexOf(sep, StringComparison.Ordinal);
                if (idx <= 0) continue;

                var before = headline[..idx].Trim();
                var after = headline[(idx + sep.Length)..].Trim();

                // Whichever segment contains a known suffix is the title segment
                if (_headlineSuffixes.Any(s => after.Contains(s, StringComparison.OrdinalIgnoreCase)))
                    return StripHeadlineSuffixes(after);

                if (_headlineSuffixes.Any(s => before.Contains(s, StringComparison.OrdinalIgnoreCase)))
                    return StripHeadlineSuffixes(before);

                // No suffix found — common pattern is "Author – Title", so take the after segment
                return after;
            }

            // No separator → just strip suffixes from the whole title
            return StripHeadlineSuffixes(headline);
        }

        protected static void ExtractCommonMetadata(string html, AudiobookMetadata metadata)
        {
            // og:image → CoverArtUrl
            if (string.IsNullOrEmpty(metadata.CoverArtUrl))
            {
                var ogImageTag = OgImageTagRegex().Match(html);
                if (ogImageTag.Success)
                {
                    var contentMatch = ContentAttributeRegex().Match(ogImageTag.Value);
                    if (contentMatch.Success)
                        metadata.CoverArtUrl = WebUtility.HtmlDecode(contentMatch.Groups[1].Value);
                }
            }

            // og:description → Description
            if (string.IsNullOrEmpty(metadata.Description))
            {
                var ogDescTag = OgDescriptionTagRegex().Match(html);
                if (ogDescTag.Success)
                {
                    var contentMatch = ContentAttributeRegex().Match(ogDescTag.Value);
                    if (contentMatch.Success)
                        metadata.Description = WebUtility.HtmlDecode(contentMatch.Groups[1].Value);
                }
            }

            // ld+json blocks: look for @type "Audiobook" first, then "Article"/"WebPage" for author via headline
            var ldJsonMatches = LdJsonRegex().Matches(html);
            string headlineForAuthor = string.Empty;

            foreach (Match ldMatch in ldJsonMatches)
            {
                try
                {
                    var json = JObject.Parse(ldMatch.Groups[1].Value);
                    var type = json["@type"]?.ToString();

                    if (type == "Audiobook")
                    {
                        if (string.IsNullOrEmpty(metadata.Author))
                            metadata.Author = json["author"]?["name"]?.ToString() ?? string.Empty;
                        if (string.IsNullOrEmpty(metadata.Narrator))
                            metadata.Narrator = json["readBy"]?["name"]?.ToString() ?? string.Empty;
                        if (string.IsNullOrEmpty(metadata.Description))
                            metadata.Description = json["description"]?.ToString() ?? string.Empty;
                        if (string.IsNullOrEmpty(metadata.CoverArtUrl))
                            metadata.CoverArtUrl = json["image"]?.ToString() ?? string.Empty;
                        break;
                    }

                    if (string.IsNullOrEmpty(headlineForAuthor) && (type == "Article" || type == "WebPage"))
                        headlineForAuthor = json["headline"]?.ToString() ?? json["name"]?.ToString() ?? string.Empty;
                }
                catch { }
            }

            // Fallback: cover art from <link rel="preload" as="image" href="...">
            if (string.IsNullOrEmpty(metadata.CoverArtUrl))
            {
                var preloadTag = PreloadImageTagRegex().Match(html);
                if (preloadTag.Success)
                {
                    var hrefMatch = HrefAttributeRegex().Match(preloadTag.Value);
                    if (hrefMatch.Success)
                    {
                        var href = hrefMatch.Groups[1].Value;
                        if (href.Contains(".jpg", StringComparison.OrdinalIgnoreCase)
                            || href.Contains(".jpeg", StringComparison.OrdinalIgnoreCase)
                            || href.Contains(".png", StringComparison.OrdinalIgnoreCase)
                            || href.Contains(".webp", StringComparison.OrdinalIgnoreCase))
                            metadata.CoverArtUrl = href;
                    }
                }
            }

            // Fallback: extract author from ld+json headline, then from H1 title
            if (string.IsNullOrEmpty(metadata.Author))
            {
                var source = !string.IsNullOrEmpty(headlineForAuthor) ? headlineForAuthor : metadata.Title;
                metadata.Author = ExtractAuthorFromHeadline(source);
            }
        }

        protected static string ExtractAuthorFromHeadline(string headline)
        {
            if (string.IsNullOrEmpty(headline)) return string.Empty;

            // Pattern: "Title [Audiobook] by Author [suffix]"
            var byIndex = headline.IndexOf(" by ", StringComparison.OrdinalIgnoreCase);
            if (byIndex > 0)
            {
                var after = StripHeadlineSuffixes(headline[(byIndex + 4)..].Trim());
                if (!string.IsNullOrEmpty(after))
                    return after;
            }

            // Pattern: "Author – Title" or "Author - Title" (en dash, em dash, hyphen)
            string[] separators = [" \u2013 ", " \u2014 ", " - "];
            foreach (var sep in separators)
            {
                var idx = headline.IndexOf(sep, StringComparison.Ordinal);
                if (idx <= 0) continue;

                var before = headline[..idx].Trim();
                var after = headline[(idx + sep.Length)..].Trim();

                // If the first segment contains "Audiobook" the author is in the second segment
                if (before.Contains("Audiobook", StringComparison.OrdinalIgnoreCase) || before.Contains('['))
                    return StripHeadlineSuffixes(after);

                return StripHeadlineSuffixes(before);
            }

            return string.Empty;
        }

        protected async Task EnrichFromFirstTrackTagsAsync(SimpleAudiobookMetadata metadata, StatusContext? ctx = null)
        {
            if (metadata.ChapterUrls.Count == 0 || string.IsNullOrEmpty(_settings.FFmpegDirectory))
                return;

            try
            {
                var ffprobeName = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
                var ffprobePath = Path.Combine(_settings.FFmpegDirectory, ffprobeName);
                if (!File.Exists(ffprobePath)) return;

                ctx?.Status("Reading original MP3 tags...");

                // -probesize and -analyzeduration 0 limit ffprobe to reading only the file header
                // (ID3v2 tags are at the start of MP3 files, so 64 KB is more than enough).
                // -show_entries format_tags avoids fetching duration/bitrate which can require
                // reading much further into the file or even to the end.
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ffprobePath,
                        Arguments = $"-v quiet -probesize 65536 -analyzeduration 0 -print_format json -show_entries format_tags \"{metadata.ChapterUrls[0]}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (string.IsNullOrWhiteSpace(output)) return;

                var json = JObject.Parse(output);
                var tags = json["format"]?["tags"] as JObject;
                if (tags == null) return;

                if (string.IsNullOrEmpty(metadata.Author))
                {
                    var artist = tags["artist"]?.ToString();
                    if (!string.IsNullOrEmpty(artist))
                        metadata.Author = artist;
                }

                if (string.IsNullOrEmpty(metadata.Year))
                {
                    var date = tags["date"]?.ToString();
                    if (!string.IsNullOrEmpty(date) && date.Length >= 4 && date[..4].All(char.IsDigit))
                        metadata.Year = date[..4];
                }

                if (string.IsNullOrEmpty(metadata.Description))
                {
                    var comment = tags["comment"]?.ToString();
                    if (!string.IsNullOrEmpty(comment) && !IsChapterReference(comment))
                        metadata.Description = comment;
                }
            }
            catch { }
        }

        private static bool IsChapterReference(string comment)
        {
            const string prefix = "Chapter ";
            return comment.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && comment.Length < 20
                && comment[prefix.Length..].All(char.IsDigit);
        }

        private static string StripHeadlineSuffixes(string text)
        {
            var result = text.Trim();
            bool stripped;
            do
            {
                stripped = false;
                foreach (var suffix in _headlineSuffixes)
                {
                    if (result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        result = result[..^suffix.Length].Trim().TrimEnd('-', '\u2013', ':', ',').Trim();
                        stripped = true;
                        break;
                    }
                }
            } while (stripped && !string.IsNullOrEmpty(result));
            return result;
        }

        protected static string BuildMetadataParams(AudiobookMetadata? bookMetadata, TrackData? trackData, bool hasCoverArt)
        {
            if (bookMetadata == null) return string.Empty;

            var parts = new List<string>();

            var trackTitle = trackData?.TrackTitle;
            if (!string.IsNullOrEmpty(trackTitle))
            {
                var ext = Path.GetExtension(trackTitle);
                if (!string.IsNullOrEmpty(ext))
                {
                    var nameWithoutExt = Path.GetFileNameWithoutExtension(trackTitle);
                    trackTitle = int.TryParse(nameWithoutExt, out _)
                        ? $"Chapter {trackData!.TrackNumber}"
                        : nameWithoutExt;
                }
                parts.Add($"-metadata title=\"{EscapeMetadata(trackTitle)}\"");
            }

            if (!string.IsNullOrEmpty(bookMetadata.Title))
                parts.Add($"-metadata album=\"{EscapeMetadata(bookMetadata.Title)}\"");

            if (!string.IsNullOrEmpty(bookMetadata.Author))
                parts.Add($"-metadata artist=\"{EscapeMetadata(bookMetadata.Author)}\"");

            if (!string.IsNullOrEmpty(bookMetadata.Narrator))
                parts.Add($"-metadata album_artist=\"{EscapeMetadata(bookMetadata.Narrator)}\"");

            if (trackData != null && trackData.TotalTracks > 0)
                parts.Add($"-metadata track=\"{trackData.TrackNumber}/{trackData.TotalTracks}\"");

            parts.Add("-metadata genre=\"Audiobook\"");

            if (!string.IsNullOrEmpty(bookMetadata.Description))
            {
                var desc = StripHtml(bookMetadata.Description);
                if (desc.Length > 500) desc = desc[..500];
                parts.Add($"-metadata comment=\"{EscapeMetadata(desc)}\"");
            }

            if (!string.IsNullOrEmpty(bookMetadata.Publisher))
                parts.Add($"-metadata publisher=\"{EscapeMetadata(bookMetadata.Publisher)}\"");

            if (!string.IsNullOrEmpty(bookMetadata.Year))
                parts.Add($"-metadata date=\"{EscapeMetadata(bookMetadata.Year)}\"");

            if (hasCoverArt)
                parts.Add("-c:v copy -metadata:s:v comment=\"Cover (front)\" -disposition:v attached_pic");

            return string.Join(" ", parts);
        }

        private static string EscapeMetadata(string value) =>
            value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "");

        protected static string StripHtml(string html) =>
            WebUtility.HtmlDecode(HtmlTagsRegex().Replace(html, " ")).Replace("  ", " ").Trim();

        protected async Task<string?> DownloadCoverArtAsync(string coverArtUrl, string folder)
        {
            try
            {
                var uriPath = new Uri(coverArtUrl).AbsolutePath.Split('?')[0];
                var ext = Path.GetExtension(uriPath);
                if (string.IsNullOrEmpty(ext)) ext = ".jpg";
                var coverPath = Path.Combine(folder, $"_cover{ext}");

                var response = await _httpService.GetAsync(coverArtUrl);
                if (!response.IsSuccessStatusCode) return null;

                await using var stream = await response.Content.ReadAsStreamAsync();
                await using var file = File.Create(coverPath);
                await stream.CopyToAsync(file);
                return coverPath;
            }
            catch
            {
                return null;
            }
        }

        protected void SetFFmpegPath() => FFmpeg.SetExecutablesPath(_settings.FFmpegDirectory);

        protected async Task ProcessDirectFilesInParallelAsync(SimpleAudiobookMetadata metadata, string folderPath)
        {
            string? coverArtPath = null;
            if (!string.IsNullOrEmpty(metadata.CoverArtUrl))
                coverArtPath = await DownloadCoverArtAsync(metadata.CoverArtUrl, folderPath);

            var expectedM4bTracks = metadata.ChapterUrls
                .Select((url, i) =>
                {
                    var trackTitle = Path.GetFileName(new Uri(url.Split('?')[0]).LocalPath);
                    return (
                        TrackNumber: i + 1,
                        FilePath: Path.ChangeExtension(Path.Combine(folderPath, trackTitle), ".m4b"),
                        Title: GetChapterDisplayTitle(trackTitle, i + 1)
                    );
                })
                .ToList();

            try
            {
                var conversionChannel = Channel.CreateBounded<DirectFileTrackData>(new BoundedChannelOptions(10)
                {
                    FullMode = BoundedChannelFullMode.Wait
                });

                var downloadSemaphore = new SemaphoreSlim(_config.MaxParallelDownloads);
                var completedDownloads = 0;
                var completedConversions = 0;
                var totalTracks = metadata.ChapterUrls.Count;
                var lockObj = new object();

                await _console.Progress()
                    .AutoRefresh(true)
                    .HideCompleted(false)
                    .Columns(
                        new TaskDescriptionColumn(),
                        new ProgressBarColumn(),
                        new PercentageColumn(),
                        new DownloadedColumn(),
                        new SpinnerColumn())
                    .StartAsync(async ctx =>
                    {
                        var downloadTasks = metadata.ChapterUrls.Select((chapterUrl, index) => Task.Run(async () =>
                        {
                            var trackTitle = Path.GetFileName(new Uri(chapterUrl.Split('?')[0]).LocalPath);

                            await downloadSemaphore.WaitAsync();
                            var progressTask = ctx.AddTask($"[green]DL {index + 1}/{totalTracks}[/] {Markup.Escape(trackTitle)}");
                            DirectFileTrackData? trackData = null;
                            try
                            {
                                var filePath = await DownloadDirectFileWithProgressAsync(chapterUrl, trackTitle, folderPath, progressTask);

                                if (!string.IsNullOrEmpty(filePath))
                                {
                                    trackData = new DirectFileTrackData
                                    {
                                        FilePath = filePath,
                                        FolderPath = folderPath,
                                        TrackTitle = trackTitle,
                                        SanitizedTitle = SanitizeName(trackTitle),
                                        TrackNumber = index + 1,
                                        TotalTracks = totalTracks
                                    };
                                    lock (lockObj)
                                    {
                                        completedDownloads++;
                                        progressTask.Description = $"[green]Done {completedDownloads}/{totalTracks}[/] {Markup.Escape(trackTitle)}";
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                lock (lockObj)
                                {
                                    progressTask.Description = $"[red]Failed[/] {Markup.Escape(trackTitle)}";
                                    _console.MarkupLine($"[red]Download error for {Markup.Escape(trackTitle)}: {Markup.Escape(ex.Message)}[/]");
                                }
                            }
                            finally
                            {
                                downloadSemaphore.Release();
                            }

                            if (trackData != null)
                                await conversionChannel.Writer.WriteAsync(trackData);
                        })).ToList();

                        var conversionTasks = Enumerable.Range(0, _config.MaxParallelConversions)
                            .Select(_ => Task.Run(async () =>
                            {
                                await foreach (var track in conversionChannel.Reader.ReadAllAsync())
                                {
                                    var convTask = ctx.AddTask($"[cyan]Converting[/] {Markup.Escape(track.TrackTitle)}");
                                    convTask.IsIndeterminate = true;

                                    try
                                    {
                                        await ConvertDirectFileTrackAsync(track, metadata, coverArtPath);

                                        lock (lockObj)
                                        {
                                            completedConversions++;
                                            convTask.IsIndeterminate = false;
                                            convTask.Value = 100;
                                            convTask.Description = $"[cyan]Converted {completedConversions}/{totalTracks}[/] {Markup.Escape(track.TrackTitle)}";
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        lock (lockObj)
                                        {
                                            convTask.IsIndeterminate = false;
                                            convTask.Value = 100;
                                            convTask.Description = $"[red]Conv. failed[/] {Markup.Escape(track.TrackTitle)}";
                                            _console.MarkupLine($"[red]Conversion error for {Markup.Escape(track.TrackTitle)}: {Markup.Escape(ex.Message)}[/]");
                                        }
                                    }
                                }
                            })).ToList();

                        await Task.WhenAll(downloadTasks);
                        conversionChannel.Writer.Complete();
                        await Task.WhenAll(conversionTasks);
                    });

                if (_settings.ConvertToM4b && metadata.ChapterUrls.Count > 1)
                {
                    var availableTracks = expectedM4bTracks
                        .Where(t => File.Exists(t.FilePath))
                        .ToList();

                    if (availableTracks.Count > 1)
                    {
                        _console.MarkupLine("[blue]Combining chapters to single M4B with chapter markers...[/]");
                        await CombineTracksToSingleM4bAsync(availableTracks, folderPath, metadata.Title, metadata, coverArtPath);
                        foreach (var track in availableTracks)
                            File.Delete(track.FilePath);
                    }
                }
            }
            finally
            {
                if (coverArtPath != null && File.Exists(coverArtPath))
                    File.Delete(coverArtPath);
            }
        }

        private async Task<string> DownloadDirectFileWithProgressAsync(string trackSrc, string trackTitle, string folderPath, ProgressTask progressTask)
        {
            var filePath = Path.Combine(folderPath, trackTitle);
            try
            {
                var response = await _httpService.GetAsync(trackSrc);
                if (!response.IsSuccessStatusCode)
                {
                    _console.MarkupLine($"[red]Could not download chapter: {Markup.Escape(trackTitle)}[/]");
                    return string.Empty;
                }

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                progressTask.MaxValue = totalBytes > 0 ? totalBytes : 1;

                await using var contentStream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = File.Create(filePath);

                var buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    totalRead += bytesRead;
                    progressTask.Value = totalRead;
                }

                if (totalBytes == 0)
                    progressTask.Value = progressTask.MaxValue;

                return filePath;
            }
            catch (Exception ex)
            {
                _console.MarkupLine($"[red]Error downloading chapter {Markup.Escape(trackSrc)}: {Markup.Escape(ex.Message)}[/]");
                return string.Empty;
            }
        }

        protected async Task<string> DownloadDirectFileAsync(string trackSrc, string trackTitle, string folderPath)
        {
            var filePath = Path.Combine(folderPath, trackTitle);
            try
            {
                var response = await _httpService.GetAsync(trackSrc);
                if (!response.IsSuccessStatusCode)
                {
                    _console.MarkupLine($"[red]Could not download chapter: {trackTitle}[/]");
                    return string.Empty;
                }

                await using var contentStream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = File.Create(filePath);
                await contentStream.CopyToAsync(fileStream);

                return filePath;
            }
            catch (Exception ex)
            {
                _console.MarkupLine($"[red]Error downloading chapter {trackSrc}: {ex.Message}[/]");
                return string.Empty;
            }
        }

        protected async Task ConvertDirectFileTrackAsync(DirectFileTrackData track,
            AudiobookMetadata? bookMetadata = null, string? coverArtPath = null)
        {
            try
            {
                var ext = Path.GetExtension(track.FilePath).ToLowerInvariant();

                if (_settings.ConvertToMp3)
                {
                    if (ext != ".mp3")
                    {
                        var mp3Output = Path.ChangeExtension(track.FilePath, ".mp3");
                        await ConvertToFormatAsync(track.FilePath, mp3Output, "-c:a libmp3lame -b:a 128k", bookMetadata, track, coverArtPath);
                    }
                    else if (bookMetadata != null)
                    {
                        var taggedPath = track.FilePath + ".tmp.mp3";
                        await ConvertToFormatAsync(track.FilePath, taggedPath, "-c:a copy", bookMetadata, track, coverArtPath);
                        File.Delete(track.FilePath);
                        File.Move(taggedPath, track.FilePath);
                    }
                }

                if (_settings.ConvertToM4b)
                {
                    if (ext != ".m4b")
                    {
                        var m4bOutput = Path.ChangeExtension(track.FilePath, ".m4b");
                        await ConvertToFormatAsync(track.FilePath, m4bOutput, "-c:a aac -b:a 64k", bookMetadata, track, coverArtPath);
                    }
                    else if (bookMetadata != null)
                    {
                        var taggedPath = track.FilePath + ".tmp.m4b";
                        await ConvertToFormatAsync(track.FilePath, taggedPath, "-c:a copy", bookMetadata, track, coverArtPath);
                        File.Delete(track.FilePath);
                        File.Move(taggedPath, track.FilePath);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Conversion failed: {ex.Message}", ex);
            }
        }

        protected async Task ConvertTrackToFormatsAsync(string inputFile, string outputFolder, string baseFileName,
            AudiobookMetadata? bookMetadata = null, TrackData? trackData = null, string? coverArtPath = null)
        {
            SetFFmpegPath();

            if (_settings.ConvertToMp3)
            {
                var mp3Output = Path.Combine(outputFolder, $"{baseFileName}.mp3");
                await ConvertToFormatAsync(inputFile, mp3Output, "-c:a libmp3lame -b:a 128k", bookMetadata, trackData, coverArtPath);
            }

            if (_settings.ConvertToM4b)
            {
                var m4bOutput = Path.Combine(outputFolder, $"{baseFileName}.m4b");
                await ConvertToFormatAsync(inputFile, m4bOutput, "-c:a aac -b:a 64k", bookMetadata, trackData, coverArtPath);
            }
        }

        protected async Task MergeTsSegmentsAsync(
            string tempFolder,
            List<string> segments,
            string outputFile,
            string codecParams,
            AudiobookMetadata? bookMetadata = null,
            TrackData? trackData = null,
            string? coverArtPath = null)
        {
            var concatFile = Path.Combine(tempFolder, "concat.txt");
            var concatLines = new List<string>();

            for (int i = 0; i < segments.Count; i++)
            {
                var segmentPath = Path.Combine(tempFolder, $"{i:D4}_{segments[i]}");
                if (File.Exists(segmentPath))
                {
                    var escapedPath = segmentPath.Replace("'", "'\\''");
                    concatLines.Add($"file '{escapedPath}'");
                }
            }

            if (concatLines.Count == 0)
            {
                throw new InvalidOperationException("No segments available for merging");
            }

            await File.WriteAllLinesAsync(concatFile, concatLines);

            var hasCover = !string.IsNullOrEmpty(coverArtPath) && File.Exists(coverArtPath);

            IConversion conversion = FFmpeg.Conversions.New();
            conversion.AddParameter($"-f concat -safe 0 -i \"{concatFile}\"");
            if (hasCover)
            {
                conversion.AddParameter($"-i \"{coverArtPath}\"");
                conversion.AddParameter("-map 0:a -map 1:v");
            }
            conversion.AddParameter(codecParams);
            var metaParams = BuildMetadataParams(bookMetadata, trackData, hasCover);
            if (!string.IsNullOrEmpty(metaParams))
                conversion.AddParameter(metaParams);
            conversion.SetOutput(outputFile);
            await conversion.Start();
        }

        protected async Task ConvertToFormatAsync(string inputFile, string outputFile, string codecParams,
            AudiobookMetadata? bookMetadata = null, TrackData? trackData = null, string? coverArtPath = null)
        {
            var hasCover = !string.IsNullOrEmpty(coverArtPath) && File.Exists(coverArtPath);

            IConversion conversion = FFmpeg.Conversions.New();
            conversion.AddParameter($"-i \"{inputFile}\"");
            if (hasCover)
            {
                conversion.AddParameter($"-i \"{coverArtPath}\"");
                conversion.AddParameter("-map 0:a -map 1:v");
            }
            conversion.AddParameter(codecParams);
            var metaParams = BuildMetadataParams(bookMetadata, trackData, hasCover);
            if (!string.IsNullOrEmpty(metaParams))
                conversion.AddParameter(metaParams);
            conversion.SetOutput(outputFile);
            await conversion.Start();
        }

        protected static string GetChapterDisplayTitle(string trackTitle, int trackNumber)
        {
            var ext = Path.GetExtension(trackTitle);
            if (!string.IsNullOrEmpty(ext))
            {
                var nameWithoutExt = Path.GetFileNameWithoutExtension(trackTitle);
                if (int.TryParse(nameWithoutExt, out _))
                    return $"Chapter {trackNumber}";
                return nameWithoutExt.Replace("_", " ").Trim();
            }
            return trackTitle;
        }

        private static string EscapeFfmetadata(string value) =>
            value.Replace("\\", "\\\\").Replace("=", "\\=").Replace("#", "\\#")
                 .Replace(";", "\\;").Replace("\n", "\\\n").Replace("\r", "");

        protected async Task<bool> HasChapterTagsAsync(string filePath)
        {
            if (string.IsNullOrEmpty(_settings.FFmpegDirectory)) return false;

            var ffprobeName = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
            var ffprobePath = Path.Combine(_settings.FFmpegDirectory, ffprobeName);
            if (!File.Exists(ffprobePath)) return false;

            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ffprobePath,
                        Arguments = $"-v quiet -print_format json -show_chapters \"{filePath}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (string.IsNullOrWhiteSpace(output)) return false;

                var json = JObject.Parse(output);
                var chapters = json["chapters"] as JArray;
                return chapters != null && chapters.Count > 0;
            }
            catch { }

            return false;
        }

        private async Task<long> GetTrackDurationMsAsync(string filePath)
        {
            if (string.IsNullOrEmpty(_settings.FFmpegDirectory)) return 0;

            var ffprobeName = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
            var ffprobePath = Path.Combine(_settings.FFmpegDirectory, ffprobeName);
            if (!File.Exists(ffprobePath)) return 0;

            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ffprobePath,
                        Arguments = $"-v quiet -print_format json -show_entries format=duration \"{filePath}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (string.IsNullOrWhiteSpace(output)) return 0;

                var json = JObject.Parse(output);
                var durationStr = json["format"]?["duration"]?.ToString();
                if (double.TryParse(durationStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var duration))
                    return (long)(duration * 1000);
            }
            catch { }

            return 0;
        }

        protected async Task CombineTracksToSingleM4bAsync(
            List<(int TrackNumber, string FilePath, string Title)> tracks,
            string outputFolder,
            string bookTitle,
            AudiobookMetadata? bookMetadata,
            string? coverArtPath)
        {
            var tempDir = Path.Combine(outputFolder, $"_combine_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                _console.MarkupLine("[blue]Reading chapter durations...[/]");

                var durations = new List<long>();
                foreach (var (_, filePath, _) in tracks)
                    durations.Add(await GetTrackDurationMsAsync(filePath));

                // Build FFMETADATA file with book metadata + chapter markers
                var metaLines = new List<string> { ";FFMETADATA1" };
                if (bookMetadata != null)
                {
                    if (!string.IsNullOrEmpty(bookMetadata.Title))
                    {
                        metaLines.Add($"title={EscapeFfmetadata(bookMetadata.Title)}");
                        metaLines.Add($"album={EscapeFfmetadata(bookMetadata.Title)}");
                    }
                    if (!string.IsNullOrEmpty(bookMetadata.Author))
                        metaLines.Add($"artist={EscapeFfmetadata(bookMetadata.Author)}");
                    if (!string.IsNullOrEmpty(bookMetadata.Narrator))
                        metaLines.Add($"album_artist={EscapeFfmetadata(bookMetadata.Narrator)}");
                    metaLines.Add("genre=Audiobook");
                    if (!string.IsNullOrEmpty(bookMetadata.Publisher))
                        metaLines.Add($"publisher={EscapeFfmetadata(bookMetadata.Publisher)}");
                    if (!string.IsNullOrEmpty(bookMetadata.Year))
                        metaLines.Add($"date={EscapeFfmetadata(bookMetadata.Year)}");
                    if (!string.IsNullOrEmpty(bookMetadata.Description))
                    {
                        var desc = StripHtml(bookMetadata.Description);
                        if (desc.Length > 500) desc = desc[..500];
                        metaLines.Add($"comment={EscapeFfmetadata(desc)}");
                    }
                }

                long cursor = 0;
                for (int i = 0; i < tracks.Count; i++)
                {
                    var duration = durations[i];
                    metaLines.Add(string.Empty);
                    metaLines.Add("[CHAPTER]");
                    metaLines.Add("TIMEBASE=1/1000");
                    metaLines.Add($"START={cursor}");
                    metaLines.Add($"END={cursor + duration}");
                    metaLines.Add($"title={EscapeFfmetadata(tracks[i].Title)}");
                    cursor += duration;
                }

                var metaFile = Path.Combine(tempDir, "metadata.txt");
                await File.WriteAllLinesAsync(metaFile, metaLines);

                // Build concat.txt
                var concatFile = Path.Combine(tempDir, "concat.txt");
                var concatLines = tracks
                    .Select(t => $"file '{t.FilePath.Replace("'", "'\\''")}'")
                    .ToList();
                await File.WriteAllLinesAsync(concatFile, concatLines);

                var sanitizedTitle = SanitizeName(bookTitle);
                var outputFile = Path.Combine(outputFolder, $"{sanitizedTitle}.m4b");
                var hasCover = !string.IsNullOrEmpty(coverArtPath) && File.Exists(coverArtPath);

                IConversion conversion = FFmpeg.Conversions.New();
                conversion.AddParameter($"-f concat -safe 0 -i \"{concatFile}\"");
                if (hasCover)
                {
                    conversion.AddParameter($"-i \"{coverArtPath}\"");
                    conversion.AddParameter($"-i \"{metaFile}\"");
                    conversion.AddParameter("-map_metadata 2 -map 0:a -map 1:v");
                    conversion.AddParameter("-c:a copy -c:v copy -disposition:v attached_pic");
                }
                else
                {
                    conversion.AddParameter($"-i \"{metaFile}\"");
                    conversion.AddParameter("-map_metadata 1 -map 0:a");
                    conversion.AddParameter("-c:a copy");
                }
                conversion.SetOutput(outputFile);
                await conversion.Start();

                _console.MarkupLine($"[green]Combined M4B with {tracks.Count} chapters:[/] {Markup.Escape(Path.GetFileName(outputFile))}");
            }
            finally
            {
                SafeDeleteDirectory(tempDir);
            }
        }

        protected async Task<T?> RetryAsync<T>(Func<Task<T>> action, int maxRetries = 3, int delayMs = 1000)
        {
            if (maxRetries <= 0) throw new ArgumentOutOfRangeException(nameof(maxRetries), "Must be greater than 0");

            for (int retry = 0; retry < maxRetries; retry++)
            {
                try
                {
                    return await action();
                }
                catch
                {
                    if (retry < maxRetries - 1)
                        await Task.Delay(delayMs * (retry + 1));
                    else
                        throw;
                }
            }

            return default;
        }

        protected void SafeDeleteDirectory(string path, int maxRetries = 3)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                        {
                            try
                            {
                                File.SetAttributes(file, FileAttributes.Normal);
                                File.Delete(file);
                            }
                            catch { }
                        }

                        Directory.Delete(path, true);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    if (i == maxRetries - 1)
                        _console.MarkupLine($"[yellow]Warning: Could not delete directory {path}: {ex.Message}[/]");
                    else
                        Thread.Sleep(1000);
                }
            }
        }

        protected string PrepareOutputFolder(string bookTitle)
        {
            var folderPath = Path.Combine(_settings.DownloadPath, SanitizeName(bookTitle));
            Directory.CreateDirectory(folderPath);
            return folderPath;
        }

        protected void ShowCompletionMessage(string folderPath)
        {
            _console.MarkupLine("[green]Download finished[/]");
            _console.MarkupLine($"[grey]Audiobook saved in:[/] {folderPath}");
            _console.MarkupLine("Press any key to continue");
            _console.Input.ReadKey(true);
        }

        protected void ShowErrorMessage(string message)
        {
            _console.MarkupLine($"[red]{message}[/]");
            _console.MarkupLine("Press any key to continue");
            _console.Input.ReadKey(true);
        }
    }
}
