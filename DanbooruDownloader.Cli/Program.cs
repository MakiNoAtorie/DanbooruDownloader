using DanbooruDownloader.Cli.Config;
using DanbooruDownloader.Cli.DAO;
using DanbooruDownloader.Cli.Data;
using DanbooruDownloader.Cli.Entity;
using DanbooruDownloader.Cli.Net;
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
            if (args.Length == 0 || args[0] is "-h" or "--help")
            {
                PrintUsage();
                return args.Length == 0 ? 1 : 0;
            }

            // "providers" and "cookie" take a positional sub-action (list/show/set) rather
            // than --flags, so they're handled separately from the flag-based commands.
            if (args[0] == "providers") return RunProviders(args.Skip(1).ToArray());
            if (args[0] == "cookie") return RunCookie(args.Skip(1).ToArray());

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
                danboo search   --provider <name> --tags "<tags>" [options]
                danboo download --provider <name> --tags "<tags>" --output <dir> [options]
                danboo batch    --jobs <file> --provider-names <p1,p2,...> --output <dir> [options]
                danboo providers list|show <name> [--providers <path>]
                danboo cookie set <provider> <value> [--providers <path>]

                Common options:
                  --provider <name>     Provider name from DanbooruProviderList.xml (required for search/download)
                  --tags <tags>         Space-separated tag query
                  --rating <r>          safe | questionable | explicit
                  --not-rating          Negate --rating
                  --limit <n>           Max results per job
                  --page <n>            Page number
                  --providers <path>    Path to DanbooruProviderList.xml (default: ./DanbooruProviderList.xml)
                  --blacklist <tags>    Comma-separated tag names to skip (post.Hidden)
                  --blacklist-regex <r> Regex alternative to --blacklist
                  -v, --verbose         Verbose logging

                search-only:
                  --json                Print raw post list as JSON

                download/batch:
                  --output <dir>        Base output folder (default: ./downloads)
                  --filename-format <f> %token% template (default: %provider%/%rating% - %id% - %tags%)
                  --db <path>           Dedup SQLite DB path (default: ./downloaded.db)
                  --force                Re-download even if already recorded/on disk
                  --extension-filter <r> Regex against the file extension
                  --exclude-extension    Invert --extension-filter (exclude matches instead of requiring them)
                  --retry <n>            Retries per request on failure (default 3)
                  --retry-delay <ms>     Delay between retries (default 3000)

                batch-only:
                  --jobs <file>          One tag query per line (blank lines and lines starting with # are skipped)
                  --provider-names <p>   Comma-separated provider names; each job line runs against each provider

                danboo artist add --provider <name> --tags "<artist>" --output <dir> [options]
                danboo artist list [--db <path>]
                danboo artist remove --provider <name> --tags "<artist>" [--db <path>]
                danboo artist refresh [--provider <name> --tags "<artist>" | --all] [--db <path>]

                  "artist add" downloads the artist's entire gallery (paging through results
                  until an empty page) into --output, and remembers the provider/tag/output/
                  filename-format/rating so "artist refresh" can be run later with no flags -
                  it re-walks pages from the newest post until it hits a page that's entirely
                  already-downloaded, so only new work gets fetched.
                  --max-pages <n>        Safety cap on pages walked per gallery (default 1000)
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

        private static List<DanbooruProvider> LoadProviders(string providersPath, out string error)
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

        private static DanbooruProvider FindProvider(List<DanbooruProvider> providers, string name)
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

        private class DownloadOptions
        {
            public string OutputDir;
            public string FilenameFormatStr;
            public bool Force;
            public string ExtensionFilterRegex;
            public bool ExcludeExtension;
            public int Retry;
            public int RetryDelayMs;
        }

        private static DownloadOptions BuildDownloadOptions(Dictionary<string, string> flags)
        {
            return new DownloadOptions
            {
                OutputDir = flags.GetValueOrDefault("output", "downloads"),
                FilenameFormatStr = flags.GetValueOrDefault("filename-format", "%provider%/%rating% - %id% - %tags%"),
                Force = flags.ContainsKey("force"),
                ExtensionFilterRegex = flags.GetValueOrDefault("extension-filter"),
                ExcludeExtension = flags.ContainsKey("exclude-extension"),
                Retry = TryInt(flags.GetValueOrDefault("retry")) ?? 3,
                RetryDelayMs = TryInt(flags.GetValueOrDefault("retry-delay")) ?? 3000,
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
                }

                var baseFilename = Helper.MakeFilename(filenameFormat, post);
                var fullPath = baseFilename + extension;

                if (!opts.Force && File.Exists(fullPath))
                {
                    Console.WriteLine($"[{provider.Name}:{post.Id}] skipped (file already exists on disk: {fullPath})");
                    db.Insert(provider.Name, post.Id, Path.GetFileName(fullPath), fullPath);
                    skipped++;
                    alreadyKnown++;
                    continue;
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
                    db.Insert(provider.Name, post.Id, Path.GetFileName(fullPath), fullPath);
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
            // Gelbooru's DAPI (pid=) is 0-indexed; Danbooru/Shimmie2's page= is 1-indexed.
            int firstPage = provider.BoardType == BoardType.Gelbooru ? 0 : 1;
            int page = TryInt(flags.GetValueOrDefault("page")) ?? firstPage;
            int pagesFetched = 0;

            for (; pagesFetched < maxPages; pagesFetched++, page++)
            {
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

            return (totalDownloaded, totalSkipped, totalFailed, pagesFetched);
        }

        // ---- search ----

        private static async Task<int> RunSearch(Dictionary<string, string> flags)
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

        private static async Task<int> RunDownload(Dictionary<string, string> flags)
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
            var opts = BuildDownloadOptions(flags);
            var dbPath = flags.GetValueOrDefault("db", "downloaded.db");

            var db = new SqliteProvider(dbPath);
            db.Create();

            using var client = new CliWebClient(new CliConfig());
            var (dao, error) = await FetchPostsWithRetry(client, provider, searchParam, opts.Retry, opts.RetryDelayMs);
            if (error != null) { Console.Error.WriteLine($"Error: {error}"); return 1; }

            var posts = dao.Posts ?? new List<DanbooruPost>();
            Console.WriteLine($"{posts.Count} post(s) found from {provider.Name}");

            var (downloaded, skipped, failed, _) = await DownloadPostsAsync(client, db, provider, posts, opts);

            Console.WriteLine($"Done. Downloaded: {downloaded}, Skipped: {skipped}, Failed: {failed}");
            return failed > 0 && downloaded == 0 ? 1 : 0;
        }

        // ---- batch ----

        private static async Task<int> RunBatch(Dictionary<string, string> flags)
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

            foreach (var tag in jobLines)
            {
                foreach (var provider in selectedProviders)
                {
                    jobIndex++;
                    Console.WriteLine($"--- Job {jobIndex}/{totalJobs}: [{provider.Name}] tags=\"{tag}\" ---");

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

            Console.WriteLine($"Batch complete. Jobs: {totalJobs}, Downloaded: {totalDownloaded}, Skipped: {totalSkipped}, Failed: {totalFailed}");
            return 0;
        }

        // ---- providers ----

        private static int RunProviders(string[] rest)
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

            Console.Error.WriteLine("Usage: danboo providers list|show <name>");
            return 1;
        }

        // ---- cookie ----

        private static int RunCookie(string[] rest)
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

            if (provider.LoginType is not (LoginType.Cookie or LoginType.CookieAlwaysAsk))
            {
                Console.Error.WriteLine($"Warning: {provider.Name} has LoginType={provider.LoginType}, not Cookie - setting UserName anyway.");
            }

            provider.UserName = cookieValue;
            new DanbooruProviderDao().Save(providers, providersPath);
            Console.WriteLine($"Cookie value for '{provider.Name}' saved to {providersPath}.");
            return 0;
        }

        // ---- artist (tracked gallery + refresh) ----

        private static async Task<int> RunArtist(string action, Dictionary<string, string> flags)
        {
            var dbPath = flags.GetValueOrDefault("db", "downloaded.db");
            var db = new SqliteProvider(dbPath);
            db.Create();

            switch (action)
            {
                case "add":
                    return await RunArtistAdd(flags, db);
                case "list":
                    return RunArtistList(db);
                case "remove":
                    return RunArtistRemove(flags, db);
                case "refresh":
                    return await RunArtistRefresh(flags, db);
                default:
                    Console.Error.WriteLine($"Unknown artist action: {action}. Use add|list|remove|refresh.");
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

            var opts = BuildDownloadOptions(flags);
            opts.OutputDir = outputDir;
            var maxPages = TryInt(flags.GetValueOrDefault("max-pages")) ?? 1000;

            db.UpsertTrackedArtist(provider.Name, tag, outputDir, flags.GetValueOrDefault("filename-format"), flags.GetValueOrDefault("rating"));
            Console.WriteLine($"Tracking [{provider.Name}] \"{tag}\" -> {outputDir}");

            using var client = new CliWebClient(new CliConfig());
            var (downloaded, skipped, failed, pages) = await DownloadGalleryAsync(client, db, provider, tag, flags, opts, maxPages);
            db.TouchTrackedArtistRefreshed(provider.Name, tag);

            Console.WriteLine($"Done. Pages: {pages}, Downloaded: {downloaded}, Skipped: {skipped}, Failed: {failed}");
            return 0;
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
            using var client = new CliWebClient(new CliConfig());

            int totalDownloaded = 0, totalFailed = 0;
            foreach (var artist in targets)
            {
                var provider = FindProvider(providers, artist.ProviderName);
                if (provider == null)
                {
                    Console.Error.WriteLine($"Provider '{artist.ProviderName}' (tracked for \"{artist.Tag}\") not found in {providersPath}, skipping.");
                    continue;
                }

                Console.WriteLine($"--- Refreshing [{provider.Name}] \"{artist.Tag}\" -> {artist.OutputDir} ---");

                var artistFlags = new Dictionary<string, string>(flags, StringComparer.OrdinalIgnoreCase);
                if (artist.Rating != null) artistFlags.TryAdd("rating", artist.Rating);
                if (artist.FilenameFormat != null) artistFlags.TryAdd("filename-format", artist.FilenameFormat);

                var opts = BuildDownloadOptions(artistFlags);
                opts.OutputDir = artist.OutputDir;
                if (artist.FilenameFormat != null) opts.FilenameFormatStr = artist.FilenameFormat;

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
