using TokyBay.Services;

namespace TokyBay.Pages
{
    public class MainPage(
        IPageService pageHandler,
        SearchTokybookPage searchPage,
        DownloadPage downloadPage,
        SettingsPage settingsPage)
    {
        private readonly IPageService _pageService = pageHandler;
        private readonly SearchTokybookPage _searchPage = searchPage;
        private readonly DownloadPage _downloadPage = downloadPage;
        private readonly SettingsPage _settingsPage = settingsPage;

        public async Task ShowAsync()
        {
            const string search = "Search book on Tokybook.com";
            const string download = "Download from URL";
            const string settings = "Settings";
            const string exit = "Exit";

            string[] options = { search, download, settings, exit };

            while (true)
            {
                var (selection, cancelled) = await _pageService.DisplayPromptAsync("Choose action:", options);
                if (cancelled || string.IsNullOrWhiteSpace(selection))
                {
                    return;
                }

                switch (selection)
                {
                    case search:
                        await _searchPage.ShowAsync();
                        break;
                    case download:
                        await _downloadPage.ShowAsync();
                        break;
                    case settings:
                        await _settingsPage.ShowAsync();
                        break;
                    case exit:
                        return;
                }
            }
        }
    }
}