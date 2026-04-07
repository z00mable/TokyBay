using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.Text;
using TokyBay.Services;

namespace TokyBay.Pages
{
    public class SearchTokybookPage(
        IAnsiConsole console,
        IPageService pageService,
        IHttpService httpService,
        IIpifyService ipifyService,
        DownloadService downloadService)
    {
        private const string TokybookUrl = "https://tokybook.com";
        private const string SearchApiPath = "/api/v1/search";
        private const string PostDetailsApiPath = "/post/";

        private readonly IAnsiConsole _console = console;
        private readonly IPageService _pageService = pageService;
        private readonly IHttpService _httpService = httpService;
        private readonly IIpifyService _ipifyService = ipifyService;
        private readonly DownloadService _downloadService = downloadService;

        public async Task ShowAsync()
        {
            _console.Clear();
            _pageService.DisplayHeader();

            var (query, cancelled) = await _pageService.DisplayAskAsync<string>("Enter search query:");
            if (cancelled || string.IsNullOrWhiteSpace(query))
            {
                return;
            }

            await SearchBookAsync(query);
        }

        private async Task SearchBookAsync(string query)
        {
            var offset = 0;
            var limit = 12;
            var hasMoreHits = false;
            var allBooks = new List<(string Title, string SlugId)>();

            const string loadMoreSelection = "[green]Load more[/]";
            const string exitSelection = "[red]Exit[/]";

            while (true)
            {
                await _console.Status()
                    .SpinnerStyle(Style.Parse("blue bold"))
                    .StartAsync("Searching...", async ctx =>
                    {
                        var searchResultsResponse = await GetSearchResultsAsync(query, offset, limit);
                        if (searchResultsResponse == null)
                        {
                            _console.MarkupLine("[red]Searching failed.[/]");
                            return;
                        }

                        var searchResults = searchResultsResponse["content"] as JArray;
                        if (searchResults != null)
                        {
                            foreach (var item in searchResults)
                            {
                                var title = item["title"]?.ToString();
                                var dynamicSlugId = item["dynamicSlugId"]?.ToString();

                                if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(dynamicSlugId))
                                {
                                    allBooks.Add((title, dynamicSlugId));
                                }
                            }
                        }

                        var totalHitsString = searchResultsResponse["totalHits"]?.ToString() ?? string.Empty;
                        if (int.TryParse(totalHitsString, out int totalHits) && totalHits > offset + limit)
                        {
                            hasMoreHits = true;
                        }
                    });

                var menuOptions = allBooks.Select(b => b.Title).ToList();
                if (hasMoreHits)
                {
                    menuOptions.Add(loadMoreSelection);
                }
                menuOptions.Add(exitSelection);

                var (selection, cancelled) = await _pageService.DisplayPromptAsync("Select a book:", menuOptions.ToArray());
                if (cancelled || string.IsNullOrWhiteSpace(selection) || selection == exitSelection)
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
                    var selectedBook = allBooks.FirstOrDefault(b => b.Title == selection);
                    if (selectedBook != default)
                    {
                        var bookUrl = TokybookUrl + PostDetailsApiPath + selectedBook.SlugId;
                        await _downloadService.DownloadAsync(bookUrl);
                        return;
                    }
                }
            }
        }

        private async Task<JObject?> GetSearchResultsAsync(string query, int offset, int limit)
        {
            try
            {
                var userIdentity = await _ipifyService.GetUserIdentityAsync();
                var payload = new JObject
                {
                    ["limit"] = limit,
                    ["offset"] = offset,
                    ["query"] = query,
                    ["userIdentity"] = userIdentity
                };

                var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
                var response = await _httpService.PostAsync(TokybookUrl + SearchApiPath, content);

                if (!response.IsSuccessStatusCode)
                {
                    _console.MarkupLine($"[red]Search request failed: {response.StatusCode}[/]");
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                return JObject.Parse(json);
            }
            catch (Exception ex)
            {
                _console.MarkupLine($"[red]Error getting search results: {ex.Message}[/]");
                return null;
            }
        }
    }
}
