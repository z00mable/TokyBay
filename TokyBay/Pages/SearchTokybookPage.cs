using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.Text;

namespace TokyBay.Pages
{
    public static class SearchTokybookPage
    {
        private const string TokybookUrl = "https://tokybook.com";
        private const string SearchApiPath = "/api/v1/search";
        private const string PostDetailsApiPath = "/post/";

        public static async Task ShowAsync()
        {
            Program.CustomAnsiConsole.Clear();
            MenuHandler.ShowHeader();
            var (query, cancelled) = await MenuHandler.DisplayAskAsync<string>("Enter search query:");
            if (cancelled)
            {
                return;
            }

            await SearchBookAsync(query!);
        }

        public static async Task SearchBookAsync(string query)
        {
            var offset = 0;
            var limit = 12;
            var hasMoreHits = false;
            var allBookTitles = new List<string>();
            var allDynamicSlugIds = new List<string>();

            const string loadMoreSelection = "[green]Load more[/]";
            const string exitSelection = "[red]Exit[/]";

            while (true)
            {
                await Program.CustomAnsiConsole.Status()
                    .SpinnerStyle(Style.Parse("blue bold"))
                    .StartAsync("Searching...", async ctx =>
                    {
                        var searchResultsResponse = await GetSearchResultsAsync(query, offset, limit);
                        if (searchResultsResponse == null)
                        {
                            Program.CustomAnsiConsole.MarkupLine("[red]Searching failed.[/]");
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
                        var parseSucceeded = int.TryParse(totalHitsString, out int totalHits);
                        if (parseSucceeded && totalHits > offset + limit)
                        {
                            hasMoreHits = true;
                        }
                    });

                var menuOptions = new List<string>(allBookTitles);
                if (hasMoreHits)
                {
                    menuOptions.Add(loadMoreSelection);
                }

                menuOptions.Add(exitSelection);
                var selection = await MenuHandler.DisplayPromptAsync("Select a book:", menuOptions.ToArray());

                if (selection == null || selection == exitSelection)
                {
                    return;
                }
                else if (selection == loadMoreSelection)
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
                        await Scraper.Tokybook.DownloadBookAsync(TokybookUrl + PostDetailsApiPath + allDynamicSlugIds[selectedIndex]);
                        return;
                    }
                }
            }
        }

        private static async Task<JObject?> GetSearchResultsAsync(string query, int offset, int limit)
        {
            try
            {
                var userIdentity = await IpifyHandler.GetUserIdentityAsync();
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
                    Program.CustomAnsiConsole.MarkupLine($"[red]Search request failed: {response.StatusCode}[/]");
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                return JObject.Parse(json);
            }
            catch (Exception ex)
            {
                Program.CustomAnsiConsole.MarkupLine($"[red]Error getting search results: {ex.Message}[/]");
                return null;
            }
        }
    }
}
