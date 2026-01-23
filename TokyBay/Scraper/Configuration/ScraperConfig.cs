namespace TokyBay.Scraper.Configuration
{
    public class ScraperConfig
    {
        public int MaxParallelDownloads { get; set; } = 3;
        public int MaxParallelConversions { get; set; } = 2;
        public int MaxSegmentsPerTrack { get; set; } = 5;
        public int RetryAttempts { get; set; } = 3;
        public int RetryDelayMs { get; set; } = 1000;
    }
}
