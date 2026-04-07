using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.Text;
using System.Threading.Channels;
using TokyBay.Models;
using TokyBay.Scraper.Base;
using TokyBay.Scraper.Configuration;
using TokyBay.Services;

namespace TokyBay.Scraper.Strategies
{
    public class TokybookStrategy(
        IAnsiConsole console,
        IHttpService httpService,
        IIpifyService ipifyService,
        ISettingsService settingsService,
        ScraperConfig? config = null) : BaseScraperStrategy(console, httpService, settingsService, config)
    {
        private const string TokybookBaseUrl = "https://tokybook.com";
        private const string PostDetailsApiPath = "/api/v1/search/post-details";
        private const string PlaylistApiPath = "/api/v1/playlist";
        private const string AudioBaseApiPath = "/api/v1/public/audio/";

        private readonly IIpifyService _ipifyService = ipifyService;

        public override bool CanHandle(string bookUrl)
        {
            return bookUrl.Contains("tokybook.com", StringComparison.OrdinalIgnoreCase);
        }

        public override async Task DownloadBookAsync(string bookUrl)
        {
            var metadata = await FetchMetadataAsync(bookUrl);
            if (metadata == null)
            {
                ShowErrorMessage("Failed to fetch audiobook metadata");
                return;
            }

            SetFFmpegPath();

            var folderPath = PrepareOutputFolder(metadata.Title);

            _console.MarkupLine($"[green]Found {metadata.Tracks.Count} tracks[/]");
            _console.MarkupLine($"[blue]Parallel downloads:[/] {_config.MaxParallelDownloads}");
            _console.MarkupLine($"[blue]Parallel conversions:[/] {_config.MaxParallelConversions}");

            await ProcessTracksInParallelAsync(metadata, folderPath);

            ShowCompletionMessage(folderPath);
        }

        private async Task<StreamingAudiobookMetadata?> FetchMetadataAsync(string bookUrl)
        {
            StreamingAudiobookMetadata? metadata = null;

            await _console.Status()
                .SpinnerStyle(Style.Parse("blue bold"))
                .StartAsync("Preparing download...", async ctx =>
                {
                    ctx.Status("Getting user identity...");
                    var userIdentity = await _ipifyService.GetUserIdentityAsync();

                    ctx.Status("Extracting dynamic slug...");
                    var dynamicSlugId = ExtractDynamicSlugId(bookUrl);
                    if (string.IsNullOrEmpty(dynamicSlugId))
                    {
                        _console.MarkupLine("[red]Could not extract slug from URL.[/]");
                        return;
                    }

                    ctx.Status("Fetching post details...");
                    var postDetails = await GetPostDetailsAsync(dynamicSlugId, userIdentity);
                    if (postDetails == null)
                    {
                        _console.MarkupLine("[red]Failed to get post details.[/]");
                        return;
                    }

                    var audioBookId = postDetails["audioBookId"]?.ToString() ?? string.Empty;
                    var postDetailToken = postDetails["postDetailToken"]?.ToString();
                    var bookTitle = postDetails["title"]?.ToString() ?? "Unknown";

                    if (string.IsNullOrEmpty(audioBookId) || string.IsNullOrEmpty(postDetailToken))
                    {
                        _console.MarkupLine("[red]Missing audioBookId or postDetailToken.[/]");
                        return;
                    }

                    ctx.Status("Fetching playlist...");
                    var playlistResponse = await GetPlaylistAsync(audioBookId, postDetailToken, userIdentity);
                    if (playlistResponse == null)
                    {
                        _console.MarkupLine("[red]Failed to get playlist.[/]");
                        return;
                    }

                    var tracks = playlistResponse["tracks"] as JArray;
                    var streamToken = playlistResponse["streamToken"]?.ToString() ?? string.Empty;

                    if (tracks == null || tracks.Count == 0 || string.IsNullOrEmpty(streamToken))
                    {
                        _console.MarkupLine("[red]Invalid playlist response.[/]");
                        return;
                    }

                    metadata = new StreamingAudiobookMetadata
                    {
                        Title = bookTitle,
                        AudioBookId = audioBookId,
                        StreamToken = streamToken,
                        Tracks = tracks.Select(t => new TrackInfo
                        {
                            Src = t["src"]?.ToString() ?? string.Empty,
                            TrackTitle = t["trackTitle"]?.ToString() ?? "Unknown"
                        }).Where(t => !string.IsNullOrEmpty(t.Src)).ToList()
                    };
                });

            return metadata;
        }

        private async Task ProcessTracksInParallelAsync(StreamingAudiobookMetadata metadata, string folderPath)
        {
            var conversionChannel = Channel.CreateBounded<SegmentedTrackData>(new BoundedChannelOptions(10)
            {
                FullMode = BoundedChannelFullMode.Wait
            });

            var downloadSemaphore = new SemaphoreSlim(_config.MaxParallelDownloads);
            var completedDownloads = 0;
            var completedConversions = 0;
            var totalTracks = metadata.Tracks.Count;
            var lockObj = new object();

            var downloadTasks = metadata.Tracks.Select((track, index) => Task.Run(async () =>
            {
                await downloadSemaphore.WaitAsync();
                try
                {
                    await Task.Delay(10 * (index + 1));

                    var downloadedTrack = await DownloadTrackAsync(
                        metadata.AudioBookId,
                        metadata.StreamToken,
                        track.Src,
                        track.TrackTitle,
                        folderPath,
                        index + 1,
                        totalTracks);

                    if (downloadedTrack != null)
                    {
                        await conversionChannel.Writer.WriteAsync(downloadedTrack);

                        lock (lockObj)
                        {
                            completedDownloads++;
                            _console.MarkupLine($"[green]Downloaded:[/] {completedDownloads}/{totalTracks} - {track.TrackTitle}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    lock (lockObj)
                    {
                        _console.MarkupLine($"[red]Download error for {track.TrackTitle}: {ex.Message}[/]");
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
                            await ConvertSegmentedTrackAsync(track);

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

        private async Task<SegmentedTrackData?> DownloadTrackAsync(
            string audioBookId,
            string streamToken,
            string trackSrc,
            string trackTitle,
            string folderPath,
            int trackNumber,
            int totalTracks)
        {
            try
            {
                var sanitizedTitle = SanitizeName(trackTitle);
                var tempFolder = Path.Combine(folderPath, $"_temp_{trackNumber}_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempFolder);

                var lastSlash = trackSrc.LastIndexOf('/');
                var basePath = trackSrc[..(lastSlash + 1)];
                var escapedTrackSrc = basePath + Uri.EscapeDataString(trackSrc[(lastSlash + 1)..]);
                var m3u8Url = TokybookBaseUrl + AudioBaseApiPath + escapedTrackSrc;

                var m3u8Content = await RetryAsync(async () =>
                {
                    var content = await DownloadM3u8PlaylistAsync(audioBookId, streamToken, m3u8Url, escapedTrackSrc);
                    return !string.IsNullOrEmpty(content) ? content : throw new InvalidOperationException("Empty playlist");
                }, _config.RetryAttempts, _config.RetryDelayMs);

                if (string.IsNullOrEmpty(m3u8Content))
                {
                    SafeDeleteDirectory(tempFolder);
                    return null;
                }

                var tsSegments = ParseTsSegments(m3u8Content);
                if (tsSegments.Count == 0)
                {
                    SafeDeleteDirectory(tempFolder);
                    return null;
                }

                await DownloadTsSegmentsAsync(audioBookId, streamToken, basePath, tsSegments, tempFolder);

                var downloadedFiles = Directory.GetFiles(tempFolder, "*.ts").Length;
                if (downloadedFiles < tsSegments.Count)
                {
                    _console.MarkupLine($"[yellow]Warning: Only {downloadedFiles}/{tsSegments.Count} segments found for {trackTitle}[/]");
                    SafeDeleteDirectory(tempFolder);
                    return null;
                }

                return new SegmentedTrackData
                {
                    TempFolder = tempFolder,
                    TrackTitle = trackTitle,
                    SanitizedTitle = sanitizedTitle,
                    FolderPath = folderPath,
                    TsSegments = tsSegments,
                    TrackNumber = trackNumber,
                    TotalTracks = totalTracks
                };
            }
            catch (Exception ex)
            {
                _console.MarkupLine($"[red]Error downloading track {trackTitle}: {ex.Message}[/]");
                return null;
            }
        }

        private async Task<string> DownloadM3u8PlaylistAsync(string audioBookId, string streamToken, string m3u8Url, string trackSrc)
        {
            var response = await GetTokybookTracksAsync(m3u8Url, audioBookId, streamToken, AudioBaseApiPath + trackSrc);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync() : string.Empty;
        }

        private static List<string> ParseTsSegments(string m3u8Content)
        {
            return m3u8Content.Split('\n')
                .Select(line => line.Trim())
                .Where(line => !line.StartsWith("#") && line.EndsWith(".ts"))
                .ToList();
        }

        private async Task DownloadTsSegmentsAsync(
            string audioBookId,
            string streamToken,
            string basePath,
            List<string> segments,
            string outputFolder)
        {
            var semaphore = new SemaphoreSlim(_config.MaxSegmentsPerTrack);
            var successCount = 0;

            var tasks = segments.Select((segment, index) => Task.Run(async () =>
            {
                await semaphore.WaitAsync();
                try
                {
                    await RetryAsync(async () =>
                    {
                        var segmentUrl = TokybookBaseUrl + AudioBaseApiPath + basePath + segment;
                        var response = await GetTokybookTracksAsync(
                            segmentUrl,
                            audioBookId,
                            streamToken,
                            AudioBaseApiPath + basePath + segment);

                        if (response.IsSuccessStatusCode)
                        {
                            var segmentPath = Path.Combine(outputFolder, $"{index:D4}_{segment}");
                            var bytes = await response.Content.ReadAsByteArrayAsync();
                            await File.WriteAllBytesAsync(segmentPath, bytes);
                            Interlocked.Increment(ref successCount);
                            return true;
                        }
                        throw new InvalidOperationException("Segment download failed");
                    }, _config.RetryAttempts, 500);
                }
                finally
                {
                    semaphore.Release();
                }
            })).ToArray();

            await Task.WhenAll(tasks);

            if (successCount < segments.Count)
            {
                throw new InvalidOperationException($"Only {successCount}/{segments.Count} segments downloaded");
            }
        }

        private async Task ConvertSegmentedTrackAsync(SegmentedTrackData track)
        {
            try
            {
                if (!Directory.Exists(track.TempFolder))
                    throw new InvalidOperationException($"Temp folder not found: {track.TempFolder}");

                if (Directory.GetFiles(track.TempFolder, "*.ts").Length == 0)
                    throw new InvalidOperationException("No segments available for merging");

                if (_settings.ConvertToMp3)
                {
                    var mp3Output = Path.Combine(track.FolderPath, $"{track.SanitizedTitle}.mp3");
                    await MergeTsSegmentsAsync(track.TempFolder, track.TsSegments, mp3Output, "-c:a libmp3lame -b:a 128k");
                }

                if (_settings.ConvertToM4b)
                {
                    var m4bOutput = Path.Combine(track.FolderPath, $"{track.SanitizedTitle}.m4b");
                    await MergeTsSegmentsAsync(track.TempFolder, track.TsSegments, m4bOutput, "-c:a aac -b:a 64k");
                }

                SafeDeleteDirectory(track.TempFolder);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Conversion failed: {ex.Message}", ex);
            }
        }

        private async Task<JObject?> GetPostDetailsAsync(string dynamicSlugId, JObject userIdentity)
        {
            try
            {
                var payload = new JObject
                {
                    ["dynamicSlugId"] = dynamicSlugId,
                    ["userIdentity"] = userIdentity
                };

                var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
                var response = await _httpService.PostAsync(TokybookBaseUrl + PostDetailsApiPath, content);

                return response.IsSuccessStatusCode ? JObject.Parse(await response.Content.ReadAsStringAsync()) : null;
            }
            catch (Exception ex)
            {
                _console.MarkupLine($"[red]Error getting post details: {ex.Message}[/]");
                return null;
            }
        }

        private async Task<JObject?> GetPlaylistAsync(string audioBookId, string postDetailToken, JObject userIdentity)
        {
            try
            {
                var payload = new JObject
                {
                    ["audioBookId"] = audioBookId,
                    ["postDetailToken"] = postDetailToken,
                    ["userIdentity"] = userIdentity
                };

                var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
                var response = await _httpService.PostAsync(TokybookBaseUrl + PlaylistApiPath, content);

                return response.IsSuccessStatusCode ? JObject.Parse(await response.Content.ReadAsStringAsync()) : null;
            }
            catch (Exception ex)
            {
                _console.MarkupLine($"[red]Error getting playlist: {ex.Message}[/]");
                return null;
            }
        }

        private async Task<HttpResponseMessage> GetTokybookTracksAsync(string url, string audioBookId, string streamToken, string trackSrc)
        {
            var headers = new Dictionary<string, string>
            {
                { "x-audiobook-id", audioBookId },
                { "x-stream-token", streamToken },
                { "x-track-src", trackSrc }
            };

            return await _httpService.GetAsync(url, headers);
        }

        private static string ExtractDynamicSlugId(string url)
        {
            try
            {
                var uri = new Uri(url);
                var segments = uri.Segments;
                return segments.Length >= 3 && segments[1] == "post/"
                    ? segments[2].TrimEnd('/')
                    : string.Empty;
            }
            catch (UriFormatException)
            {
                return string.Empty;
            }
        }
    }
}
