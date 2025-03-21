using TokyBay;

class Program
{
    static async Task Main(string[] args)
    {
        string? customDownloadFolder = null;
        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "-d" || args[i] == "--directory") && i + 1 < args.Length)
            {
                customDownloadFolder = args[i + 1];
            }
        }

        Settings.DownloadPath = customDownloadFolder ?? Directory.GetCurrentDirectory();
        await MenuHandler.ShowMainMenu();
    }
}