namespace DanbooruDownloader.Cli.Config
{
    // Stands in for the WinForms app's Properties.Settings.Default (app.config-backed).
    // No file-based loading yet (Phase 3) - just the defaults needed for search/download.
    public class CliConfig
    {
        public int TimeoutMs { get; set; } = 30000;
        public string UserAgent { get; set; } = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) DanbooruDownloader.Cli/0.1";
        public string AcceptLanguage { get; set; } = "en-US,en;q=0.9";
    }
}
