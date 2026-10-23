namespace DanbooruDownloader.Cli.Config
{
    // Stands in for the WinForms app's Properties.Settings.Default (app.config-backed).
    public class CliConfig
    {
        public int TimeoutMs { get; set; } = 30000;

        // Default for API requests. Danbooru (API and CDN) serves self-identifying clients but
        // sends a Cloudflare challenge to browser User-Agents that cannot run JavaScript, and
        // e621's API rules require a descriptive User-Agent. Do not add the original project's
        // URL: e621 stalls requests whose User-Agent links it (tested 2026-10).
        public const string ApiUserAgent = "DanbooruDownloader.Cli/1.0";

        // Cookie-login providers only: Cloudflare ties a solved challenge's cf_clearance
        // cookie to the User-Agent that solved it, so the cookie copied from a browser works
        // only with a browser User-Agent.
        public const string BrowserUserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36";

        public string AcceptLanguage { get; set; } = "en-US,en;q=0.9";
    }
}
