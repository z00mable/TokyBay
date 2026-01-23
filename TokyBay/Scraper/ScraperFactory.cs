using TokyBay.Scraper.Abstractions;

namespace TokyBay.Scraper
{
    public class ScraperFactory(IEnumerable<IScraperStrategy> strategies)
    {
        private readonly IEnumerable<IScraperStrategy> _strategies = strategies ?? throw new ArgumentNullException(nameof(strategies));

        public IScraperStrategy? GetStrategy(string bookUrl)
        {
            if (string.IsNullOrWhiteSpace(bookUrl))
            {
                throw new ArgumentException("Book URL cannot be empty", nameof(bookUrl));
            }

            return _strategies.FirstOrDefault(s => s.CanHandle(bookUrl));
        }

        public IEnumerable<IScraperStrategy> GetAllStrategies()
        {
            return _strategies;
        }

        public bool CanHandleUrl(string bookUrl)
        {
            return GetStrategy(bookUrl) != null;
        }
    }
}
