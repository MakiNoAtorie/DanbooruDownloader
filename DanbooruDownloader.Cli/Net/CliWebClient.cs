using DanbooruDownloader.Cli.Config;
using DanbooruDownloader.Cli.Entity;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
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
        public async Task<Stream> GetStreamAsync(string url, string referer = null, string userAgent = null)
        {
            using var request = CreateRequest(url, referer, userAgent);
            using var response = await _client.SendAsync(request, Program.Token);
            var body = await response.Content.ReadAsByteArrayAsync(Program.Token);
            // A non-2xx status with a body is not thrown here: Cloudflare's challenge page and
            // provider error pages carry the readable reason that DanbooruPostDao reports.
            // With no body there is nothing to report but the status.
            if (!response.IsSuccessStatusCode && body.Length == 0)
            {
                var hint = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? " - this provider may need a login: danboo login <provider>"
                    : "";
                throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}{hint}");
            }
            return new MemoryStream(body);
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
