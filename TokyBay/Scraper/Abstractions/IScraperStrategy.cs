namespace TokyBay.Scraper.Abstractions
{
    public interface IScraperStrategy
    {
        Task DownloadBookAsync(string bookUrl);
        bool CanHandle(string bookUrl);
    }
}