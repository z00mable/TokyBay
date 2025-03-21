using HtmlAgilityPack;
using Spectre.Console;
using System.Net;

namespace TokyBay
{
    public static class BookSearcher
    {
        public static async Task PromptSearchBook()
        {
            AnsiConsole.Clear();
            AnsiConsole.MarkupLine("[blue italic]Welcome to TokyBay[/]");
            AnsiConsole.WriteLine();
            string query = AnsiConsole.Ask<string>("Enter search query:");
            await SearchBook(query);
        }

        public static async Task SearchBook(string query)
        {
            int page = 1;
            List<string> allTitles = new List<string>();
            List<string> allUrls = new List<string>();

            while (true)
            {
                string url = $"https://tokybook.com/page/{page}/?s={Uri.EscapeDataString(query)}";
                string html = string.Empty;
                await AnsiConsole.Status()
                    .SpinnerStyle(Style.Parse("blue bold"))
                    .StartAsync("Searching...", async ctx =>
                    {
                        html = await HttpHelper.GetHtmlAsync(url);
                    });

                HtmlDocument htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(html);
                var titleNodes = htmlDoc.DocumentNode.SelectNodes("//h2[@class='entry-title']/a");
                bool hasNextPage = htmlDoc.DocumentNode.SelectSingleNode("//a[contains(@class, 'next page-numbers')]") != null;

                if (titleNodes != null)
                {
                    foreach (var node in titleNodes)
                    {
                        allTitles.Add(WebUtility.HtmlDecode(node.InnerText));
                        allUrls.Add(node.GetAttributeValue("href", ""));
                    }
                }

                List<string> menuOptions = new List<string>(allTitles);
                if (hasNextPage)
                {
                    menuOptions.Add("[green]Load more[/]");
                }

                menuOptions.Add("[red]Exit[/]");
                var selection = MenuHandler.DisplayMenu("Select a book:", menuOptions.ToArray());

                if (selection == "[red]Exit[/]")
                {
                    return;
                }
                else if (selection == "[green]Load more[/]")
                {
                    page++;
                    continue;
                }
                else
                {
                    int selectedIndex = allTitles.IndexOf(selection);
                    if (selectedIndex >= 0)
                    {
                        await Downloader.GetChapters(allUrls[selectedIndex], customDownloadFolder: null);
                        return;
                    }
                }
            }
        }
    }
}
