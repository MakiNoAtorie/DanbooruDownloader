using DanbooruDownloader.Cli.Config;
using DanbooruDownloader.Cli.DAO;
using DanbooruDownloader.Cli.Data;
using DanbooruDownloader.Cli.Entity;
using DanbooruDownloader.Cli.Gallery;
using DanbooruDownloader.Cli.Net;
using DanbooruDownloader.Cli.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DanbooruDownloader.Cli
{
    public static class Program
    {
        public static readonly SimpleLogger Logger = new SimpleLogger();

        public static async Task<int> Main(string[] args)
        {
            // No args (e.g. a double-clicked launcher) drops into the numbered interactive
            // menu instead of just dumping usage and exiting - --help still gives the full
            // flag reference for scripting.
            if (args.Length == 0)
            {
                return await Interactive.InteractiveMenu.RunAsync();
            }

            if (args[0] is "-h" or "--help")
            {
                PrintUsage();
                return 0;
            }

            // "providers" and "cookie" take a positional sub-action (list/show/set) rather
            // than --flags, so they're handled separately from the flag-based commands.
            if (args[0] == "providers") return await RunProviders(args.Skip(1).ToArray());
            if (args[0] == "cookie") return RunCookie(args.Skip(1).ToArray());

            if (args[0] == "tags")
            {
                if (args.Length < 2 || args[1] != "download")
                {
                    Console.Error.WriteLine("Usage: danboo tags download --provider <name> [--output tags.xml] [options]");
                    return 1;
                }
                var tagsFlags = ParseFlags(args.Skip(2).ToArray());
                Logger.Verbose = tagsFlags.ContainsKey("verbose") || tagsFlags.ContainsKey("v");
                return await RunTagsDownload(tagsFlags);
            }

            // "artist schedule" has its own nested sub-action (install/uninstall/status).
            if (args[0] == "artist" && args.Length >= 2 && args[1] == "schedule")
            {
                if (args.Length < 3)
                {
                    Console.Error.WriteLine("Usage: danboo artist schedule install|uninstall|status [options]");
                    return 1;
                }
                var scheduleFlags = ParseFlags(args.Skip(3).ToArray());
                return RunArtistSchedule(args[2], scheduleFlags);
            }

            // "artist" also has a positional sub-action (add/list/remove/refresh), but the
            // rest of its args are still --flags.
            if (args[0] == "artist")
            {
                if (args.Length < 2)
                {
                    Console.Error.WriteLine("Usage: danboo artist add|list|remove|refresh [options]");
                    return 1;
                }
                var artistFlags = ParseFlags(args.Skip(2).ToArray());
                Logger.Verbose = artistFlags.ContainsKey("verbose") || artistFlags.ContainsKey("v");
                return await RunArtist(args[1], artistFlags);
            }

            var flags = ParseFlags(args.Skip(1).ToArray());
            Logger.Verbose = flags.ContainsKey("verbose") || flags.ContainsKey("v");

            switch (args[0])
            {
                case "search":
                    return await RunSearch(flags);

                case "download":
                    return await RunDownload(flags);

                case "batch":
                    return await RunBatch(flags);

                default:
                    Console.Error.WriteLine($"Unknown command: {args[0]}");
                    PrintUsage();
                    return 1;
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("""
                danboo                  Interactive numbered menu (no flags needed)

                danboo search   --provider <name> --tags "<tags>" [options]
                danboo download --provider <name> --tags "<tags>" --output <dir> [options]
                danboo batch    --jobs <file> --provider-names <p1,p2,...> --output <dir> [options]
                danboo providers list|show <name>|check [--providers <path>]
                danboo cookie set <provider> <value> [--providers <path>]
                danboo tags download --provider <name> [--output tags.xml] [options]

                Common options:
                  --provider <name>     Provider name from DanbooruProviderList.xml (required for search/download)
                  --tags <tags>         Space-separated tag query
                  --rating <r>          safe | questionable | explicit
                  --not-rating          Negate --rating
                  --limit <n>           Max results per job/page
                  --page <n>            Page number
                  --providers <path>    Path to DanbooruProviderList.xml (default: ./DanbooruProviderList.xml)
                  --blacklist <tags>    Comma-separated tag names to skip (post.Hidden)
                  --blacklist-regex <r> Regex alternative to --blacklist
                  --delay <ms>          Politeness delay between requests (pages and file downloads); default 0
                  -v, --verbose         Verbose logging

                search-only:
                  --json                Print raw post list as JSON

                download/batch/artist:
                  --output <dir>        Base output folder (default: ./downloads)
                  --filename-format <f> %token% template (default: %provider%/%rating% - %id% - %tags%)
                  --db <path>           Dedup SQLite DB path (default: ./downloaded.db)
                  --force                Re-download even if already recorded/on disk
                  --extension-filter <r> Regex against the file extension
                  --exclude-extension    Invert --extension-filter (exclude matches instead of requiring them)
                  --retry <n>            Retries per request on failure (default 3)
                  --retry-delay <ms>     Delay between retries (default 3000)
                  --dedup-by-md5         Also skip a post if its exact image was already downloaded under
                                         a different provider/post-id (common for crossposted art)
                  --html-index           Write an index.html gallery into --output after downloading
                  --all-pages            (download/batch only) walk every page instead of just one -
                                         same pagination as "artist add", but without tracking it
                  --max-pages <n>        Safety cap on pages walked (default 1000)

                batch-only:
                  --jobs <file>          One tag query per line (blank lines and lines starting with # are skipped)
                  --provider-names <p>   Comma-separated provider names; each job line runs against each provider

                danboo artist add      --provider <name> --tags "<artist>" --output <dir> [options]
                danboo artist add-many --file <path> [options]
                danboo artist list     [--db <path>]
                danboo artist remove   --provider <name> --tags "<artist>" [--db <path>]
                danboo artist refresh  [--provider <name> --tags "<artist>" | --all] [--db <path>]

                  "artist add" downloads the artist's entire gallery (paging through results
                  until an empty page) into --output, and remembers the provider/tag/output/
                  filename-format/rating so "artist refresh" can be run later with no flags -
                  it re-walks pages from the newest post until it hits a page that's entirely
                  already-downloaded, so only new work gets fetched. Generates an index.html
                  in --output by default (--no-html-index to skip).

                  "artist add-many" reads --file, one "provider,tag,output_dir[,rating]" line
                  per artist, and runs "artist add" for each.

                danboo artist schedule install [--interval-hours <n>] [--db <path>] [--providers <path>]
                danboo artist schedule uninstall
                danboo artist schedule status

                  Installs a macOS launchd agent (~/Library/LaunchAgents) that runs
                  "artist refresh --all" every --interval-hours (default 24), so tracked
                  artists actually stay up to date without you remembering to run refresh.

                danboo tags download downloads a provider's tag list into tags.xml so
                %artist%/%character%/%copyright%/%circle% filename tokens resolve to real
                names instead of falling back to "Unknown" - fetches by tag type where the
                board supports it (most do), since a plain count-ordered dump buries rare
                tags like individual artist names under generic ones like "long_hair".
                """);
        }

        private static Dictionary<string, string> ParseFlags(string[] args)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--") && !(args[i].StartsWith("-") && args[i].Length == 2)) continue;

                var key = args[i].TrimStart('-');
                var hasValue = i + 1 < args.Length && !(args[i + 1].StartsWith("--"));
                result[key] = hasValue ? args[++i] : "true";
            }
            return result;
        }

        private static int? TryInt(string value)
        {
            return int.TryParse(value, out var result) ? result : null;
        }

        // ---- shared: providers, search params, fetching, downloading ----

        internal static List<DanbooruProvider> LoadProviders(string providersPath, out string error)
        {
            error = null;
            try
            {
                return new DanbooruProviderDao().Read(providersPath);
            }
            catch (Exception ex)
            {
                error = $"Failed to read providers from {providersPath}: {ex.Message}";
                return null;
            }
        }

        internal static DanbooruProvider FindProvider(List<DanbooruProvider> providers, string name)
        {
            return providers.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        private static DanbooruSearchParam BuildSearchParam(DanbooruProvider provider, string tag, Dictionary<string, string> flags)
        {
            var rating = flags.GetValueOrDefault("rating");
            // Accept short forms (safe/questionable/explicit) as well as the raw
            // "rating:x" meta-tag the original app's dropdown values used internally.
            if (!string.IsNullOrWhiteSpace(rating) && !rating.Contains(':'))
            {
                rating = "rating:" + rating;
            }
            var searchParam = new DanbooruSearchParam
            {
                Provider = provider,
                Tag = tag,
                Rating = rating,
                IsNotRating = flags.ContainsKey("not-rating"),
                Limit = TryInt(flags.GetValueOrDefault("limit")) ?? provider.DefaultLimit,
                Page = TryInt(flags.GetValueOrDefault("page")),
            };
            searchParam.Option = BuildOption(provider, tag, flags);
            return searchParam;
        }

        private static DanbooruPostDaoOption BuildOption(DanbooruProvider provider, string tag, Dictionary<string, string> flags)
        {
            var option = new DanbooruPostDaoOption
            {
                Provider = provider,
                Query = tag,
                SearchTags = tag,
            };

            var blacklistRegex = flags.GetValueOrDefault("blacklist-regex");
            if (!string.IsNullOrWhiteSpace(blacklistRegex))
            {
                option.BlacklistedTagsUseRegex = true;
                option.BlacklistedTagsRegex = new Regex(blacklistRegex, RegexOptions.IgnoreCase);
            }
            else
            {
                var blacklist = flags.GetValueOrDefault("blacklist");
                if (!string.IsNullOrWhiteSpace(blacklist))
                {
                    option.BlacklistedTags = blacklist.Split(',')
                        .Select(t => new DanbooruTag { Name = t.Trim() })
                        .Where(t => t.Name.Length > 0)
                        .ToList();
                }
            }

            return option;
        }

        private static async Task<(DanbooruPostDao dao, string error)> FetchPostsWithRetry(CliWebClient client, DanbooruProvider provider, DanbooruSearchParam searchParam, int retries, int retryDelayMs)
        {
            var url = provider.GetQueryUrl(searchParam);
            Logger.Debug($"GET {url}");

            string lastError = null;
            for (int attempt = 0; attempt <= retries; attempt++)
            {
                try
                {
                    using var stream = await client.GetStreamAsync(url);
                    var dao = new DanbooruPostDao(stream, searchParam.Option, searchParam.Page);
                    if (!dao.HasError) return (dao, null);
                    lastError = dao.ErrorMessage;
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                }

                if (attempt < retries)
                {
                    Logger.Warn($"Search request failed ({lastError}), retrying ({attempt + 1}/{retries})...");
                    await Task.Delay(retryDelayMs);
                }
            }
            return (null, $"Request failed after {retries} retr{(retries == 1 ? "y" : "ies")}: {lastError}");
        }

        private static string GetExtension(string fileUrl)
        {
            string extension = fileUrl.Contains('?') ? fileUrl[..fileUrl.IndexOf('?')] : fileUrl;
            extension = Path.GetExtension(extension);
            return string.IsNullOrWhiteSpace(extension) ? ".jpg" : extension;
        }

        private static void TryGenerateHtmlIndex(string outputDir, string providerName, string tag)
        {
            try
            {
                HtmlGalleryGenerator.Generate(outputDir, $"{providerName} - {tag}");
            }
            catch (Exception ex)
            {
                // Never let index generation break an otherwise-successful download.
                Logger.Warn($"Failed to generate index.html for {outputDir}: {ex.Message}");
            }
        }

        private class DownloadOptions
        {
            public string OutputDir;
            public string FilenameFormatStr;
            public bool Force;
            public string ExtensionFilterRegex;
            public bool ExcludeExtension;
            public int Retry;
            public int RetryDelayMs;
            public int RequestDelayMs;
            public bool DedupByMd5;
            public bool HtmlIndex;
        }

        private static DownloadOptions BuildDownloadOptions(Dictionary<string, string> flags)
        {
            return new DownloadOptions
            {
                OutputDir = flags.GetValueOrDefault("output", "downloads"),
                // Grouped by what was searched (tag/artist), not by which site it came from -
                // that's the more useful grouping when just browsing a downloads folder.
                FilenameFormatStr = flags.GetValueOrDefault("filename-format", "%searchtag%/%rating% - %id% - %tags%"),
                Force = flags.ContainsKey("force"),
                ExtensionFilterRegex = flags.GetValueOrDefault("extension-filter"),
                ExcludeExtension = flags.ContainsKey("exclude-extension"),
                Retry = TryInt(flags.GetValueOrDefault("retry")) ?? 3,
                RetryDelayMs = TryInt(flags.GetValueOrDefault("retry-delay")) ?? 3000,
                // Politeness delay between requests (both page fetches and file downloads) -
                // added after we got Cloudflare-walled testing Danbooru; keeps galleries with
                // many pages/files from hammering a site in a tight loop.
                RequestDelayMs = TryInt(flags.GetValueOrDefault("delay")) ?? 0,
                DedupByMd5 = flags.ContainsKey("dedup-by-md5"),
                HtmlIndex = flags.ContainsKey("html-index"),
            };
        }

        private static async Task<(int downloaded, int skipped, int failed, int alreadyKnown)> DownloadPostsAsync(
            CliWebClient client, SqliteProvider db, DanbooruProvider provider,
            List<DanbooruPost> posts, DownloadOptions opts)
        {
            var filenameFormat = new DanbooruFilenameFormat
            {
                BaseFolder = opts.OutputDir,
                FilenameFormat = opts.FilenameFormatStr,
            };

            Regex extFilter = string.IsNullOrWhiteSpace(opts.ExtensionFilterRegex) ? null : new Regex(opts.ExtensionFilterRegex, RegexOptions.IgnoreCase);

            int downloaded = 0, skipped = 0, failed = 0, alreadyKnown = 0;

            foreach (var post in posts)
            {
                if (post.Hidden)
                {
                    Console.WriteLine($"[{provider.Name}:{post.Id}] skipped (blacklisted tag)");
                    skipped++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(post.FileUrl))
                {
                    Console.WriteLine($"[{provider.Name}:{post.Id}] skipped (no file url)");
                    skipped++;
                    continue;
                }

                var extension = GetExtension(post.FileUrl);
                if (extFilter != null)
                {
                    var isMatch = extFilter.IsMatch(extension);
                    var shouldSkip = opts.ExcludeExtension ? isMatch : !isMatch;
                    if (shouldSkip)
                    {
                        Console.WriteLine($"[{provider.Name}:{post.Id}] skipped (extension filter: {extension})");
                        skipped++;
                        continue;
                    }
                }

                if (!opts.Force)
                {
                    var existing = db.GetDownloadedFileByProviderAndPostId(provider.Name, post.Id);
                    if (existing.Count > 0)
                    {
                        Console.WriteLine($"[{provider.Name}:{post.Id}] skipped (already downloaded: {existing[0].Path})");
                        skipped++;
                        alreadyKnown++;
                        continue;
                    }

                    if (opts.DedupByMd5 && !string.IsNullOrWhiteSpace(post.MD5))
                    {
                        var elsewhere = db.GetDownloadedFileByMd5(post.MD5);
                        if (elsewhere != null)
                        {
                            Console.WriteLine($"[{provider.Name}:{post.Id}] skipped (same image already downloaded via [{elsewhere.ProviderName}:{elsewhere.PostId}]: {elsewhere.Path})");
                            skipped++;
                            alreadyKnown++;
                            continue;
                        }
                    }
                }

                var baseFilename = Helper.MakeFilename(filenameFormat, post);
                var fullPath = baseFilename + extension;

                if (!opts.Force && File.Exists(fullPath))
                {
                    Console.WriteLine($"[{provider.Name}:{post.Id}] skipped (file already exists on disk: {fullPath})");
                    db.Insert(provider.Name, post.Id, Path.GetFileName(fullPath), fullPath, post.MD5);
                    skipped++;
                    alreadyKnown++;
                    continue;
                }

                if (opts.RequestDelayMs > 0 && downloaded + failed > 0)
                {
                    await Task.Delay(opts.RequestDelayMs);
                }

                Exception lastError = null;
                bool success = false;
                for (int attempt = 0; attempt <= opts.Retry; attempt++)
                {
                    try
                    {
                        await client.DownloadFileAsync(post.FileUrl, fullPath, referer: post.Referer);
                        success = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        if (attempt < opts.Retry)
                        {
                            Logger.Warn($"[{provider.Name}:{post.Id}] download failed ({ex.Message}), retrying ({attempt + 1}/{opts.Retry})...");
                            await Task.Delay(opts.RetryDelayMs);
                        }
                    }
                }

                if (success)
                {
                    db.Insert(provider.Name, post.Id, Path.GetFileName(fullPath), fullPath, post.MD5);
                    Console.WriteLine($"[{provider.Name}:{post.Id}] downloaded -> {fullPath}");
                    downloaded++;
                }
                else
                {
                    Console.Error.WriteLine($"[{provider.Name}:{post.Id}] failed after {opts.Retry} retries: {lastError?.Message}");
                    failed++;
                }
            }

            return (downloaded, skipped, failed, alreadyKnown);
        }

        /// <summary>
        /// Page through a provider's search results starting at searchParam.Page (default 1),
        /// downloading each page, until a page comes back empty (end of gallery) or a full
        /// page's posts were all already known (dedup DB hits) - which means we've caught up
        /// to previously-downloaded content, so it's safe to stop instead of re-walking the
        /// entire history every time. Used for both a one-off full-gallery grab and for
        /// `artist refresh`, where it doubles as the incremental "only fetch what's new" logic.
        /// </summary>
        private static async Task<(int downloaded, int skipped, int failed, int pages)> DownloadGalleryAsync(
            CliWebClient client, SqliteProvider db, DanbooruProvider provider,
            string tag, Dictionary<string, string> flags, DownloadOptions opts, int maxPages)
        {
            int totalDownloaded = 0, totalSkipped = 0, totalFailed = 0;
            int page = TryInt(flags.GetValueOrDefault("page")) ?? PageIndexing.FirstPage(provider.BoardType);
            int pagesFetched = 0;

            for (; pagesFetched < maxPages; pagesFetched++, page++)
            {
                if (opts.RequestDelayMs > 0 && pagesFetched > 0)
                {
                    await Task.Delay(opts.RequestDelayMs);
                }

                var pageFlags = new Dictionary<string, string>(flags, StringComparer.OrdinalIgnoreCase) { ["page"] = page.ToString() };
                var searchParam = BuildSearchParam(provider, tag, pageFlags);

                var (dao, error) = await FetchPostsWithRetry(client, provider, searchParam, opts.Retry, opts.RetryDelayMs);
                if (error != null)
                {
                    Console.Error.WriteLine($"Page {page} failed: {error}");
                    totalFailed++;
                    break;
                }

                var posts = dao.Posts ?? new List<DanbooruPost>();
                if (posts.Count == 0)
                {
                    Logger.Debug($"Page {page}: empty, end of gallery.");
                    break;
                }

                Console.WriteLine($"Page {page}: {posts.Count} post(s)");
                var (downloaded, skipped, failed, alreadyKnown) = await DownloadPostsAsync(client, db, provider, posts, opts);
                totalDownloaded += downloaded;
                totalSkipped += skipped;
                totalFailed += failed;

                if (alreadyKnown == posts.Count)
                {
                    Logger.Debug($"Page {page}: entirely already-downloaded, caught up.");
                    pagesFetched++;
                    break;
                }
            }

            if (opts.HtmlIndex && (totalDownloaded > 0 || !File.Exists(Path.Combine(opts.OutputDir, "index.html"))))
            {
                TryGenerateHtmlIndex(opts.OutputDir, provider.Name, tag);
            }

            return (totalDownloaded, totalSkipped, totalFailed, pagesFetched);
        }

        // ---- search ----

        internal static async Task<int> RunSearch(Dictionary<string, string> flags)
        {
            var providersPath = flags.GetValueOrDefault("providers", "DanbooruProviderList.xml");
            var providerName = flags.GetValueOrDefault("provider");
            if (string.IsNullOrWhiteSpace(providerName))
            {
                Console.Error.WriteLine("--provider is required.");
                return 1;
            }

            var providers = LoadProviders(providersPath, out var loadError);
            if (loadError != null) { Console.Error.WriteLine(loadError); return 1; }

            var provider = FindProvider(providers, providerName);
            if (provider == null)
            {
                Console.Error.WriteLine($"Provider '{providerName}' not found in {providersPath}.");
                Console.Error.WriteLine("Available providers: " + string.Join(", ", providers.Select(p => p.Name)));
                return 1;
            }

            var searchParam = BuildSearchParam(provider, flags.GetValueOrDefault("tags", ""), flags);

            using var client = new CliWebClient(new CliConfig());
            var (dao, error) = await FetchPostsWithRetry(client, provider, searchParam, 0, 0);
            if (error != null) { Console.Error.WriteLine($"Error: {error}"); return 1; }

            var posts = dao.Posts ?? new List<DanbooruPost>();

            if (flags.ContainsKey("json"))
            {
                var json = System.Text.Json.JsonSerializer.Serialize(
                    posts.Select(p => new { p.Id, p.Rating, p.MD5, p.FileUrl, p.Width, p.Height, p.Tags, p.Hidden }),
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                Console.WriteLine(json);
            }
            else
            {
                Console.WriteLine($"{posts.Count} post(s) from {provider.Name}");
                foreach (var post in posts)
                {
                    var hiddenTag = post.Hidden ? " [blacklisted]" : "";
                    Console.WriteLine($"{post.Id,10}  {post.Rating,-5}  {post.Width}x{post.Height}  {post.MD5}{hiddenTag}");
                }
            }

            return 0;
        }

        // ---- download ----

        internal static async Task<int> RunDownload(Dictionary<string, string> flags)
        {
            var providersPath = flags.GetValueOrDefault("providers", "DanbooruProviderList.xml");
            var providerName = flags.GetValueOrDefault("provider");
            if (string.IsNullOrWhiteSpace(providerName))
            {
                Console.Error.WriteLine("--provider is required.");
                return 1;
            }

            var providers = LoadProviders(providersPath, out var loadError);
            if (loadError != null) { Console.Error.WriteLine(loadError); return 1; }

            var provider = FindProvider(providers, providerName);
            if (provider == null)
            {
                Console.Error.WriteLine($"Provider '{providerName}' not found in {providersPath}.");
                Console.Error.WriteLine("Available providers: " + string.Join(", ", providers.Select(p => p.Name)));
                return 1;
            }

            var tag = flags.GetValueOrDefault("tags", "");
            var opts = BuildDownloadOptions(flags);
            var dbPath = flags.GetValueOrDefault("db", "downloaded.db");

            var db = new SqliteProvider(dbPath);
            db.Create();

            using var client = new CliWebClient(new CliConfig());

            if (flags.ContainsKey("all-pages"))
            {
                var maxPages = TryInt(flags.GetValueOrDefault("max-pages")) ?? 1000;
                var (downloadedAll, skippedAll, failedAll, pages) = await DownloadGalleryAsync(client, db, provider, tag, flags, opts, maxPages);
                Console.WriteLine($"Done. Pages: {pages}, Downloaded: {downloadedAll}, Skipped: {skippedAll}, Failed: {failedAll}");
                return failedAll > 0 && downloadedAll == 0 ? 1 : 0;
            }

            var searchParam = BuildSearchParam(provider, tag, flags);
            var (dao, error) = await FetchPostsWithRetry(client, provider, searchParam, opts.Retry, opts.RetryDelayMs);
            if (error != null) { Console.Error.WriteLine($"Error: {error}"); return 1; }

            var posts = dao.Posts ?? new List<DanbooruPost>();
            Console.WriteLine($"{posts.Count} post(s) found from {provider.Name}");

            var (downloaded, skipped, failed, _) = await DownloadPostsAsync(client, db, provider, posts, opts);
            if (opts.HtmlIndex) TryGenerateHtmlIndex(opts.OutputDir, provider.Name, tag);

            Console.WriteLine($"Done. Downloaded: {downloaded}, Skipped: {skipped}, Failed: {failed}");
            return failed > 0 && downloaded == 0 ? 1 : 0;
        }

        // ---- batch ----

        internal static async Task<int> RunBatch(Dictionary<string, string> flags)
        {
            var jobsPath = flags.GetValueOrDefault("jobs");
            var providerNamesRaw = flags.GetValueOrDefault("provider-names");
            if (string.IsNullOrWhiteSpace(jobsPath) || string.IsNullOrWhiteSpace(providerNamesRaw))
            {
                Console.Error.WriteLine("--jobs and --provider-names are required.");
                return 1;
            }
            if (!File.Exists(jobsPath))
            {
                Console.Error.WriteLine($"Jobs file not found: {jobsPath}");
                return 1;
            }

            var providersPath = flags.GetValueOrDefault("providers", "DanbooruProviderList.xml");
            var providers = LoadProviders(providersPath, out var loadError);
            if (loadError != null) { Console.Error.WriteLine(loadError); return 1; }

            var providerNames = providerNamesRaw.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            var selectedProviders = new List<DanbooruProvider>();
            foreach (var name in providerNames)
            {
                var p = FindProvider(providers, name);
                if (p == null)
                {
                    Console.Error.WriteLine($"Provider '{name}' not found in {providersPath}.");
                    return 1;
                }
                selectedProviders.Add(p);
            }

            var jobLines = File.ReadAllLines(jobsPath)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("#"))
                .ToList();

            if (jobLines.Count == 0)
            {
                Console.Error.WriteLine($"No job lines found in {jobsPath}.");
                return 1;
            }

            var opts = BuildDownloadOptions(flags);
            var dbPath = flags.GetValueOrDefault("db", "downloaded.db");
            var db = new SqliteProvider(dbPath);
            db.Create();

            using var client = new CliWebClient(new CliConfig());

            int totalDownloaded = 0, totalSkipped = 0, totalFailed = 0, jobIndex = 0;
            int totalJobs = jobLines.Count * selectedProviders.Count;
            var allPages = flags.ContainsKey("all-pages");
            var maxPages = TryInt(flags.GetValueOrDefault("max-pages")) ?? 1000;

            foreach (var tag in jobLines)
            {
                foreach (var provider in selectedProviders)
                {
                    jobIndex++;
                    Console.WriteLine($"--- Job {jobIndex}/{totalJobs}: [{provider.Name}] tags=\"{tag}\" ---");

                    if (allPages)
                    {
                        var (downloadedAll, skippedAll, failedAll, pages) = await DownloadGalleryAsync(client, db, provider, tag, flags, opts, maxPages);
                        Console.WriteLine($"Pages: {pages}, Downloaded: {downloadedAll}, Skipped: {skippedAll}, Failed: {failedAll}");
                        totalDownloaded += downloadedAll;
                        totalSkipped += skippedAll;
                        totalFailed += failedAll;
                        continue;
                    }

                    var searchParam = BuildSearchParam(provider, tag, flags);
                    var (dao, error) = await FetchPostsWithRetry(client, provider, searchParam, opts.Retry, opts.RetryDelayMs);
                    if (error != null)
                    {
                        Console.Error.WriteLine($"Job failed: {error}");
                        totalFailed++;
                        continue;
                    }

                    var posts = dao.Posts ?? new List<DanbooruPost>();
                    Console.WriteLine($"{posts.Count} post(s) found");

                    var (downloaded, skipped, failed, _) = await DownloadPostsAsync(client, db, provider, posts, opts);
                    totalDownloaded += downloaded;
                    totalSkipped += skipped;
                    totalFailed += failed;
                }
            }

            if (opts.HtmlIndex) TryGenerateHtmlIndex(opts.OutputDir, string.Join("+", providerNames), string.Join(" | ", jobLines));

            Console.WriteLine($"Batch complete. Jobs: {totalJobs}, Downloaded: {totalDownloaded}, Skipped: {totalSkipped}, Failed: {totalFailed}");
            return 0;
        }

        // ---- providers ----

        internal static async Task<int> RunProviders(string[] rest)
        {
            var flags = ParseFlags(rest.Length > 1 ? rest[1..] : Array.Empty<string>());
            var providersPath = flags.GetValueOrDefault("providers", "DanbooruProviderList.xml");
            var providers = LoadProviders(providersPath, out var loadError);
            if (loadError != null) { Console.Error.WriteLine(loadError); return 1; }

            if (rest.Length == 0 || rest[0] == "list")
            {
                foreach (var p in providers)
                {
                    Console.WriteLine($"{p.Name,-28} {p.BoardType,-9} {p.Preferred,-5} {p.LoginType,-15} {p.Url}");
                }
                return 0;
            }

            if (rest[0] == "show")
            {
                if (rest.Length < 2) { Console.Error.WriteLine("Usage: danboo providers show <name>"); return 1; }
                var provider = FindProvider(providers, rest[1]);
                if (provider == null) { Console.Error.WriteLine($"Provider '{rest[1]}' not found."); return 1; }

                Console.WriteLine($"Name:          {provider.Name}");
                Console.WriteLine($"Url:           {provider.Url}");
                Console.WriteLine($"BoardType:     {provider.BoardType}");
                Console.WriteLine($"Preferred:     {provider.Preferred}");
                Console.WriteLine($"LoginType:     {provider.LoginType}");
                Console.WriteLine($"UserName:      {provider.UserName}");
                Console.WriteLine($"ApiKey:        {(string.IsNullOrEmpty(provider.ApiKey) ? "(none)" : "(set)")}");
                Console.WriteLine($"DefaultLimit:  {provider.DefaultLimit}");
                Console.WriteLine($"HardLimit:     {provider.HardLimit}");
                Console.WriteLine($"DateTimeFormat:{provider.DateTimeFormat}");
                return 0;
            }

            if (rest[0] == "check")
            {
                await RunProvidersCheck(providers);
                return 0;
            }

            Console.Error.WriteLine("Usage: danboo providers list|show <name>|check");
            return 1;
        }

        /// <summary>
        /// Pings every provider's base URL and classifies the result - added after we found
        /// Danbooru itself silently Cloudflare-walling anonymous requests (200/403 with a
        /// "Just a moment..." challenge page instead of real data). Lets you know upfront
        /// which providers are actually usable before trying to track an artist there.
        /// </summary>
        private static async Task RunProvidersCheck(List<DanbooruProvider> providers)
        {
            using var client = new CliWebClient(new CliConfig());

            foreach (var p in providers)
            {
                string status;
                try
                {
                    using var stream = await client.GetStreamAsync(p.Url);
                    using var reader = new StreamReader(stream);
                    var body = await reader.ReadToEndAsync();

                    if (body.Contains(Helper.CLOUDFLARE_STR) || body.Contains(Helper.DDOS_STR))
                        status = "Cloudflare-blocked";
                    else
                        status = "OK";
                }
                catch (Exception ex)
                {
                    status = $"unreachable ({ex.Message})";
                }

                Console.WriteLine($"{p.Name,-28} {status,-20} {p.Url}");
            }
        }

        // ---- tags ----

        internal static async Task<int> RunTagsDownload(Dictionary<string, string> flags)
        {
            var providersPath = flags.GetValueOrDefault("providers", "DanbooruProviderList.xml");
            var providerName = flags.GetValueOrDefault("provider");
            if (string.IsNullOrWhiteSpace(providerName))
            {
                Console.Error.WriteLine("--provider is required.");
                return 1;
            }

            var providers = LoadProviders(providersPath, out var loadError);
            if (loadError != null) { Console.Error.WriteLine(loadError); return 1; }

            var provider = FindProvider(providers, providerName);
            if (provider == null)
            {
                Console.Error.WriteLine($"Provider '{providerName}' not found in {providersPath}.");
                return 1;
            }

            var output = flags.GetValueOrDefault("output", "tags.xml");
            // Conservative default: a very large --limit can itself trip a site's bot
            // detection (seen live against Konachan - 1000 got Cloudflare-walled, 50-100 is fine).
            var limitPerPage = TryInt(flags.GetValueOrDefault("limit")) ?? 100;
            // This is a per-tag-type page cap (up to 4 types fetched separately), not a
            // single shared total - the generic --max-pages default of 1000 would mean up to
            // 400,000 tags/requests here, so tags download gets its own, much smaller default.
            var maxPages = TryInt(flags.GetValueOrDefault("max-pages")) ?? 50;
            var delayMs = TryInt(flags.GetValueOrDefault("delay")) ?? 0;

            using var client = new CliWebClient(new CliConfig());
            List<DanbooruTag> tags;
            try
            {
                tags = await DanbooruDownloader.Cli.Tags.TagDownloader.DownloadAsync(client, provider, limitPerPage, maxPages, delayMs, Console.WriteLine);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Tag download failed: {ex.Message}");
                return 1;
            }

            DanbooruTagsDao.Save(output, tags);
            Console.WriteLine($"Saved {tags.Count} tags to {output}.");
            return 0;
        }

        // ---- cookie ----

        internal static int RunCookie(string[] rest)
        {
            if (rest.Length < 3 || rest[0] != "set")
            {
                Console.Error.WriteLine("Usage: danboo cookie set <provider> <value> [--providers <path>]");
                return 1;
            }

            var providerName = rest[1];
            var cookieValue = rest[2];
            var flags = ParseFlags(rest.Length > 3 ? rest[3..] : Array.Empty<string>());
            var providersPath = flags.GetValueOrDefault("providers", "DanbooruProviderList.xml");

            var providers = LoadProviders(providersPath, out var loadError);
            if (loadError != null) { Console.Error.WriteLine(loadError); return 1; }

            var provider = FindProvider(providers, providerName);
            if (provider == null) { Console.Error.WriteLine($"Provider '{providerName}' not found."); return 1; }

            // Setting a cookie only does anything at request time if LoginType is actually
            // Cookie - GetQueryUrl() only injects the cookie jar for that case. Previously
            // this just warned and left the provider as-is (e.g. Anonymous), so the cookie
            // silently never got used; since running "cookie set" is a clear signal the user
            // wants cookie auth for this provider, switch it automatically instead.
            if (provider.LoginType is not (LoginType.Cookie or LoginType.CookieAlwaysAsk))
            {
                Console.WriteLine($"Note: {provider.Name} was LoginType={provider.LoginType} - switching it to Cookie so this value actually gets used.");
                provider.LoginType = LoginType.Cookie;
            }

            provider.UserName = cookieValue;
            new DanbooruProviderDao().Save(providers, providersPath);
            Console.WriteLine($"Cookie value for '{provider.Name}' saved to {providersPath}.");
            return 0;
        }

        // ---- artist (tracked gallery + refresh) ----

        internal static async Task<int> RunArtist(string action, Dictionary<string, string> flags)
        {
            var dbPath = flags.GetValueOrDefault("db", "downloaded.db");
            var db = new SqliteProvider(dbPath);
            db.Create();

            switch (action)
            {
                case "add":
                    return await RunArtistAdd(flags, db);
                case "add-many":
                    return await RunArtistAddMany(flags, db);
                case "list":
                    return RunArtistList(db);
                case "remove":
                    return RunArtistRemove(flags, db);
                case "refresh":
                    return await RunArtistRefresh(flags, db);
                default:
                    Console.Error.WriteLine($"Unknown artist action: {action}. Use add|add-many|list|remove|refresh.");
                    return 1;
            }
        }

        private static async Task<int> RunArtistAdd(Dictionary<string, string> flags, SqliteProvider db)
        {
            var providersPath = flags.GetValueOrDefault("providers", "DanbooruProviderList.xml");
            var providerName = flags.GetValueOrDefault("provider");
            var tag = flags.GetValueOrDefault("tags");
            var outputDir = flags.GetValueOrDefault("output");

            if (string.IsNullOrWhiteSpace(providerName) || string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(outputDir))
            {
                Console.Error.WriteLine("Usage: danboo artist add --provider <name> --tags \"<artist>\" --output <dir> [options]");
                return 1;
            }

            var providers = LoadProviders(providersPath, out var loadError);
            if (loadError != null) { Console.Error.WriteLine(loadError); return 1; }

            var provider = FindProvider(providers, providerName);
            if (provider == null)
            {
                Console.Error.WriteLine($"Provider '{providerName}' not found in {providersPath}.");
                return 1;
            }

            using var client = new CliWebClient(new CliConfig());
            return await AddOneArtist(client, db, provider, tag, outputDir, flags);
        }

        /// <summary>
        /// Shared by "artist add" (single, from --flags) and "artist add-many" (bulk, from a
        /// file) so both paths track+download identically.
        /// </summary>
        private static async Task<int> AddOneArtist(CliWebClient client, SqliteProvider db, DanbooruProvider provider, string tag, string outputDir, Dictionary<string, string> flags)
        {
            var opts = BuildDownloadOptions(flags);
            opts.OutputDir = outputDir;
            // The default format groups by %searchtag% (see BuildDownloadOptions), but here
            // --output is already a dedicated per-artist folder - nesting by searchtag again
            // inside it would just duplicate the folder name (e.g. downloads/sushoartworks/
            // sushoartworks/...), so drop that prefix unless the user explicitly asked for
            // their own format.
            if (!flags.ContainsKey("filename-format"))
            {
                opts.FilenameFormatStr = "%rating% - %id% - %tags%";
            }
            // Tracked artists get a browsable index.html by default (no GUI grid anymore);
            // opt out with --no-html-index.
            opts.HtmlIndex = !flags.ContainsKey("no-html-index");
            var maxPages = TryInt(flags.GetValueOrDefault("max-pages")) ?? 1000;

            db.UpsertTrackedArtist(provider.Name, tag, outputDir, flags.GetValueOrDefault("filename-format"), flags.GetValueOrDefault("rating"));
            Console.WriteLine($"Tracking [{provider.Name}] \"{tag}\" -> {outputDir}");

            var (downloaded, skipped, failed, pages) = await DownloadGalleryAsync(client, db, provider, tag, flags, opts, maxPages);
            db.TouchTrackedArtistRefreshed(provider.Name, tag);

            Console.WriteLine($"Done. Pages: {pages}, Downloaded: {downloaded}, Skipped: {skipped}, Failed: {failed}");
            return 0;
        }

        /// <summary>
        /// Bulk-register + download several artists from a file: one "provider,tag,output_dir"
        /// (or "provider,tag,output_dir,rating") line each. Blank lines and lines starting
        /// with # are skipped, matching the "batch" jobs-file convention.
        /// </summary>
        private static async Task<int> RunArtistAddMany(Dictionary<string, string> flags, SqliteProvider db)
        {
            var filePath = flags.GetValueOrDefault("file");
            if (string.IsNullOrWhiteSpace(filePath))
            {
                Console.Error.WriteLine("Usage: danboo artist add-many --file <path> [options]");
                Console.Error.WriteLine("File format: one 'provider,tag,output_dir[,rating]' per line.");
                return 1;
            }
            if (!File.Exists(filePath))
            {
                Console.Error.WriteLine($"File not found: {filePath}");
                return 1;
            }

            var providersPath = flags.GetValueOrDefault("providers", "DanbooruProviderList.xml");
            var providers = LoadProviders(providersPath, out var loadError);
            if (loadError != null) { Console.Error.WriteLine(loadError); return 1; }

            var lines = File.ReadAllLines(filePath)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("#"))
                .ToList();

            if (lines.Count == 0)
            {
                Console.Error.WriteLine($"No entries found in {filePath}.");
                return 1;
            }

            using var client = new CliWebClient(new CliConfig());
            int ok = 0, failed = 0;
            var interArtistDelayMs = TryInt(flags.GetValueOrDefault("delay")) ?? 0;

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var parts = line.Split(',').Select(p => p.Trim()).ToArray();
                if (parts.Length < 3)
                {
                    Console.Error.WriteLine($"Skipping malformed line (need provider,tag,output_dir): \"{line}\"");
                    failed++;
                    continue;
                }

                var provider = FindProvider(providers, parts[0]);
                if (provider == null)
                {
                    Console.Error.WriteLine($"Provider '{parts[0]}' not found in {providersPath}, skipping \"{line}\".");
                    failed++;
                    continue;
                }

                // --delay already spaces out requests *within* one artist's gallery walk
                // (DownloadGalleryAsync); this spaces out the handoff *between* artists too,
                // since consecutive lines are often the same provider.
                if (interArtistDelayMs > 0 && i > 0)
                {
                    await Task.Delay(interArtistDelayMs);
                }

                var lineFlags = new Dictionary<string, string>(flags, StringComparer.OrdinalIgnoreCase);
                if (parts.Length >= 4 && !string.IsNullOrWhiteSpace(parts[3])) lineFlags["rating"] = parts[3];

                Console.WriteLine($"=== [{provider.Name}] \"{parts[1]}\" -> {parts[2]} ===");
                var result = await AddOneArtist(client, db, provider, parts[1], parts[2], lineFlags);
                if (result == 0) ok++; else failed++;
            }

            Console.WriteLine($"add-many complete. Artists added: {ok}, Failed: {failed}");
            return 0;
        }

        /// <summary>
        /// Installs/removes/checks a macOS launchd agent that periodically runs
        /// "artist refresh --all" on its own - since a refresh command you have to remember
        /// to run isn't actually automatic, this is the piece that makes it so.
        /// </summary>
        internal static int RunArtistSchedule(string action, Dictionary<string, string> flags)
        {
            switch (action)
            {
                case "install":
                    {
                        var intervalHours = TryInt(flags.GetValueOrDefault("interval-hours")) ?? 24;
                        var dbPath = Path.GetFullPath(flags.GetValueOrDefault("db", "downloaded.db"));
                        var providersPath = Path.GetFullPath(flags.GetValueOrDefault("providers", "DanbooruProviderList.xml"));
                        var (ok, message) = Scheduling.LaunchdScheduler.Install(intervalHours, dbPath, providersPath, Directory.GetCurrentDirectory());
                        Console.WriteLine(message);
                        return ok ? 0 : 1;
                    }
                case "uninstall":
                    {
                        var (ok, message) = Scheduling.LaunchdScheduler.Uninstall();
                        Console.WriteLine(message);
                        return ok ? 0 : 1;
                    }
                case "status":
                    Console.WriteLine(Scheduling.LaunchdScheduler.Status());
                    return 0;
                default:
                    Console.Error.WriteLine("Usage: danboo artist schedule install|uninstall|status [--interval-hours <n>] [--db <path>] [--providers <path>]");
                    return 1;
            }
        }

        private static int RunArtistList(SqliteProvider db)
        {
            var artists = db.ListTrackedArtists();
            if (artists.Count == 0)
            {
                Console.WriteLine("No tracked artists yet. Use 'danboo artist add' to track one.");
                return 0;
            }

            foreach (var a in artists)
            {
                var lastRefresh = a.LastRefreshedAt?.ToString("yyyy-MM-dd HH:mm") ?? "never";
                Console.WriteLine($"[{a.ProviderName}] \"{a.Tag}\" -> {a.OutputDir}  (last refreshed: {lastRefresh})");
            }
            return 0;
        }

        private static int RunArtistRemove(Dictionary<string, string> flags, SqliteProvider db)
        {
            var providerName = flags.GetValueOrDefault("provider");
            var tag = flags.GetValueOrDefault("tags");
            if (string.IsNullOrWhiteSpace(providerName) || string.IsNullOrWhiteSpace(tag))
            {
                Console.Error.WriteLine("Usage: danboo artist remove --provider <name> --tags \"<artist>\"");
                return 1;
            }

            var removed = db.RemoveTrackedArtist(providerName, tag);
            Console.WriteLine(removed
                ? $"Stopped tracking [{providerName}] \"{tag}\" (downloaded files are kept on disk)."
                : $"No tracked artist found for [{providerName}] \"{tag}\".");
            return 0;
        }

        private static async Task<int> RunArtistRefresh(Dictionary<string, string> flags, SqliteProvider db)
        {
            var providersPath = flags.GetValueOrDefault("providers", "DanbooruProviderList.xml");
            var providerName = flags.GetValueOrDefault("provider");
            var tag = flags.GetValueOrDefault("tags");
            var all = flags.ContainsKey("all");

            if (!all && (string.IsNullOrWhiteSpace(providerName) || string.IsNullOrWhiteSpace(tag)))
            {
                Console.Error.WriteLine("Usage: danboo artist refresh --provider <name> --tags \"<artist>\"  (or --all)");
                return 1;
            }

            var providers = LoadProviders(providersPath, out var loadError);
            if (loadError != null) { Console.Error.WriteLine(loadError); return 1; }

            var targets = all ? db.ListTrackedArtists() : new List<TrackedArtist> { db.GetTrackedArtist(providerName, tag) };
            targets = targets.Where(t => t != null).ToList();

            if (targets.Count == 0)
            {
                Console.Error.WriteLine(all ? "No tracked artists to refresh." : $"No tracked artist found for [{providerName}] \"{tag}\". Use 'danboo artist add' first.");
                return 1;
            }

            var maxPages = TryInt(flags.GetValueOrDefault("max-pages")) ?? 1000;
            var interArtistDelayMs = TryInt(flags.GetValueOrDefault("delay")) ?? 0;
            using var client = new CliWebClient(new CliConfig());

            int totalDownloaded = 0, totalFailed = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                var artist = targets[i];
                var provider = FindProvider(providers, artist.ProviderName);
                if (provider == null)
                {
                    Console.Error.WriteLine($"Provider '{artist.ProviderName}' (tracked for \"{artist.Tag}\") not found in {providersPath}, skipping.");
                    continue;
                }

                // Same reasoning as add-many: space out the handoff between artists, not
                // just requests within one artist's own gallery walk.
                if (interArtistDelayMs > 0 && i > 0)
                {
                    await Task.Delay(interArtistDelayMs);
                }

                Console.WriteLine($"--- Refreshing [{provider.Name}] \"{artist.Tag}\" -> {artist.OutputDir} ---");

                var artistFlags = new Dictionary<string, string>(flags, StringComparer.OrdinalIgnoreCase);
                if (artist.Rating != null) artistFlags.TryAdd("rating", artist.Rating);
                if (artist.FilenameFormat != null) artistFlags.TryAdd("filename-format", artist.FilenameFormat);

                var opts = BuildDownloadOptions(artistFlags);
                opts.OutputDir = artist.OutputDir;
                if (artist.FilenameFormat != null)
                {
                    opts.FilenameFormatStr = artist.FilenameFormat;
                }
                else if (!artistFlags.ContainsKey("filename-format"))
                {
                    // Same reasoning as AddOneArtist: --output is already this artist's own
                    // folder, so don't also nest by %searchtag% inside it.
                    opts.FilenameFormatStr = "%rating% - %id% - %tags%";
                }
                opts.HtmlIndex = !flags.ContainsKey("no-html-index");

                var (downloaded, skipped, failed, pages) = await DownloadGalleryAsync(client, db, provider, artist.Tag, artistFlags, opts, maxPages);
                db.TouchTrackedArtistRefreshed(provider.Name, artist.Tag);
                totalDownloaded += downloaded;
                totalFailed += failed;

                Console.WriteLine($"Pages: {pages}, Downloaded: {downloaded}, Skipped: {skipped}, Failed: {failed}");
            }

            Console.WriteLine($"Refresh complete. Artists: {targets.Count}, New downloads: {totalDownloaded}, Failed: {totalFailed}");
            return 0;
        }
    }
}
