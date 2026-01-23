using TokyBay.Scraper;

namespace TokyBay.Services
{
    public class DownloadService(ScraperFactory scraperFactory)
    {
        private readonly ScraperFactory _scraperFactory = scraperFactory ?? throw new ArgumentNullException(nameof(scraperFactory));

        public async Task<bool> DownloadAsync(string bookUrl)
        {
            var strategy = _scraperFactory.GetStrategy(bookUrl);

            if (strategy == null)
            {
                throw new NotSupportedException($"No scraper strategy found for URL: {bookUrl}");
            }

            try
            {
                await strategy.DownloadBookAsync(bookUrl);
                return true;
            }
            catch (Exception ex)
            {
                throw new Exception($"Download failed using {strategy.GetType().Name}: {ex.Message}", ex);
            }
        }

        public IEnumerable<string> GetSupportedDomains()
        {
            return new[]
            {
                "tokybook.com",
                "freeaudiobooks.top",
                "zaudiobooks.com"
            };
        }
    }
}