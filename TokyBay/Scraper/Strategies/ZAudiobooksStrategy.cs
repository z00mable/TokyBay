using Spectre.Console;
using System.Text.RegularExpressions;
using System.Threading.Channels;
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
            return bookUrl.Contains("freeaudiobooks.top", StringComparison.OrdinalIgnoreCase) ||
                   bookUrl.Contains("zaudiobooks", StringComparison.OrdinalIgnoreCase);
        }

        public override async Task DownloadBookAsync(string bookUrl)
        {
            var metadata = await FetchMetadataAsync(bookUrl);
            if (metadata == null || metadata.ChapterUrls.Count == 0)
            {
                ShowErrorMessage("No valid tracks found");
                return;
            }

            var folderPath = PrepareOutputFolder(metadata.Title);

            _console.MarkupLine($"[green]Found {metadata.ChapterUrls.Count} tracks[/]");
            _console.MarkupLine($"[blue]Parallel downloads:[/] {_config.MaxParallelDownloads}");
            _console.MarkupLine($"[blue]Parallel conversions:[/] {_config.MaxParallelConversions}");

            await ProcessTracksInParallelAsync(metadata, folderPath);

            ShowCompletionMessage();
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

        private async Task ProcessTracksInParallelAsync(SimpleAudiobookMetadata metadata, string folderPath)
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

            var downloadTasks = metadata.ChapterUrls.Select((chapterUrl, index) => Task.Run(async () =>
            {
                await downloadSemaphore.WaitAsync();
                try
                {
                    await Task.Delay(100 * (index + 1));

                    var trackTitle = chapterUrl.Split('/').Last();
                    var filePath = await DownloadTrackAsync(chapterUrl, trackTitle, folderPath, index + 1, totalTracks);

                    if (!string.IsNullOrEmpty(filePath))
                    {
                        var trackData = new DirectFileTrackData
                        {
                            FilePath = filePath,
                            FolderPath = folderPath,
                            TrackTitle = trackTitle,
                            SanitizedTitle = SanitizeName(trackTitle),
                            TrackNumber = index + 1,
                            TotalTracks = totalTracks
                        };

                        await conversionChannel.Writer.WriteAsync(trackData);

                        lock (lockObj)
                        {
                            completedDownloads++;
                            _console.MarkupLine($"[green]Downloaded:[/] {completedDownloads}/{totalTracks} - {trackTitle}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    lock (lockObj)
                    {
                        var trackTitle = chapterUrl.Split('/').Last();
                        _console.MarkupLine($"[red]Download error for {trackTitle}: {ex.Message}[/]");
                    }
                }
                finally
                {
                    downloadSemaphore.Release();
                }
            })).ToList();

            var conversionTasks = Enumerable.Range(0, _config.MaxParallelConversions)
                .Select(_ => Task.Run(async () =>
                {
                    await foreach (var track in conversionChannel.Reader.ReadAllAsync())
                    {
                        try
                        {
                            await ConvertDirectFileTrackAsync(track);

                            lock (lockObj)
                            {
                                completedConversions++;
                                _console.MarkupLine($"[cyan]Converted:[/] {completedConversions}/{totalTracks} - {track.TrackTitle}");
                            }
                        }
                        catch (Exception ex)
                        {
                            lock (lockObj)
                            {
                                _console.MarkupLine($"[red]Conversion error for {track.TrackTitle}: {ex.Message}[/]");
                            }
                        }
                    }
                })).ToList();

            await Task.WhenAll(downloadTasks);
            conversionChannel.Writer.Complete();
            await Task.WhenAll(conversionTasks);
        }

        private async Task<string> DownloadTrackAsync(string trackSrc, string trackTitle, string folderPath, int trackNumber, int totalTracks)
        {
            var filePath = Path.Combine(folderPath, trackTitle);
            try
            {
                var response = await _httpUtil.GetAsync(trackSrc);
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

        private async Task ConvertDirectFileTrackAsync(DirectFileTrackData track)
        {
            try
            {
                bool needsMp3Conversion = _settings.ConvertToMp3 && !await IsValidMp3Async(track.FilePath);

                if (needsMp3Conversion)
                {
                    var mp3Output = track.FilePath + ".mp3";
                    await ConvertToFormatAsync(track.FilePath, mp3Output, "-c:a libmp3lame -b:a 128k");
                }

                if (_settings.ConvertToM4b)
                {
                    var filePathWithoutExtension = string.Join('.', track.FilePath.Split('.').SkipLast(1));
                    var m4bOutput = filePathWithoutExtension + ".m4b";
                    await ConvertToFormatAsync(track.FilePath, m4bOutput, "-c:a aac -b:a 64k");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Conversion failed: {ex.Message}", ex);
            }
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

                var title = ExtractTitle(html);

                return new SimpleAudiobookMetadata
                {
                    Title = title,
                    ChapterUrls = chapterUrls
                };
            }
            catch (Exception ex)
            {
                _console.MarkupLine($"[red]Error fetching chapter URLs: {ex.Message}[/]");
                return null;
            }
        }

        private static string ExtractTitle(string html)
        {
            var h1Match = Regex.Match(html, @"<h1[^>]*>(.*?)</h1>", RegexOptions.Singleline);
            if (h1Match.Success)
            {
                return System.Net.WebUtility.HtmlDecode(h1Match.Groups[1].Value.Trim());
            }

            return "Unknown Title";
        }

        private static string ExtractUrl(string line)
        {
            var startQuote = line.IndexOf(":") + 1;
            var url = line[startQuote..]
                .Trim()
                .Trim('"', ',', ' ')
                .Replace("\\", "");

            return url;
        }
    }
}