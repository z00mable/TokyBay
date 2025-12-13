using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.Text;
using System.Text.RegularExpressions;
using Xabe.FFmpeg;

namespace TokyBay
{
    public static class Downloader
    {
        private const string TokybookUrl = "https://tokybook.com";
        private const string PostDetailsApiPath = "/api/v1/search/post-details";
        private const string PlaylistApiPath = "/api/v1/playlist";
        private const string AudioBaseApiPath = "/api/v1/public/audio/";

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

            var trackCount = 1;
            foreach (var track in tracks)
            {
                var src = track["src"]?.ToString();
                if (string.IsNullOrEmpty(src))
                {
                    continue;
                }

                var trackTitle = track["trackTitle"]?.ToString() ?? "Unknown";
                await DownloadTrack(audioBookId, streamToken, src, trackTitle, folderPath);
                AnsiConsole.MarkupLine($"[green]Completed:[/] {trackCount++} of {tracks.Count}");
            }

            AnsiConsole.MarkupLine("[green]Download finished[/]");
            AnsiConsole.MarkupLine("Press any key to continue");
            Console.ReadKey(true);
        }

        private static async Task DownloadTrack(string audioBookId, string streamToken, string trackSrc, string trackTitle, string folderPath)
        {
            try
            {
                var sanitizedTitle = SanitizeName(trackTitle);
                var tempFolder = Path.Combine(folderPath, "_temp_" + sanitizedTitle);
                Directory.CreateDirectory(tempFolder);

                AnsiConsole.MarkupLine($"[blue]Processing:[/] {trackTitle}");

                var basePath = trackSrc.Substring(0, trackSrc.LastIndexOf('/') + 1);
                var escapedTrackSrc = basePath + Uri.EscapeDataString(trackSrc.Substring(trackSrc.LastIndexOf('/') + 1));

                var m3u8Url = TokybookUrl + AudioBaseApiPath + escapedTrackSrc;
                var m3u8Content = await DownloadM3u8Playlist(audioBookId, streamToken, m3u8Url, escapedTrackSrc);

                if (string.IsNullOrEmpty(m3u8Content))
                {
                    AnsiConsole.MarkupLine($"[red]Failed to download playlist for {trackTitle}[/]");
                    return;
                }

                var tsSegments = ParseTsSegments(m3u8Content);
                if (tsSegments.Count == 0)
                {
                    AnsiConsole.MarkupLine($"[red]No segments found in playlist for {trackTitle}[/]");
                    return;
                }

                await DownloadTsSegments(audioBookId, streamToken, basePath, tsSegments, tempFolder);

                if (SettingsMenu.UserSettings.ConvertToMp3)
                {
                    var outputFile = Path.Combine(folderPath, sanitizedTitle + ".mp3");
                    await MergeTsSegments(tempFolder, tsSegments, outputFile, trackTitle, isMp3Conversion: true);
                }

                if  (SettingsMenu.UserSettings.ConvertToM4b)
                {
                    var outputFile = Path.Combine(folderPath, sanitizedTitle + ".m4b");
                    await MergeTsSegments(tempFolder, tsSegments, outputFile, trackTitle, isMp3Conversion: false);
                }

                Directory.Delete(tempFolder, true);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error downloading track {trackTitle}: {ex.Message}[/]");
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
                AnsiConsole.MarkupLine($"[red]Error downloading m3u8: {ex.Message}[/]");
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

        private static async Task DownloadTsSegments(string audioBookId, string streamToken, string basePath, List<string> segments, string outputFolder)
        {
            await AnsiConsole.Progress().StartAsync(async ctx =>
            {
                var task = ctx.AddTask($"[green]Downloading segments[/]", maxValue: segments.Count);

                for (int i = 0; i < segments.Count; i++)
                {
                    var segment = segments[i];
                    var segmentUrl = TokybookUrl + AudioBaseApiPath + basePath + segment;
                    var trackSrc = basePath + segment;

                    var response = await HttpUtil.GetTracksAsync(segmentUrl, audioBookId, streamToken, AudioBaseApiPath + trackSrc);
                    if (response.IsSuccessStatusCode)
                    {
                        var segmentPath = Path.Combine(outputFolder, $"{i:D4}_{segment}");
                        var bytes = await response.Content.ReadAsByteArrayAsync();
                        await File.WriteAllBytesAsync(segmentPath, bytes);
                    }

                    task.Increment(1);
                }
            });
        }

        private static async Task MergeTsSegments(string tempFolder, List<string> segments, string outputFile, string trackTitle, bool isMp3Conversion)
        {
            FFmpeg.SetExecutablesPath(SettingsMenu.UserSettings.FFmpegDirectory);

            var concatFile = Path.Combine(tempFolder, "concat.txt");
            var concatLines = new List<string>();

            for (int i = 0; i < segments.Count; i++)
            {
                var segmentPath = Path.Combine(tempFolder, $"{i:D4}_{segments[i]}");
                concatLines.Add($"file '{segmentPath}'");
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

            var extension = isMp3Conversion ? "mp3" : "m4b";
            await AnsiConsole.Progress().StartAsync(async ctx =>
            {
                var task = ctx.AddTask($"[green]Converting to {extension}:[/] {trackTitle}", maxValue: 100);
                conversion.OnProgress += (sender, progress) =>
                {
                    task.Value = progress.Percent;
                };
                await conversion.Start();
            });
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