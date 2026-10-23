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
            _client.DefaultRequestHeaders.UserAgent.ParseAdd(config.UserAgent);
            _client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(config.AcceptLanguage);
        }

        public async Task<Stream> GetStreamAsync(string url, string referer = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(referer))
            {
                request.Headers.Referrer = new Uri(referer);
            }

            var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            // Don't throw on a non-2xx status here: Cloudflare's bot-challenge page (and some
            // provider error pages) come back with readable HTML body content that
            // DanbooruPostDao already knows how to detect and report clearly (see
            // Helper.CLOUDFLARE_STR/DDOS_STR) - throwing here would hide that behind a bare
            // "403 Forbidden" instead.
            return await response.Content.ReadAsStreamAsync();
        }

        public async Task DownloadFileAsync(string url, string destinationPath, string referer = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(referer))
            {
                request.Headers.Referrer = new Uri(referer);
            }

            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var tempPath = destinationPath + ".!tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? ".");

            try
            {
                using (var fileStream = File.Create(tempPath))
                using (var responseStream = await response.Content.ReadAsStreamAsync())
                {
                    await responseStream.CopyToAsync(fileStream);
                }
                File.Move(tempPath, destinationPath, overwrite: true);
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
