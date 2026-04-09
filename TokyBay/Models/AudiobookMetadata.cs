namespace TokyBay.Models
{
    public abstract class AudiobookMetadata
    {
        public string Title { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Narrator { get; set; } = string.Empty;
        public string CoverArtUrl { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Publisher { get; set; } = string.Empty;
        public string Year { get; set; } = string.Empty;
    }
}
