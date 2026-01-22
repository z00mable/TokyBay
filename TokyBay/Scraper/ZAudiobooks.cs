using Spectre.Console;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using TokyBay.Models;
using TokyBay.Services;
using Xabe.FFmpeg;

namespace TokyBay.Scraper
{
    public class ZAudiobooks(
        IAnsiConsole console,
        IHttpService httpUtil,
        ISettingsService settingsService)
    {
        private const string BaseUrl = "https://files01.freeaudiobooks.top/audio/";

        private const int MaxParallelDownloads = 3;
        private const int MaxParallelConversions = 2;

        private readonly IAnsiConsole _console = console;
        private readonly IHttpService _httpUtil = httpUtil;
        private readonly UserSettings _settings = settingsService.GetSettings();

        public async Task DownloadBookAsync(string bookUrl)
        {
            AudiobookData? audiobookData = null;
            string bookTitle = string.Empty;

            await _console.Status()
                .SpinnerStyle(Style.Parse("blue bold"))
                .StartAsync("Preparing download...", async ctx =>
                {
                    ctx.Status("Getting title and chapters...");
                    audiobookData = await GetChapterUrlsAsync(bookUrl);
                });

            if (audiobookData == null || audiobookData.ChapterUrls.Count == 0)
            {
                _console.MarkupLine("[red]No valid tracks found.[/]");
                _console.MarkupLine("Press any key to continue");
                Console.ReadKey(true);
                return;
            }

            var folderPath = Path.Combine(_settings.DownloadPath, SanitizeName(audiobookData.Title));
            Directory.CreateDirectory(folderPath);

            _console.MarkupLine($"[green]Found {audiobookData.ChapterUrls.Count} tracks[/]");
            _console.MarkupLine($"[blue]Parallel downloads:[/] {MaxParallelDownloads}");
            _console.MarkupLine($"[blue]Parallel conversions:[/] {MaxParallelConversions}");

            await ProcessTracksInParallelAsync(audiobookData.ChapterUrls, folderPath);

            _console.MarkupLine("[green]Download finished[/]");
            _console.MarkupLine("Press any key to continue");
            Console.ReadKey(true);
            return;
        }

        private async Task ProcessTracksInParallelAsync(List<string> chaptersUrls, string folderPath)
        {
            var conversionChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(10)
            {
                FullMode = BoundedChannelFullMode.Wait
            });

            var downloadSemaphore = new SemaphoreSlim(MaxParallelDownloads);
            var completedDownloads = 0;
            var completedConversions = 0;
            var totalTracks = chaptersUrls.Count;
            var lockObj = new object();

            var downloadTasks = new List<Task>();
            for (int i = 0; i < chaptersUrls.Count; i++)
            {
                var src = chaptersUrls[i];
                var trackNumber = i + 1;
                if (string.IsNullOrEmpty(src))
                {
                    continue;
                }

                var trackTitle = src.Split('/').Last();

                downloadTasks.Add(Task.Run(async () =>
                {
                    await downloadSemaphore.WaitAsync();
                    try
                    {
                        await Task.Delay(100 * trackNumber);

                        var filePath = await DownloadTrackAsync(src, trackTitle, folderPath, trackNumber, totalTracks);
                        if (!string.IsNullOrEmpty(filePath))
                        {
                            await conversionChannel.Writer.WriteAsync(filePath);

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
                            _console.MarkupLine($"[red]Download error for {trackTitle}: {ex.Message}[/]");
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
                    await foreach (var filePath in conversionChannel.Reader.ReadAllAsync())
                    {
                        try
                        {
                            await ConvertTrackAsync(filePath);

                            lock (lockObj)
                            {
                                completedConversions++;
                                _console.MarkupLine($"[cyan]Converted:[/] {completedConversions}/{totalTracks} - {filePath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            lock (lockObj)
                            {
                                _console.MarkupLine($"[red]Conversion error for {filePath}: {ex.Message}[/]");
                            }
                        }
                    }
                }));
            }

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

        private async Task ConvertTrackAsync(string filePath)
        {
            try
            {
                FFmpeg.SetExecutablesPath(_settings.FFmpegDirectory);
                IConversion conversion = FFmpeg.Conversions.New();
                conversion.AddParameter($"-i \"{filePath}\"");
                if (_settings.ConvertToMp3)
                {
                    if (await IsValidMp3Async(filePath) == false)
                    {
                        var outputFile = Path.Combine(filePath + ".mp3");
                        conversion.AddParameter("-c:a libmp3lame -b:a 128k");
                        conversion.SetOutput(outputFile);
                    }
                }

                if (_settings.ConvertToM4b)
                {
                    var filePathWithoutExtension = string.Join('.', filePath.Split('.').SkipLast(1));
                    var outputFile = Path.Combine(filePathWithoutExtension + ".m4b");
                    conversion.AddParameter("-c:a aac -b:a 64k");
                    conversion.SetOutput(outputFile);
                }

                await conversion.Start();
            }
            catch (Exception ex)
            {
                throw new Exception($"Conversion failed: {ex.Message}", ex);
            }
        }

        private async Task<AudiobookData?> GetChapterUrlsAsync(string bookUrl)
        {
            var response = await _httpUtil.GetAsync(bookUrl);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync();
            var lines = html.Split('\n');
            var startIndex = Array.FindIndex(lines, line => line.Contains("tracks = ["));
            var chapterUrls = new List<string>();

            if (startIndex == -1)
            {
                return null;
            }

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

            return new AudiobookData
            {
                Title = title,
                ChapterUrls = chapterUrls
            };
        }

        private async Task<bool> IsValidMp3Async(string filePath)
        {
            try
            {
                var mediaInfo = await FFmpeg.GetMediaInfo(filePath);

                var audioStream = mediaInfo.AudioStreams.FirstOrDefault();
                if (audioStream != null)
                {
                    return audioStream.Codec.Contains("mp3", StringComparison.CurrentCultureIgnoreCase);
                }

                return false;
            }
            catch
            {
                return false;
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

        private static string SanitizeName(string fileName)
        {
            return Regex.Replace(fileName, "[^A-Za-z0-9]+", "_");
        }
    }
}
