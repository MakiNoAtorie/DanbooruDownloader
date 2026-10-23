using DanbooruDownloader.Cli.Config;
using DanbooruDownloader.Cli.Entity;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DanbooruDownloader.Cli.Net
{
    /// <summary>
    /// HttpClient-based replacement for the WinForms app's ExtendedWebClient (a
    /// System.Net.WebClient subclass). Shares DanbooruProvider.CookieJar so cookie-based
    /// providers (LoginType.Cookie/CookieAlwaysAsk) work the same way as before.
    /// </summary>
    public class CliWebClient : IDisposable
    {
        private readonly HttpClient _client;

        public CliWebClient(CliConfig config)
        {
            var handler = new HttpClientHandler
            {
                CookieContainer = DanbooruProvider.CookieJar,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            };

            _client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMilliseconds(config.TimeoutMs),
            };
            _client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(config.AcceptLanguage);
        }

        // The User-Agent is per request: one client serves several providers in a batch, and
        // each needs its own (see DanbooruProvider.UserAgent).
        private static HttpRequestMessage CreateRequest(string url, string referer, string userAgent)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(userAgent ?? CliConfig.ApiUserAgent);
            if (!string.IsNullOrWhiteSpace(referer))
            {
                request.Headers.Referrer = new Uri(referer);
            }
            return request;
        }

        /// <summary>
        /// Fetch an API/listing response into memory. Only for small responses, not files.
        /// </summary>
        /// <param name="htmlIsData">For HTML-scraped boards, whose error pages (e.g. Shimmie's 404 for no results) are parsed too.</param>
        public async Task<Stream> GetStreamAsync(string url, string referer = null, string userAgent = null, bool htmlIsData = false)
        {
            using var request = CreateRequest(url, referer, userAgent);
            using var response = await _client.SendAsync(request, Program.Token);
            var body = await response.Content.ReadAsByteArrayAsync(Program.Token);
            // A non-2xx API body (Danbooru's JSON error, Cloudflare's challenge page) carries a
            // readable reason that DanbooruPostDao reports. Other pages (CAPTCHA, host down) do
            // not parse, so report the status and the page title instead.
            if (!response.IsSuccessStatusCode && !(htmlIsData && body.Length > 0) && !IsReadableError(body, response.Content.Headers.ContentType?.MediaType))
            {
                var title = Regex.Match(Encoding.UTF8.GetString(body), @"<title>\s*(.*?)\s*</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline).Groups[1].Value;
                var code = (int)response.StatusCode;
                // Cloudflare's 52x codes come without a reason phrase.
                var reason = string.IsNullOrWhiteSpace(response.ReasonPhrase) || response.ReasonPhrase == "<none>"
                    ? (code is >= 520 and <= 527 ? "the site's server is down or unreachable" : "")
                    : response.ReasonPhrase;
                // A titled page (CAPTCHA, block notice) explains itself; a bare 401/403 is usually a missing login.
                var hint = title.Length == 0 && response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? " - this provider may need a login: danboo login <provider>"
                    : "";
                throw new HttpRequestException($"HTTP {code} {reason}{(title.Length > 0 ? $" ({title})" : "")}{hint}".Replace("  ", " "), null, response.StatusCode);
            }
            return new MemoryStream(body);
        }

        private static bool IsReadableError(byte[] body, string mediaType)
        {
            if (body.Length == 0) return false;
            if (mediaType != null && (mediaType.Contains("json") || mediaType.Contains("xml"))) return true;
            var text = Encoding.UTF8.GetString(body);
            return text.Contains(Helper.CLOUDFLARE_STR) || text.Contains(Helper.DDOS_STR);
        }

        /// <returns>The number of bytes written.</returns>
        public async Task<long> DownloadFileAsync(string url, string destinationPath, string referer = null, string userAgent = null)
        {
            using var request = CreateRequest(url, referer, userAgent);

            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Program.Token);
            response.EnsureSuccessStatusCode();

            var tempPath = destinationPath + ".!tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? ".");

            try
            {
                long bytes;
                using (var fileStream = File.Create(tempPath))
                using (var responseStream = await response.Content.ReadAsStreamAsync(Program.Token))
                {
                    await responseStream.CopyToAsync(fileStream, Program.Token);
                    bytes = fileStream.Length;
                }
                File.Move(tempPath, destinationPath, overwrite: true);
                return bytes;
            }
            catch
            {
                File.Delete(tempPath);
                throw;
            }
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }
}
