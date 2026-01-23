using Spectre.Console;
using System.Text.RegularExpressions;
using TokyBay.Models;
using TokyBay.Scraper.Abstractions;
using TokyBay.Scraper.Configuration;
using TokyBay.Services;
using Xabe.FFmpeg;

namespace TokyBay.Scraper.Base
{
    public abstract class BaseScraperStrategy(
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

        protected static string SanitizeName(string fileName)
        {
            return Regex.Replace(fileName, "[^A-Za-z0-9]+", "_");
        }

        protected async Task ConvertTrackToFormatsAsync(string inputFile, string outputFolder, string baseFileName)
        {
            FFmpeg.SetExecutablesPath(_settings.FFmpegDirectory);

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
            FFmpeg.SetExecutablesPath(_settings.FFmpegDirectory);
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
            conversion.AddParameter(codecParams);
            conversion.SetOutput(outputFile);
            await conversion.Start();
        }

        protected async Task ConvertToFormatAsync(string inputFile, string outputFile, string codecParams)
        {
            FFmpeg.SetExecutablesPath(_settings.FFmpegDirectory);
            IConversion conversion = FFmpeg.Conversions.New();
            conversion.AddParameter($"-i \"{inputFile}\"");
            conversion.AddParameter(codecParams);
            conversion.SetOutput(outputFile);
            await conversion.Start();
        }

        protected async Task<bool> IsValidMp3Async(string filePath)
        {
            try
            {
                var mediaInfo = await FFmpeg.GetMediaInfo(filePath);
                var audioStream = mediaInfo.AudioStreams.FirstOrDefault();
                return audioStream?.Codec.Contains("mp3", StringComparison.CurrentCultureIgnoreCase) ?? false;
            }
            catch
            {
                return false;
            }
        }

        protected async Task<T?> RetryAsync<T>(Func<Task<T>> action, int maxRetries = 3, int delayMs = 1000)
        {
            for (int retry = 0; retry < maxRetries; retry++)
            {
                try
                {
                    return await action();
                }
                catch
                {
                    if (retry < maxRetries - 1)
                    {
                        await Task.Delay(delayMs * (retry + 1));
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            return default;
        }

        protected void SafeDeleteDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                try
                {
                    Directory.Delete(path, true);
                }
                catch (Exception ex)
                {
                    _console.MarkupLine($"[yellow]Warning: Could not delete directory {path}: {ex.Message}[/]");
                }
            }
        }

        protected void LogProgress(string message, ConsoleColor color = ConsoleColor.Green)
        {
            var markup = color switch
            {
                ConsoleColor.Red => "red",
                ConsoleColor.Yellow => "yellow",
                ConsoleColor.Blue => "blue",
                ConsoleColor.Cyan => "cyan",
                _ => "green"
            };
            _console.MarkupLine($"[{markup}]{message}[/]");
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
            Console.ReadKey(true);
        }

        protected void ShowErrorMessage(string message)
        {
            _console.MarkupLine($"[red]{message}[/]");
            _console.MarkupLine("Press any key to continue");
            Console.ReadKey(true);
        }
    }
}