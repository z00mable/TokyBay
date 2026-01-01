using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using TokyBay.Models;
using Xabe.FFmpeg;

namespace TokyBay
{
    public static class Downloader
    {
        private const string TokybookUrl = "https://tokybook.com";
        private const string PostDetailsApiPath = "/api/v1/search/post-details";
        private const string PlaylistApiPath = "/api/v1/playlist";
        private const string AudioBaseApiPath = "/api/v1/public/audio/";

        private const int MaxParallelDownloads = 2;
        private const int MaxParallelConversions = 2;
        private const int MaxSegmentsPerTrack = 3;

        public static async Task GetInput()
        {
            AnsiConsole.Clear();
            Constants.ShowHeader();
            AnsiConsole.MarkupLine($"[grey]Audiobook will be saved in:[/] {SettingsMenu.UserSettings.DownloadPath}");
            while (true)
            {
                AnsiConsole.WriteLine();
                var url = AnsiConsole.Ask<string>("Enter URL:");
                if (url == null || !url.StartsWith("https://tokybook.com/"))
                {
                    AnsiConsole.MarkupLine("[red]Invalid URL! Try again.[/]");
                    continue;
                }

                await GetChapters(url);
                break;
            }
        }

        public static async Task GetChapters(string bookUrl)
        {
            JArray? tracks = null;
            string bookTitle = string.Empty;
            string audioBookId = string.Empty;
            string streamToken = string.Empty;

            await AnsiConsole.Status()
                .SpinnerStyle(Style.Parse("blue bold"))
                .StartAsync("Preparing download...", async ctx =>
                {
                    ctx.Status("Getting user identity...");
                    var userIdentity = await TokybookApiHandler.GetUserIdentity();

                    ctx.Status("Extracting dynamic slug...");
                    var dynamicSlugId = ExtractDynamicSlugId(bookUrl);
                    if (string.IsNullOrEmpty(dynamicSlugId))
                    {
                        AnsiConsole.MarkupLine("[red]Could not extract slug from URL.[/]");
                        return;
                    }

                    ctx.Status("Fetching post details...");
                    var postDetails = await GetPostDetails(dynamicSlugId, userIdentity);
                    if (postDetails == null)
                    {
                        AnsiConsole.MarkupLine("[red]Failed to get post details.[/]");
                        return;
                    }

                    audioBookId = postDetails["audioBookId"]?.ToString() ?? string.Empty;
                    var postDetailToken = postDetails["postDetailToken"]?.ToString();
                    bookTitle = postDetails["title"]?.ToString() ?? "Unknown";

                    if (string.IsNullOrEmpty(audioBookId) || string.IsNullOrEmpty(postDetailToken))
                    {
                        AnsiConsole.MarkupLine("[red]Missing audioBookId or postDetailToken.[/]");
                        return;
                    }

                    ctx.Status("Fetching playlist...");
                    var playlistResponse = await GetPlaylist(audioBookId, postDetailToken, dynamicSlugId, userIdentity);
                    if (playlistResponse == null)
                    {
                        AnsiConsole.MarkupLine("[red]Failed to get playlist.[/]");
                        return;
                    }

                    tracks = playlistResponse["tracks"] as JArray;
                    streamToken = playlistResponse["streamToken"]?.ToString() ?? string.Empty;

                    if (string.IsNullOrEmpty(streamToken))
                    {
                        AnsiConsole.MarkupLine("[red]Missing streamToken.[/]");
                        return;
                    }
                });

            if (tracks == null || tracks.Count == 0)
            {
                AnsiConsole.MarkupLine("[red]No valid tracks found.[/]");
                AnsiConsole.MarkupLine("Press any key to continue");
                Console.ReadKey(true);
                return;
            }

            var folderPath = Path.Combine(SettingsMenu.UserSettings.DownloadPath, SanitizeName(bookTitle));
            Directory.CreateDirectory(folderPath);

            AnsiConsole.MarkupLine($"[green]Found {tracks.Count} tracks[/]");
            AnsiConsole.MarkupLine($"[blue]Parallel downloads:[/] {MaxParallelDownloads}");
            AnsiConsole.MarkupLine($"[blue]Parallel conversions:[/] {MaxParallelConversions}");

            await ProcessTracksInParallel(tracks, audioBookId, streamToken, folderPath);

            AnsiConsole.MarkupLine("[green]Download finished[/]");
            AnsiConsole.MarkupLine("Press any key to continue");
            Console.ReadKey(true);
        }

