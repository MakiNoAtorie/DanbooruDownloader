namespace DanbooruDownloader.Cli.Config
{
    // Stands in for the WinForms app's Properties.Settings.Default (app.config-backed).
    // No file-based loading yet (Phase 3) - just the defaults needed for search/download.
    public class CliConfig
    {
        public int TimeoutMs { get; set; } = 30000;
        // A real browser UA, not a self-identifying one - Cloudflare (and most bot-detection)
        // ties a solved challenge's cf_clearance cookie to the exact User-Agent that solved
        // it, so announcing "DanbooruDownloader.Cli/0.1" here guaranteed a mismatch and a
        // re-challenge even with a valid cookie copied from a real browser session.
        public string UserAgent { get; set; } = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36";
        public string AcceptLanguage { get; set; } = "en-US,en;q=0.9";
    }
}
