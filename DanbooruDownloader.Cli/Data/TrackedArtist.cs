using System;

namespace DanbooruDownloader.Cli.Data
{
    public class TrackedArtist
    {
        public int Id { get; set; }
        public string ProviderName { get; set; }
        public string Tag { get; set; }
        public string OutputDir { get; set; }
        public string FilenameFormat { get; set; }
        public string Rating { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastRefreshedAt { get; set; }
    }
}
