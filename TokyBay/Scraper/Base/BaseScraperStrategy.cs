using Spectre.Console;
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
        IHttpService httpUtil,
        ISettingsService settingsService,
        ScraperConfig? config = null) : IScraperStrategy
    {
        protected readonly IAnsiConsole _console = console;
        protected readonly IHttpService _httpUtil = httpUtil;
        protected readonly UserSettings _settings = settingsService.GetSettings();
        protected readonly ScraperConfig _config = config ?? new ScraperConfig();

        public abstract Task DownloadBookAsync(string bookUrl);
        public abstract bool CanHandle(string bookUrl);

        [GeneratedRegex("[^A-Za-z0-9]+")]
        private static partial Regex SanitizeRegex();

        [GeneratedRegex(@"<h1[^>]*>(.*?)</h1>", RegexOptions.Singleline)]
        private static partial Regex H1Regex();

        protected static string SanitizeName(string fileName)
        {
            return SanitizeRegex().Replace(fileName, "_");
        }

        protected static string ExtractTitleFromH1(string html)
        {
            var match = H1Regex().Match(html);
            return match.Success
                ? System.Net.WebUtility.HtmlDecode(match.Groups[1].Value.Trim())
                : "Unknown Title";
        }

        protected void SetFFmpegPath() => FFmpeg.SetExecutablesPath(_settings.FFmpegDirectory);

        protected async Task ProcessDirectFilesInParallelAsync(SimpleAudiobookMetadata metadata, string folderPath)
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
                var trackTitle = Path.GetFileName(new Uri(chapterUrl.Split('?')[0]).LocalPath);

                await downloadSemaphore.WaitAsync();
                try
                {
                    await Task.Delay(100 * (index + 1));

                    var filePath = await DownloadDirectFileAsync(chapterUrl, trackTitle, folderPath);

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

        protected async Task<string> DownloadDirectFileAsync(string trackSrc, string trackTitle, string folderPath)
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

        protected async Task ConvertDirectFileTrackAsync(DirectFileTrackData track)
        {
            try
            {
                var ext = Path.GetExtension(track.FilePath).ToLowerInvariant();

                if (_settings.ConvertToMp3 && ext != ".mp3")
                {
                    var mp3Output = Path.ChangeExtension(track.FilePath, ".mp3");
                    await ConvertToFormatAsync(track.FilePath, mp3Output, "-c:a libmp3lame -b:a 128k");
                }

                if (_settings.ConvertToM4b && ext != ".m4b")
                {
                    var m4bOutput = Path.ChangeExtension(track.FilePath, ".m4b");
                    await ConvertToFormatAsync(track.FilePath, m4bOutput, "-c:a aac -b:a 64k");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Conversion failed: {ex.Message}", ex);
            }
        }

        protected async Task ConvertTrackToFormatsAsync(string inputFile, string outputFolder, string baseFileName)
        {
            SetFFmpegPath();

            if (_settings.ConvertToMp3)
            {
                var mp3Output = Path.Combine(outputFolder, $"{baseFileName}.mp3");
                await ConvertToFormatAsync(inputFile, mp3Output, "-c:a libmp3lame -b:a 128k");
            }

            if (_settings.ConvertToM4b)
            {
                var m4bOutput = Path.Combine(outputFolder, $"{baseFileName}.m4b");
                await ConvertToFormatAsync(inputFile, m4bOutput, "-c:a aac -b:a 64k");
            }
        }

        protected async Task MergeTsSegmentsAsync(
            string tempFolder,
            List<string> segments,
            string outputFile,
            string codecParams)
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

            IConversion conversion = FFmpeg.Conversions.New();
            conversion.AddParameter($"-f concat -safe 0 -i \"{concatFile}\"");
            conversion.AddParameter(codecParams);
            conversion.SetOutput(outputFile);
            await conversion.Start();
        }

        protected async Task ConvertToFormatAsync(string inputFile, string outputFile, string codecParams)
        {
            IConversion conversion = FFmpeg.Conversions.New();
            conversion.AddParameter($"-i \"{inputFile}\"");
            conversion.AddParameter(codecParams);
            conversion.SetOutput(outputFile);
            await conversion.Start();
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

        protected void ShowCompletionMessage()
        {
            _console.MarkupLine("[green]Download finished[/]");
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
