using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.Text.RegularExpressions;

namespace TokyBay
{
    public static class Downloader
    {
        private const string BaseUrl = "https://files01.tokybook.com/audio/";
        private const string MediaFallbackUrl = "https://files02.tokybook.com/audio/";
        private const string SkipChapter = "https://file.tokybook.com/upload/welcome-you-to-tokybook.mp3";
        private const string SlashReplaceString = " out of ";

        public static async Task GetInput(string? customDownloadFolder)
        {
            AnsiConsole.Clear();
            AnsiConsole.MarkupLine("[blue italic]Welcome to TokyBay[/]");
            while (true)
            {
                AnsiConsole.WriteLine();
                string url = AnsiConsole.Ask<string>("Enter URL:");
                if (!url.StartsWith("https://tokybook.com/"))
                {
                    AnsiConsole.MarkupLine("[red]Invalid URL! Try again.[/]");
                    continue;
                }

                await GetChapters(url, customDownloadFolder);
                break;
            }
        }

        public static async Task GetChapters(string bookUrl, string? customDownloadFolder)
        {
            string html = string.Empty;
            await AnsiConsole.Status()
                .SpinnerStyle(Style.Parse("blue bold"))
                .StartAsync("Preparing download...", async ctx =>
                {
                    html = (await HttpHelper.GetHtmlAsync(bookUrl));
                });

            HtmlDocument htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);

            var scripts = htmlDoc.DocumentNode.SelectNodes("//script");
            if (scripts == null)
            {
                AnsiConsole.MarkupLine("[red]No tracks found.[/]");
                AnsiConsole.MarkupLine("Press any key to continue");
                Console.ReadKey(true);
                return;
            }

            string jsonString = "";
            foreach (var script in scripts)
            {
                Match match = Regex.Match(script.InnerText, "tracks\\s*=\\s*(\\[.*?\\])", RegexOptions.Singleline);
                if (match.Success)
                {
                    jsonString = match.Groups[1].Value;
                    break;
                }
            }

            if (string.IsNullOrEmpty(jsonString))
            {
                AnsiConsole.MarkupLine("[red]No valid track information found.[/]");
                AnsiConsole.MarkupLine("Press any key to continue");
                Console.ReadKey(true);
                return;
            }

            JArray json = JArray.Parse(jsonString);
            List<(string name, string url)> chapters = new List<(string, string)>();
            foreach (var item in json)
            {
                string chapterName = item["name"]?.ToString() ?? "Unknown";
                string chapterUrl = item["chapter_link_dropbox"]?.ToString() ?? "";
                if (chapterUrl != SkipChapter)
                {
                    chapters.Add((chapterName, chapterUrl));
                }
            }

            string folderBase = customDownloadFolder ?? Directory.GetCurrentDirectory();
            Uri bookUri = new Uri(bookUrl);
            string folderPath = (bookUri.Segments != null && bookUri.Segments.Length > 0)
                ? Path.Combine(folderBase, bookUri.Segments[^1])
                : folderBase;
            Directory.CreateDirectory(folderPath);

            foreach (var chapter in chapters)
            {
                string fullUrl = chapter.url.StartsWith("http") ? chapter.url : BaseUrl + chapter.url;
                string fileName = chapter.name.Replace("/", SlashReplaceString) + ".mp3";
                await DownloadFile(fullUrl, folderPath, fileName);
            }

            AnsiConsole.MarkupLine("[green]Download finished to path[/]");
            AnsiConsole.MarkupLine($"[green]{folderPath}[/]");
            AnsiConsole.MarkupLine("Press any key to continue");
            Console.ReadKey(true);
        }

        public static async Task DownloadFile(string url, string folderPath, string fileName)
        {
            HttpResponseMessage response = await HttpHelper.GetHttpResponseAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                if (url.StartsWith(BaseUrl))
                {
                    string fallbackUrl = MediaFallbackUrl + url.Substring(BaseUrl.Length);
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

            string path = Path.Combine(folderPath, fileName);
            long? totalBytes = response.Content.Headers.ContentLength ?? 100;
            await AnsiConsole.Progress().StartAsync(async ctx =>
            {
                var task = ctx.AddTask($"[green]Downloading:[/] {fileName}", maxValue: totalBytes.Value);
                using (var responseStream = await response.Content.ReadAsStreamAsync())
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] buffer = new byte[8192];
                    int read;
                    while ((read = await responseStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fs.WriteAsync(buffer, 0, read);
                        task.Increment(read);
                    }
                }
            });
        }
    }
}
