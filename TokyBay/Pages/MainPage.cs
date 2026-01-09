namespace TokyBay.Pages
{
    public class MainPage
    {
        public static async Task ShowAsync()
        {
            const string search = "Search book on Tokybook.com";
            const string download = "Download from URL";
            const string settings = "Settings";
            const string exit = "Exit";

            string[] options = { search, download, settings, exit };

            while (true)
            {
                var selection = await MenuHandler.DisplayPromptAsync("Choose action:", options);
                if (selection == null)
                {
                    return;
                }

                switch (selection)
                {
                    case search:
                        await SearchTokybookPage.ShowAsync();
                        break;
                    case download:
                        await DownloadPage.ShowAsync();
                        break;
                    case settings:
                        await SettingsPage.ShowAsync();
                        break;
                    case exit:
                        return;
                }
            }
        }
    }
}
