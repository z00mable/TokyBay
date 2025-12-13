using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.Text;

namespace TokyBay
{
    public static class BookSearcher
    {
        private const string TokybookUrl = "https://tokybook.com";
        private const string SearchApiPath = "/api/v1/search";
        private const string PostDetailsApiPath = "/post/";

        public static async Task PromptSearchBook()
        {
            AnsiConsole.Clear();
            Constants.ShowHeader();
            var query = AnsiConsole.Ask<string>("Enter search query:");
            await SearchBook(query);
        }

        public static async Task SearchBook(string query)
        {
            var offset = 0;
            var limit = 12;
            var hasMoreHits = false;
            var allBookTitles = new List<string>();
            var allDynamicSlugIds = new List<string>();

            while (true)
            {
                var url = $"https://tokybook.com/search?q={Uri.EscapeDataString(query)}";
                var html = string.Empty;
                await AnsiConsole.Status()
                    .SpinnerStyle(Style.Parse("blue bold"))
                    .StartAsync("Searching...", async ctx =>
                    {
                        var searchResultsResponse = await GetSearchResults(query, offset, limit);
                        if (searchResultsResponse == null)
                        {
                            AnsiConsole.MarkupLine("[red]Searching failed.[/]");
                            return;
                        }

                        var searchResults = searchResultsResponse["content"] as JArray;
                        if (searchResults != null)
                        {
                            foreach (var item in searchResults)
                            {
                                var title = item["title"]?.ToString();
                                var dynamicSlugId = item["dynamicSlugId"]?.ToString();

                                if (!string.IsNullOrEmpty(title))
                                {
                                    allBookTitles.Add(title);
                                }

                                if (!string.IsNullOrEmpty(dynamicSlugId))
                                {
                                    allDynamicSlugIds.Add(dynamicSlugId);
                                }
                            }
                        }

                        var totalHitsString = searchResultsResponse["totalHits"]?.ToString() ?? string.Empty;
                        int.TryParse(totalHitsString, out int totalHits);
                        if (totalHits > offset + limit)
                        {
                            hasMoreHits = true;
                        }
                    });

                var menuOptions = new List<string>(allBookTitles);
                if (hasMoreHits)
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
                    hasMoreHits = false;
                    offset += limit;
                    continue;
                }
                else
                {
                    var selectedIndex = allBookTitles.IndexOf(selection);
                    if (selectedIndex >= 0)
                    {
                        await Downloader.GetChapters(TokybookUrl + PostDetailsApiPath + allDynamicSlugIds[selectedIndex]);
                        return;
                    }
                }
            }
        }

        private static async Task<JObject?> GetSearchResults(string query, int offset, int limit)
        {
            try
            {
                var userIdentity = await TokybookApiHandler.GetUserIdentity();
                var payload = new JObject
                {
                    ["limit"] = limit,
                    ["offset"] = offset,
                    ["query"] = query,
                    ["userIdentity"] = userIdentity
                };

                var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
                var response = await HttpUtil.PostAsync(TokybookUrl + SearchApiPath, content);

                if (!response.IsSuccessStatusCode)
                {
                    AnsiConsole.MarkupLine($"[red]Search request failed: {response.StatusCode}[/]");
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                return JObject.Parse(json);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error getting search results: {ex.Message}[/]");
                return null;
            }
        }
    }
}
