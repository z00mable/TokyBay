using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Newtonsoft.Json.Linq;

class Program
{
    private static readonly HttpClient httpClient = new HttpClient();
    private const string BaseUrl = "https://files01.tokybook.com/audio/";
    private const string MediaFallbackUrl = "https://files02.tokybook.com/audio/";
    private const string SkipChapter = "https://file.tokybook.com/upload/welcome-you-to-tokybook.mp3";
    private const string SlashReplaceString = " out of ";
    private static string customDownloadFolder = null;

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
            int selected = DisplayMenu("\x1B[4mChoose action:\x1B[0m", options);
            switch (selected)
            {
                case 0:
                    await SearchBook();
                    break;
                case 1:
                    await GetInput();
                    break;
                case 2:
                    return;
            }
        }
    }

    static int DisplayMenu(string prompt, string[] options)
    {
        Console.CursorVisible = false;
        int currentSelection = 0;
        ConsoleKey key;
        do
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("\x1b[3mWelcome to TokyBay\x1b[0m");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine(prompt);
            Console.WriteLine();
            for (int i = 0; i < options.Length; i++)
            {
                if (i == currentSelection)
                {
                    Console.BackgroundColor = ConsoleColor.Gray;
                    Console.ForegroundColor = ConsoleColor.Black;
                }
                Console.WriteLine(options[i]);
                Console.ResetColor();
            }
            ConsoleKeyInfo keyInfo = Console.ReadKey(true);
            key = keyInfo.Key;
            if (key == ConsoleKey.UpArrow)
                currentSelection = (currentSelection == 0) ? options.Length - 1 : currentSelection - 1;
            else if (key == ConsoleKey.DownArrow)
                currentSelection = (currentSelection + 1) % options.Length;
        } while (key != ConsoleKey.Enter);

        Console.CursorVisible = false;
        return currentSelection;
    }

    static async Task SearchBook()
    {
        Console.Clear();
        Console.Write("Enter search query: ");
        string query = Console.ReadLine();
        string url = $"https://tokybook.com/?s={Uri.EscapeDataString(query)}";

        string html = await httpClient.GetStringAsync(url);
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(html);

        var titles = htmlDoc.DocumentNode.SelectNodes("//h2[@class='entry-title']/a");
        if (titles == null)
        {
            Console.WriteLine("No results found.");
            Console.ReadKey();
            return;
        }

        List<string> urls = new List<string>();
        List<string> displayTitles = new List<string>();
        foreach (var title in titles)
        {
            displayTitles.Add(WebUtility.HtmlDecode(title.InnerText));
            urls.Add(title.GetAttributeValue("href", ""));
        }

        displayTitles.Add("Exit");
        int selected = DisplayMenu("Select a book:", displayTitles.ToArray());
        if (selected == displayTitles.Count - 1) 
        { 
            return; 
        }

        await GetChapters(urls[selected]);
    }

    static async Task GetInput()
    {
        Console.Clear();
        Console.Write("Enter URL: ");
        string url = Console.ReadLine();
        if (!url.StartsWith("https://tokybook.com/"))
        {
            Console.WriteLine("Invalid URL!");
            Console.ReadKey();
            return;
        }
        await GetChapters(url);
    }

    static async Task GetChapters(string bookUrl)
    {
        string html = await httpClient.GetStringAsync(bookUrl);
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(html);

        var scripts = htmlDoc.DocumentNode.SelectNodes("//script");
        if (scripts == null)
        {
            Console.WriteLine("No tracks found.");
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
            Console.WriteLine("No valid track information found.");
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
        string folderPath = bookUri.Segments != null && bookUri.Segments.Length > 0
            ? Path.Combine(folderBase, bookUri.Segments[^1])
            : folderBase;
        Directory.CreateDirectory(folderPath);

        foreach (var chapter in chapters)
        {
            string fullUrl = chapter.url.StartsWith("http") ? chapter.url : BaseUrl + chapter.url;
            string fileName = chapter.name.Replace("/", SlashReplaceString) + ".mp3";
            await DownloadFile(fullUrl, Path.Combine(folderPath, fileName));
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Download finished");
        Console.ResetColor();
    }

    static async Task DownloadFile(string url, string filename)
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
                    Console.WriteLine($"Failed to download {url} and fallback {fallbackUrl}");
                    return;
                }
            }
            else
            {
                Console.WriteLine($"Failed to download {url}");
                return;
            }
        }

        long? totalBytes = response.Content.Headers.ContentLength;
        Console.WriteLine($"\rDownloading: {filename}");
        using (var responseStream = await response.Content.ReadAsStreamAsync())
        using (var fs = new FileStream(filename, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            byte[] buffer = new byte[8192];
            long totalRead = 0;
            int read;
            while ((read = await responseStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fs.WriteAsync(buffer, 0, read);
                totalRead += read;
                if (totalBytes.HasValue)
                {
                    int percent = (int)((totalRead * 100) / totalBytes.Value);
                    Console.Write($"\rProgress: {percent}%");
                }
            }
        }
    }
}
