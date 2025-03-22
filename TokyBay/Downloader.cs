using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.Text.RegularExpressions;
using Xabe.FFmpeg;

namespace TokyBay
{
    public static class Downloader
    {
        private const string BaseUrl = "https://files01.tokybook.com/audio/";
        private const string MediaFallbackUrl = "https://files02.tokybook.com/audio/";
        private const string SkipChapter = "https://file.tokybook.com/upload/welcome-you-to-tokybook.mp3";
        private const string SlashReplaceString = " out of ";

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
            var tracks = string.Empty;
            await AnsiConsole.Status()
                .SpinnerStyle(Style.Parse("blue bold"))
                .StartAsync("Preparing download...", async ctx =>
                {
                    tracks = await GetTracksFromHtml(bookUrl);
                });

            if (string.IsNullOrEmpty(tracks))
            {
                AnsiConsole.MarkupLine("[red]No valid track information found.[/]");
                AnsiConsole.MarkupLine("Press any key to continue");
                Console.ReadKey(true);
                return;
            }

            var bookUri = new Uri(bookUrl);
            var folderPath = (bookUri.Segments != null && bookUri.Segments.Length > 0)
                ? Path.Combine(SettingsMenu.UserSettings.DownloadPath, bookUri.Segments[^1])
                : SettingsMenu.UserSettings.DownloadPath;
            Directory.CreateDirectory(folderPath);

            var chapters = JArray.Parse(tracks);
            var fileNames = new List<string>();
            int index = 0;
            foreach (var chapter in chapters)
            {
                if (index > 2)
                {
                    break;
                }
                index++;

                var chapterUrl = chapter["chapter_link_dropbox"]?.ToString() ?? "";
                if (chapterUrl == SkipChapter)
                {
                    continue;
                }

                var chapterName = chapter["name"]?.ToString() ?? "Unknown";
                var fullUrl = chapterUrl.StartsWith("http") ? chapterUrl : BaseUrl + chapterUrl;
                var fileName = chapterName.Replace("/", SlashReplaceString) + ".mp3";
                fileNames.Add(fileName);
                await DownloadFile(fullUrl, folderPath, fileName);
            }
            
            if (SettingsMenu.UserSettings.ConvertMp3ToM4b)
            {
                foreach(var fileName in fileNames)
                {
                    await ConvertMp3FileToM4b(folderPath, fileName);
                    if (SettingsMenu.UserSettings.DeleteMp3AfterDownload)
                    {
                        await DeleteMp3File(folderPath, fileName);
                    }
                }
            }

            AnsiConsole.MarkupLine("[green]Download finished[/]");
            AnsiConsole.MarkupLine("Press any key to continue");
            Console.ReadKey(true);
        }

        public static async Task DownloadFile(string url, string folderPath, string fileName)
        {
            var response = await HttpHelper.GetHttpResponseAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                if (url.StartsWith(BaseUrl))
                {
                    var fallbackUrl = MediaFallbackUrl + url.Substring(BaseUrl.Length);
                    response = await HttpHelper.GetHttpResponseAsync(fallbackUrl);
                    if (!response.IsSuccessStatusCode)
                    {
                        AnsiConsole.MarkupLine($"[red]Failed to download {url} and fallback {fallbackUrl}[/]");
                        return;
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine($"[red]Failed to download {url}[/]");
                    return;
                }
            }

            var path = Path.Combine(folderPath, fileName);
            var totalBytes = response.Content.Headers.ContentLength ?? 100;
            await AnsiConsole.Progress().StartAsync(async ctx =>
            {
                var task = ctx.AddTask($"[green]Downloading:[/] {fileName}", maxValue: totalBytes);
                using (var responseStream = await response.Content.ReadAsStreamAsync())
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[8192];
                    int read;
                    while ((read = await responseStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fs.WriteAsync(buffer, 0, read);
                        task.Increment(read);
                    }
                }
            });
        }

        private static async Task<string> GetTracksFromHtml(string bookUrl)
        {
            var html = await HttpHelper.GetHtmlAsync(bookUrl);
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);
            var scripts = htmlDoc.DocumentNode.SelectNodes("//script");
            if (scripts == null)
            {
                return string.Empty;
            }

            var tracks = string.Empty;
            foreach (var script in scripts)
            {
                var match = Regex.Match(script.InnerText, "tracks\\s*=\\s*(\\[.*?\\])", RegexOptions.Singleline);
                if (match.Success)
                {
                    tracks = match.Groups[1].Value;
                    break;
                }
            }

            return tracks;
        }

        private static async Task ConvertMp3FileToM4b(string folderPath, string fileName)
        {
            var inputFile = Path.Combine(folderPath, fileName);
            var outputFile = inputFile.Replace(".mp3", ".m4b");

            FFmpeg.SetExecutablesPath(SettingsMenu.UserSettings.FFmpegDirectory);
            IConversion conversion = FFmpeg.Conversions.New();
            conversion.AddParameter($"-i \"{inputFile}\"");
            conversion.AddParameter("-c:a aac -b:a 64k");
            conversion.SetOutput(outputFile);

            await AnsiConsole.Progress().StartAsync(async ctx =>
            {
                var task = ctx.AddTask($"[green]Converting to m4b:[/] {fileName}", maxValue: 100);
                conversion.OnProgress += (sender, progress) =>
                {
                    task.Value = progress.Percent;
                };

                await conversion.Start();
            });
        }

        private static async Task DeleteMp3File(string folderPath, string fileName)
        {
            var filePath = Path.Combine(folderPath, fileName);
            if (File.Exists(filePath))
            {
                await Task.Run(() => File.Delete(filePath));
                AnsiConsole.MarkupLine($"[green]File deleted:[/] {fileName}");
            }
        }
    }
}