        private static async Task ProcessTracksInParallel(JArray tracks, string audioBookId, string streamToken, string folderPath)
        {
            var conversionChannel = Channel.CreateBounded<DownloadedTrack>(new BoundedChannelOptions(10)
            {
                FullMode = BoundedChannelFullMode.Wait
            });

            var downloadSemaphore = new SemaphoreSlim(MaxParallelDownloads);
            var completedDownloads = 0;
            var completedConversions = 0;
            var totalTracks = tracks.Count;
            var lockObj = new object();

            var downloadTasks = new List<Task>();
            for (int i = 0; i < tracks.Count; i++)
            {
                var track = tracks[i];
                var trackNumber = i + 1;

                var src = track["src"]?.ToString();
                if (string.IsNullOrEmpty(src))
                {
                    continue;
                }

                var trackTitle = track["trackTitle"]?.ToString() ?? "Unknown";

                downloadTasks.Add(Task.Run(async () =>
                {
                    await downloadSemaphore.WaitAsync();
                    try
                    {
                        await Task.Delay(100 * trackNumber);

                        var downloadedTrack = await DownloadTrackAsync(audioBookId, streamToken, src, trackTitle, folderPath, trackNumber, totalTracks);
                        if (downloadedTrack != null)
                        {
                            await conversionChannel.Writer.WriteAsync(downloadedTrack);

                            lock (lockObj)
                            {
                                completedDownloads++;
                                AnsiConsole.MarkupLine($"[green]Downloaded:[/] {completedDownloads}/{totalTracks} - {trackTitle}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (lockObj)
                        {
                            AnsiConsole.MarkupLine($"[red]Download error for {trackTitle}: {ex.Message}[/]");
                        }
                    }
                    finally
                    {
                        downloadSemaphore.Release();
                    }
                }));
            }

            var conversionTasks = new List<Task>();
            for (int i = 0; i < MaxParallelConversions; i++)
            {
                conversionTasks.Add(Task.Run(async () =>
                {
                    await foreach (var downloadedTrack in conversionChannel.Reader.ReadAllAsync())
                    {
                        try
                        {
                            await ConvertTrackAsync(downloadedTrack);

                            lock (lockObj)
                            {
                                completedConversions++;
                                AnsiConsole.MarkupLine($"[cyan]Converted:[/] {completedConversions}/{totalTracks} - {downloadedTrack.TrackTitle}");
                            }
                        }
                        catch (Exception ex)
                        {
                            lock (lockObj)
                            {
                                AnsiConsole.MarkupLine($"[red]Conversion error for {downloadedTrack.TrackTitle}: {ex.Message}[/]");
                            }
                        }
                    }
                }));
            }

            await Task.WhenAll(downloadTasks);
            conversionChannel.Writer.Complete();
            await Task.WhenAll(conversionTasks);
        }

        private static async Task<DownloadedTrack?> DownloadTrackAsync(string audioBookId, string streamToken, string trackSrc, string trackTitle, string folderPath, int trackNumber, int totalTracks)
        {
            try
            {
                var sanitizedTitle = SanitizeName(trackTitle);
                var tempFolder = Path.Combine(folderPath, "_temp_" + sanitizedTitle + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(tempFolder);

                var basePath = trackSrc.Substring(0, trackSrc.LastIndexOf('/') + 1);
                var escapedTrackSrc = basePath + Uri.EscapeDataString(trackSrc.Substring(trackSrc.LastIndexOf('/') + 1));

                var m3u8Url = TokybookUrl + AudioBaseApiPath + escapedTrackSrc;

                string? m3u8Content = null;
                for (int retry = 0; retry < 3; retry++)
                {
                    m3u8Content = await DownloadM3u8Playlist(audioBookId, streamToken, m3u8Url, escapedTrackSrc);
                    if (!string.IsNullOrEmpty(m3u8Content))
                    {
                        break;
                    }

                    if (retry < 2)
                    {
                        await Task.Delay(1000 * (retry + 1));
                    }
                }

                if (string.IsNullOrEmpty(m3u8Content))
                {
                    AnsiConsole.MarkupLine($"[red]Failed to download playlist for {trackTitle}[/]");
                    Directory.Delete(tempFolder, true);
                    return null;
                }

                var tsSegments = ParseTsSegments(m3u8Content);
                if (tsSegments.Count == 0)
                {
                    AnsiConsole.MarkupLine($"[red]No segments found in playlist for {trackTitle}[/]");
                    Directory.Delete(tempFolder, true);
                    return null;
                }

                await DownloadTsSegments(audioBookId, streamToken, basePath, tsSegments, tempFolder, trackTitle);

                return new DownloadedTrack
                {
                    TempFolder = tempFolder,
                    TrackTitle = trackTitle,
                    FolderPath = folderPath,
                    SanitizedTitle = sanitizedTitle,
                    TsSegments = tsSegments,
                    TrackNumber = trackNumber,
                    TotalTracks = totalTracks
                };
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error downloading track {trackTitle}: {ex.Message}[/]");
                return null;
            }
        }

        private static async Task ConvertTrackAsync(DownloadedTrack track)
        {
            try
            {
                if (SettingsMenu.UserSettings.ConvertToMp3)
                {
                    var outputFile = Path.Combine(track.FolderPath, track.SanitizedTitle + ".mp3");
                    await MergeTsSegments(track.TempFolder, track.TsSegments, outputFile, track.TrackTitle, isMp3Conversion: true);
                }

                if (SettingsMenu.UserSettings.ConvertToM4b)
                {
                    var outputFile = Path.Combine(track.FolderPath, track.SanitizedTitle + ".m4b");
                    await MergeTsSegments(track.TempFolder, track.TsSegments, outputFile, track.TrackTitle, isMp3Conversion: false);
                }

                if (Directory.Exists(track.TempFolder))
                {
                    try
                    {
                        Directory.Delete(track.TempFolder, true);
                    }
                    catch
                    {
                        AnsiConsole.MarkupLine($"[red]Error deleting temp folder: {track.TempFolder}[/]");
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Conversion failed: {ex.Message}", ex);
            }
        }

        private static async Task<string> DownloadM3u8Playlist(string audioBookId, string streamToken, string m3u8Url, string trackSrc)
        {
            try
            {
                var response = await HttpUtil.GetTracksAsync(m3u8Url, audioBookId, streamToken, AudioBaseApiPath + trackSrc);
                if (!response.IsSuccessStatusCode)
                {
                    return string.Empty;
                }

                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error downloading playlist {m3u8Url}: {ex.Message}[/]");
                return string.Empty;
            }
        }

        private static List<string> ParseTsSegments(string m3u8Content)
        {
            var segments = new List<string>();
            var lines = m3u8Content.Split('\n');

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("#") && trimmed.EndsWith(".ts"))
                {
                    segments.Add(trimmed);
                }
            }

            return segments;
        }

        private static async Task DownloadTsSegments(string audioBookId, string streamToken, string basePath, List<string> segments, string outputFolder, string trackTitle)
        {
            var segmentSemaphore = new SemaphoreSlim(MaxSegmentsPerTrack);
            var downloadTasks = new List<Task>();
            var successCount = 0;
            var lockObj = new object();

            for (int i = 0; i < segments.Count; i++)
            {
                var index = i;
                var segment = segments[i];

                downloadTasks.Add(Task.Run(async () =>
                {
                    await segmentSemaphore.WaitAsync();
                    try
                    {
                        var segmentUrl = TokybookUrl + AudioBaseApiPath + basePath + segment;
                        var trackSrc = basePath + segment;

                        for (int retry = 0; retry < 3; retry++)
                        {
                            try
                            {
                                var response = await HttpUtil.GetTracksAsync(segmentUrl, audioBookId, streamToken, AudioBaseApiPath + trackSrc);
                                if (response.IsSuccessStatusCode)
                                {
                                    var segmentPath = Path.Combine(outputFolder, $"{index:D4}_{segment}");
                                    var bytes = await response.Content.ReadAsByteArrayAsync();
                                    await File.WriteAllBytesAsync(segmentPath, bytes);

                                    lock (lockObj)
                                    {
                                        successCount++;
                                    }
                                    break;
                                }
                            }
                            catch
                            {
                                if (retry < 2)
                                    await Task.Delay(500 * (retry + 1));
                            }
                        }
                    }
                    finally
                    {
                        segmentSemaphore.Release();
                    }
                }));
            }

            await Task.WhenAll(downloadTasks);

            if (successCount < segments.Count)
            {
                throw new Exception($"Only {successCount}/{segments.Count} segments downloaded successfully");
            }
        }

        private static async Task MergeTsSegments(string tempFolder, List<string> segments, string outputFile, string trackTitle, bool isMp3Conversion)
        {
            FFmpeg.SetExecutablesPath(SettingsMenu.UserSettings.FFmpegDirectory);

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
                throw new Exception("No segments available for merging");
            }

            await File.WriteAllLinesAsync(concatFile, concatLines);

            IConversion conversion = FFmpeg.Conversions.New();
            conversion.AddParameter($"-f concat -safe 0 -i \"{concatFile}\"");
            if (isMp3Conversion)
            {
                conversion.AddParameter("-c:a libmp3lame -b:a 128k");
            }
            else
            {
                conversion.AddParameter("-c:a aac -b:a 64k");
            }

            conversion.SetOutput(outputFile);
            await conversion.Start();
        }

        private static async Task<JObject?> GetPostDetails(string dynamicSlugId, JObject userIdentity)
        {
            try
            {
                var payload = new JObject
                {
                    ["dynamicSlugId"] = dynamicSlugId,
                    ["userIdentity"] = userIdentity
                };

                var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
                var response = await HttpUtil.PostAsync(TokybookUrl + PostDetailsApiPath, content);

                if (!response.IsSuccessStatusCode)
                {
                    AnsiConsole.MarkupLine($"[red]Post details request failed: {response.StatusCode}[/]");
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                return JObject.Parse(json);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error getting post details: {ex.Message}[/]");
                return null;
            }
        }

        private static async Task<JObject?> GetPlaylist(string audioBookId, string postDetailToken, string dynamicSlugId, JObject userIdentity)
        {
            try
            {
                var payload = new JObject
                {
                    ["audioBookId"] = audioBookId,
                    ["postDetailToken"] = postDetailToken,
                    ["userIdentity"] = new JObject
                    {
                        ["ipAddress"] = userIdentity["ipAddress"],
                        ["timestamp"] = userIdentity["timestamp"],
                        ["userAgent"] = userIdentity["userAgent"],
                    }
                };

                var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
                var response = await HttpUtil.PostAsync(TokybookUrl + PlaylistApiPath, content);

                if (!response.IsSuccessStatusCode)
                {
                    AnsiConsole.MarkupLine($"[red]Playlist request failed: {response.StatusCode}[/]");
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                return JObject.Parse(json);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error getting playlist: {ex.Message}[/]");
                return null;
            }
        }

        private static string ExtractDynamicSlugId(string url)
        {
            var uri = new Uri(url);
            var segments = uri.Segments;
            if (segments.Length >= 3 && segments[1] == "post/")
            {
                return segments[2].TrimEnd('/');
            }

            return string.Empty;
        }

        private static string SanitizeName(string fileName)
        {
            return Regex.Replace(fileName, "[^A-Za-z0-9]+", "_");
        }
    }
}