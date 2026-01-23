namespace TokyBay.Models
{
    public abstract class AudiobookMetadata
    {
        public string Title { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
    }
}
