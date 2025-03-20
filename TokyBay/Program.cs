using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using Spectre.Console;

class Program
{
    private static readonly HttpClient httpClient = new HttpClient();
    private const string BaseUrl = "https://files01.tokybook.com/audio/";
    private const string MediaFallbackUrl = "https://files02.tokybook.com/audio/";
    private const string SkipChapter = "https://file.tokybook.com/upload/welcome-you-to-tokybook.mp3";
    private const string SlashReplaceString = " out of ";
    private static string customDownloadFolder = null;

    private static Dictionary<int, (List<string> displayTitles, List<string> urls)> searchCache 
        = new Dictionary<int, (List<string>, List<string>)>();

    static async Task Main(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "-d" || args[i] == "--directory") && i + 1 < args.Length)
            {
                customDownloadFolder = args[i + 1];
            }
        }

        string[] options = { "Search book", "Download from URL", "Exit" };
        while (true)
        {
            var selected = DisplayMenu("Choose action:", options);
            switch (selected)
            {
                case "Search book":
                    await PromptSearchBook();
                    break;
                case "Download from URL":
                    await GetInput();
                    break;
                case "Exit":
                    return;
            }

        }
    }

    static string DisplayMenu(string prompt, string[] options)
    {
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine("[blue italic]Welcome to TokyBay[/]");
        AnsiConsole.WriteLine();
        var selection = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"[grey]{prompt}[/]")
                .AddChoices(options)
        );
        return selection;
    }

    static async Task PromptSearchBook()
    {
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine("[blue italic]Welcome to TokyBay[/]");
        AnsiConsole.WriteLine();
        string query = AnsiConsole.Ask<string>("Enter search query:");
        searchCache.Clear();
        await SearchBook(query, 1);
    }

    static async Task SearchBook(string query, int page)
    {
        List<string> displayTitles;
        List<string> urls;

        if (searchCache.ContainsKey(page))
        {
            (displayTitles, urls) = searchCache[page];
        }
        else
        {
            string url = $"https://tokybook.com/page/{page}/?s={Uri.EscapeDataString(query)}";
            string html = string.Empty;

            await AnsiConsole.Status()
                .StartAsync("Searching...", async ctx =>
                {
                    html = await httpClient.GetStringAsync(url);
                });

            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);

            var titleNodes = htmlDoc.DocumentNode.SelectNodes("//h2[@class='entry-title']/a");
            displayTitles = new List<string>();
            urls = new List<string>();

            if (titleNodes != null)
            {
                foreach (var node in titleNodes)
                {
                    displayTitles.Add(WebUtility.HtmlDecode(node.InnerText));
                    urls.Add(node.GetAttributeValue("href", ""));
                }
            }

            var nextPageNode = htmlDoc.DocumentNode.SelectSingleNode("//a[contains(@class, 'next page-numbers')]");
            if (nextPageNode != null)
            {
                displayTitles.Add("[green]Next page[/]");
                urls.Add("next");
            }
            if (page > 1)
            {
                displayTitles.Add("[green]Previous page[/]");
                urls.Add("previous");
            }
            displayTitles.Add("[red]Exit[/]");
            urls.Add("exit");

            searchCache[page] = (displayTitles, urls);
        }

        var selection = DisplayMenu("Select a book:", displayTitles.ToArray());
        if (selection == "[red]Exit[/]")
            return;
        else if (selection == "[green]Next page[/]")
            await SearchBook(query, page + 1);
        else if (selection == "[green]Previous page[/]")
            await SearchBook(query, page - 1);
        else
        {
            int selectedIndex = displayTitles.IndexOf(selection);
            await GetChapters(urls[selectedIndex]);
        }
    }

    static async Task GetInput()
    {
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine("[blue italic]Welcome to TokyBay[/]");
        AnsiConsole.WriteLine();
        string url = AnsiConsole.Ask<string>("Enter URL:");
        if (!url.StartsWith("https://tokybook.com/"))
        {
            AnsiConsole.MarkupLine("[red]Invalid URL![/]");
            AnsiConsole.Console.Input.ReadKey(false);
            return;
        }
        await GetChapters(url);
    }

    static async Task GetChapters(string bookUrl)
    {
        string html = string.Empty;
        await AnsiConsole.Status()
                .StartAsync("Preparing download...", async ctx =>
                {
                    html = await httpClient.GetStringAsync(bookUrl);
                });

        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(html);

        var scripts = htmlDoc.DocumentNode.SelectNodes("//script");
        if (scripts == null)
        {
            AnsiConsole.MarkupLine("[red]No tracks found.[/]");
            AnsiConsole.Ask<string>("Press Enter to continue");
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
            AnsiConsole.Ask<string>("Press Enter to continue");
            return;
        }

        JArray json = JArray.Parse(jsonString);
        List<(string name, string url)> chapters = new List<(string, string)>();

        foreach (var item in json)
        {
            string chapterName = item["name"]?.ToString() ?? "Unknown";
            string chapterUrl = item["chapter_link_dropbox"]?.ToString() ?? "";
            if (chapterUrl != SkipChapter)
                chapters.Add((chapterName, chapterUrl));
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
        AnsiConsole.Ask<string>("Press Enter to continue");
    }

    static async Task DownloadFile(string url, string folderPath, string fileName)
    {
        var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            if (url.StartsWith(BaseUrl))
            {
                string fallbackUrl = MediaFallbackUrl + url.Substring(BaseUrl.Length);
                response = await httpClient.GetAsync(fallbackUrl, HttpCompletionOption.ResponseHeadersRead);
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
        long? totalBytes = response.Content.Headers.ContentLength ?? 100;
        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
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
