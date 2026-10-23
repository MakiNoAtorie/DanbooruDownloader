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
using System.Threading;
using System.Threading.Tasks;

namespace DanbooruDownloader.Cli
{
    public static class Program
    {
        public static readonly SimpleLogger Logger = new SimpleLogger();

        private static CancellationTokenSource _cts = new CancellationTokenSource();
        private static bool _commandRunning;

        /// <summary>Cancelled by the first Ctrl+C during a command; every request and delay observes it.</summary>
        internal static CancellationToken Token => _cts.Token;

        public static async Task<int> Main(string[] args)
        {
            Console.CancelKeyPress += OnCancelKey;

            // No args (e.g. a double-clicked launcher) drops into the numbered interactive menu.
            if (args.Length == 0)
            {
                return await Interactive.InteractiveMenu.RunAsync();
            }
            return await RunCommand(args);
        }

        // First Ctrl+C stops the command cleanly (partial file removed, summary printed);
        // a second one, or one outside a command, ends the process as usual.
        private static void OnCancelKey(object sender, ConsoleCancelEventArgs e)
        {
            if (!_commandRunning || _cts.IsCancellationRequested) return;
            e.Cancel = true;
            _cts.Cancel();
            Ui.Warn("Stopping... (press Ctrl+C again to quit at once)");
        }

        /// <summary>Runs one command line. The interactive menu calls this too, so both share parsing and checks.</summary>
        internal static async Task<int> RunCommand(string[] args)
        {
            _commandRunning = true;
            try
            {
                return await Dispatch(args);
            }
            catch (UsageException ex)
            {
                Ui.Error(ex.Message);
                var help = HelpText.Commands.ContainsKey(args[0]) ? $"danboo {args[0]} --help" : "danboo --help";
                Ui.DimErr($"See: {help}");
                return 2;
            }
            catch (OperationCanceledException) when (Token.IsCancellationRequested)
            {
                Ui.Warn("Stopped.");
                return 130;
            }
            finally
            {
                _commandRunning = false;
                if (_cts.IsCancellationRequested) _cts = new CancellationTokenSource();
            }
        }

        private static readonly string[] CommandNames = { "download", "search", "batch", "artist", "login", "cookie", "apikey", "providers", "tags" };

        private static async Task<int> Dispatch(string[] args)
        {
            var command = args[0];
            var rest = args[1..];

            if (command is "-h" or "--help" or "help")
            {
                Ui.Line(rest.Length > 0 && HelpText.Commands.TryGetValue(rest[0], out var topic) ? topic() : HelpText.General);
                return 0;
            }
            if (command is "--version" or "-V")
            {
                Ui.Line($"danboo {typeof(Program).Assembly.GetName().Version?.ToString(3)}");
                return 0;
            }
            if ((rest.Contains("--help") || rest.Contains("-h")) && HelpText.Commands.TryGetValue(command, out var help))
            {
                Ui.Line(help());
                return 0;
            }

            switch (command)
            {
                case "search": return await RunSearch(Parse(rest, SearchSpec));
                case "download": return await RunDownload(Parse(rest, "provider= tags= " + DownloadSpec));
                case "batch": return await RunBatch(Parse(rest, "jobs= provider-names= " + DownloadSpec));
                case "providers": return await RunProviders(rest);
                case "login": return RunLogin(rest);
                case "cookie": return RunCookie(rest);
                case "apikey": return RunApiKey(rest);
                case "artist": return await RunArtist(rest);
                case "tags":
                    if (rest.Length == 0 || rest[0] != "download") throw new UsageException("Usage: danboo tags download --provider <name> [--output <file>]");
                    return await RunTagsDownload(Parse(rest[1..], "provider= output= limit= max-pages= delay= providers="));
                default:
                    var guess = CommandNames.OrderBy(c => Flags.Distance(c, command)).First();
                    throw new UsageException($"Unknown command '{command}'.{(Flags.Distance(guess, command) <= 2 ? $" Did you mean '{guess}'?" : "")}");
            }
        }

        private const string FilterSpec = "rating= not-rating limit= page= blacklist= blacklist-regex= retry= retry-delay= providers=";
        private const string SearchSpec = "provider= tags= json " + FilterSpec;
        // --html-index is the old opt-in; the index is now written by default.
        private const string DownloadSpec = FilterSpec + " output= filename-format= db= force extension-filter= exclude-extension dedup-by-md5 html-index no-html-index all-pages max-pages= delay= track";

        private static Dictionary<string, string> Parse(string[] args, string spec, List<string> positionals = null)
        {
            var flags = Flags.Parse(args, spec, positionals);
            Logger.Verbose = flags.ContainsKey("verbose") || flags.ContainsKey("v");
            return flags;
        }

        // Prefer a provider list in the working directory; fall back to the one shipped next
        // to the binary, so a launcher started from another directory still finds it.
        internal static string DefaultProvidersPath
        {
            get
            {
                const string name = "DanbooruProviderList.xml";
                if (File.Exists(name)) return name;
                var shipped = Path.Combine(AppContext.BaseDirectory, name);
                return File.Exists(shipped) ? shipped : name;
            }
        }

        private static string GetProvidersPath(Dictionary<string, string> flags) => flags.GetValueOrDefault("providers") ?? DefaultProvidersPath;

        private static string GetDbPath(Dictionary<string, string> flags) => flags.GetValueOrDefault("db") ?? AppPaths.Db;

        private static SqliteProvider OpenDb(Dictionary<string, string> flags)
        {
            var db = new SqliteProvider(GetDbPath(flags));
            db.Create();
            return db;
        }

        // ---- shared: providers, search params, fetching, downloading ----

        /// <param name="withEnvCredentials">False for commands that save the list back, so
        /// secrets from .env are never written into DanbooruProviderList.xml.</param>
        internal static List<DanbooruProvider> LoadProviders(string providersPath, out string error, bool withEnvCredentials = true)
        {
            error = null;
            try
            {
                var providers = new DanbooruProviderDao().Read(providersPath);
                if (withEnvCredentials) EnvFile.Apply(providers, EnvFile.DefaultPath);
                return providers;
            }
            catch (Exception ex)
            {
                error = $"Failed to read providers from {providersPath}: {ex.Message}";
                return null;
            }
        }

        private static List<DanbooruProvider> LoadProviders(Dictionary<string, string> flags)
        {
            var providers = LoadProviders(GetProvidersPath(flags), out var error);
            if (error != null) throw new UsageException(error);
            return providers;
        }

        /// <summary>
        /// The exact name, else the providers whose name (letters and digits only) starts
        /// with, or else contains, what was typed: "e621" finds "e621 (Furry)".
        /// </summary>
        internal static List<DanbooruProvider> MatchProviders(List<DanbooruProvider> providers, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return new List<DanbooruProvider>();
            var exact = providers.FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (exact != null) return new List<DanbooruProvider> { exact };

            static string Key(string s) => new string(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
            var key = Key(name);
            if (key.Length == 0) return new List<DanbooruProvider>();
            var prefix = providers.Where(p => Key(p.Name).StartsWith(key)).ToList();
            return prefix.Count > 0 ? prefix : providers.Where(p => Key(p.Name).Contains(key)).ToList();
        }

        internal static DanbooruProvider FindProvider(List<DanbooruProvider> providers, string name)
        {
            var matches = MatchProviders(providers, name);
            return matches.Count == 1 ? matches[0] : null;
        }

        private static DanbooruProvider RequireProvider(List<DanbooruProvider> providers, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new UsageException("--provider is required. \"danboo providers list\" shows the sites.");
            var matches = MatchProviders(providers, name);
            if (matches.Count == 1) return matches[0];
            throw new UsageException(matches.Count == 0
                ? $"No provider matches '{name}'. Available: {string.Join(", ", providers.Select(p => p.Name))}"
                : $"'{name}' matches several providers: {string.Join(", ", matches.Select(p => p.Name))}. Type more of the name.");
        }

        internal static bool LoginMissing(DanbooruProvider provider) => provider.LoginType switch
        {
            LoginType.ApiKey => string.IsNullOrEmpty(provider.ApiKey),
            LoginType.Cookie or LoginType.CookieAlwaysAsk => string.IsNullOrEmpty(provider.UserName),
            _ => false,
        };

        private static void WarnIfLoginMissing(DanbooruProvider provider)
        {
            if (!LoginMissing(provider)) return;
            var kind = provider.LoginType == LoginType.ApiKey ? "an API key" : "a cookie";
            Logger.Warn($"{provider.Name} needs {kind} and none is saved, so requests may fail. Save one with: danboo login \"{provider.Name}\"");
        }

        private static Regex ParseRegex(Dictionary<string, string> flags, string key)
        {
            var pattern = flags.GetValueOrDefault(key);
            if (string.IsNullOrWhiteSpace(pattern)) return null;
            try
            {
                return new Regex(pattern, RegexOptions.IgnoreCase);
            }
            catch (ArgumentException ex)
            {
                throw new UsageException($"--{key} is not a valid regular expression: {ex.Message}");
            }
        }

        /// <param name="flags">--page here counts from 1 on every board.</param>
        private static DanbooruSearchParam BuildSearchParam(DanbooruProvider provider, string tag, Dictionary<string, string> flags)
        {
            var userPage = Flags.Int(flags, "page", min: 1);
            var searchParam = new DanbooruSearchParam
            {
                Provider = provider,
                Tag = tag,
                Rating = NormalizeRating(flags.GetValueOrDefault("rating"), provider),
                IsNotRating = flags.ContainsKey("not-rating"),
                Limit = ClampLimit(Flags.Int(flags, "limit", min: 1) ?? provider.DefaultLimit, provider),
                Page = userPage is int p ? PageIndexing.ToApiPage(provider.BoardType, p) : null,
            };
            searchParam.Option = BuildOption(provider, tag, flags);
            return searchParam;
        }

        /// <summary>
        /// Accepts short forms (safe/general/sensitive/questionable/explicit) or a raw
        /// "rating:x" meta-tag. On boards with general/sensitive ratings, "safe" maps to
        /// "general": there "rating:safe" is an alias for sensitive, the opposite of the intent.
        /// </summary>
        internal static string NormalizeRating(string rating, DanbooruProvider provider)
        {
            if (string.IsNullOrWhiteSpace(rating)) return null;
            rating = rating.Trim().ToLowerInvariant();
            if (rating.StartsWith("rating:")) rating = rating["rating:".Length..];
            if (provider.UsesGeneralSensitiveRatings && rating == "safe") rating = "general";
            return "rating:" + rating;
        }

        /// <summary>The word for a post's rating letter; "s" is "sensitive" on boards that have general/sensitive.</summary>
        internal static string RatingName(string rating, DanbooruProvider provider)
        {
            var r = rating?.Trim().ToLowerInvariant() ?? "";
            if (r.StartsWith("rating:")) r = r["rating:".Length..];
            return r switch
            {
                "g" => "general",
                "s" => provider.UsesGeneralSensitiveRatings ? "sensitive" : "safe",
                "q" => "questionable",
                "e" => "explicit",
                _ => r,
            };
        }

        private static int ClampLimit(int limit, DanbooruProvider provider)
        {
            if (provider.HardLimit > 0 && limit > provider.HardLimit)
            {
                Logger.Warn($"--limit {limit} is above {provider.Name}'s maximum, using {provider.HardLimit}.");
                return provider.HardLimit;
            }
            return limit;
        }

        private static DanbooruPostDaoOption BuildOption(DanbooruProvider provider, string tag, Dictionary<string, string> flags)
        {
            var option = new DanbooruPostDaoOption
            {
                Provider = provider,
                Query = tag,
                SearchTags = tag,
            };

            var blacklistRegex = ParseRegex(flags, "blacklist-regex");
            if (blacklistRegex != null)
            {
                option.BlacklistedTagsUseRegex = true;
                option.BlacklistedTagsRegex = blacklistRegex;
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
            Logger.Debug($"GET {Helper.RemoveAuthInfo(url)}");

            string lastError = null;
            for (int attempt = 0; attempt <= retries; attempt++)
            {
                try
                {
                    using var stream = await client.GetStreamAsync(url, userAgent: provider.UserAgent);
                    var dao = new DanbooruPostDao(stream, searchParam.Option, searchParam.Page);
                    if (!dao.HasError) return (dao, null);
                    lastError = dao.ErrorMessage;
                }
                catch (Exception ex)
                {
                    Token.ThrowIfCancellationRequested();
                    lastError = ex.Message;
                }

                if (attempt < retries)
                {
                    Logger.Warn($"Search request failed ({lastError}), retrying ({attempt + 1}/{retries})...");
                    await Task.Delay(retryDelayMs, Token);
                }
            }
            return (null, retries == 0 ? lastError : $"Request failed after {retries} retr{(retries == 1 ? "y" : "ies")}: {lastError}");
        }

        private static string GetExtension(string fileUrl)
        {
            string extension = fileUrl.Contains('?') ? fileUrl[..fileUrl.IndexOf('?')] : fileUrl;
            extension = Path.GetExtension(extension);
            return string.IsNullOrWhiteSpace(extension) ? ".jpg" : extension;
        }

        // Explorer and most Windows viewers cannot open paths over MAX_PATH (260 with the
        // terminator), although .NET can write them, so shorten the file name to fit.
        internal static string FitWindowsPath(string baseFilename, string extension)
        {
            const int maxPath = 259;
            var over = Path.GetFullPath(baseFilename).Length + extension.Length - maxPath;
            if (over <= 0) return baseFilename;
            var name = Path.GetFileName(baseFilename);
            // The folder alone is too long: cutting the name cannot fix it.
            if (over >= name.Length) return baseFilename;
            return baseFilename[..^name.Length] + Helper.CutAtTagBoundary(name[..^over]);
        }

        // Without a known MD5 there is nothing to compare, so assume it is the same file.
        internal static bool FileMatchesMd5(string path, string md5)
        {
            if (string.IsNullOrWhiteSpace(md5)) return true;
            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(stream));
            return string.Equals(hash, md5, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Writes index.html when something new arrived or there is none yet.</summary>
        private static void WriteIndexIfNeeded(DownloadOptions opts, string title, bool changed)
        {
            if (!opts.HtmlIndex || !Directory.Exists(opts.OutputDir)) return;
            if (!changed && File.Exists(Path.Combine(opts.OutputDir, "index.html"))) return;
            try
            {
                HtmlGalleryGenerator.Generate(opts.OutputDir, title);
            }
            catch (Exception ex)
            {
                // Never let index generation break an otherwise-successful download.
                Logger.Warn($"Failed to generate index.html for {opts.OutputDir}: {ex.Message}");
            }
        }

        private class DownloadOptions
        {
            public string OutputDir;
            public string FilenameFormatStr;
            public bool Force;
            public Regex ExtensionFilter;
            public bool ExcludeExtension;
            public int Retry;
            public int RetryDelayMs;
            public int RequestDelayMs;
            public bool DedupByMd5;
            public bool HtmlIndex;
        }

        private const string DefaultFilenameFormat = "%searchtag%/%rating% - %id% - %tags%";
        // For a folder that already belongs to one search (tracked artists).
        private const string TrackedFilenameFormat = "%rating% - %id% - %tags%";

        private static DownloadOptions BuildDownloadOptions(Dictionary<string, string> flags)
        {
            return new DownloadOptions
            {
                OutputDir = flags.GetValueOrDefault("output") ?? AppPaths.DefaultOutput,
                // Grouped by what was searched (tag/artist), not by which site it came from -
                // that's the more useful grouping when just browsing a downloads folder.
                FilenameFormatStr = flags.GetValueOrDefault("filename-format", DefaultFilenameFormat),
                Force = flags.ContainsKey("force"),
                ExtensionFilter = ParseRegex(flags, "extension-filter"),
                ExcludeExtension = flags.ContainsKey("exclude-extension"),
                Retry = Flags.Int(flags, "retry", 3, 0),
                RetryDelayMs = Flags.Int(flags, "retry-delay", 3000, 0),
                // Politeness delay between requests (both page fetches and file downloads) -
                // added after we got Cloudflare-walled testing Danbooru; keeps galleries with
                // many pages/files from hammering a site in a tight loop.
                RequestDelayMs = Flags.Int(flags, "delay", 0, 0),
                DedupByMd5 = flags.ContainsKey("dedup-by-md5"),
                HtmlIndex = !flags.ContainsKey("no-html-index"),
            };
        }

        /// <summary>
        /// Gelbooru's own API key page publishes a hard limit ("10 requests every 1
        /// second(s)") for API-key-authenticated requests. Nothing in the response tells us
        /// this programmatically, so if the user hasn't set an explicit --delay, fall back to
        /// a safe default for exactly that case rather than risk the key getting throttled -
        /// 150ms keeps well under 10/s even with rounding/network jitter.
        /// </summary>
        private static int EffectiveDelayMs(DownloadOptions opts, DanbooruProvider provider) => EffectiveDelayMs(opts.RequestDelayMs, provider);

        private static int EffectiveDelayMs(int requestedDelayMs, DanbooruProvider provider)
        {
            if (requestedDelayMs > 0) return requestedDelayMs;
            if (provider.BoardType == BoardType.Gelbooru && provider.LoginType == LoginType.ApiKey) return 150;
            return 0;
        }

        private static int MaxPages(Dictionary<string, string> flags) => Flags.Int(flags, "max-pages", 1000, 1);

        // The folder %searchtag% gives a search, so tracked and untracked downloads of a tag meet.
        internal static string FolderForSearch(string tag) => Helper.SanitizeFilename(string.IsNullOrWhiteSpace(tag) ? "latest" : tag);

        /// <summary>Counts for one run, kept outside the loops so a stopped run can still report them.</summary>
        private sealed class Tally
        {
            public int Downloaded, AlreadyHad, Blacklisted, NoUrl, Filtered, Failed, Pages;
            public long Bytes;
            public bool LastPageFull;

            public void Add(Tally other)
            {
                Downloaded += other.Downloaded;
                AlreadyHad += other.AlreadyHad;
                Blacklisted += other.Blacklisted;
                NoUrl += other.NoUrl;
                Filtered += other.Filtered;
                Failed += other.Failed;
                Pages += other.Pages;
                Bytes += other.Bytes;
            }

            public void Print(string label, string outputDir = null)
            {
                var parts = new List<string> { $"{Downloaded} downloaded" + (Bytes > 0 ? $" ({Ui.Size(Bytes)})" : "") };
                if (AlreadyHad > 0) parts.Add($"{AlreadyHad} already downloaded");
                var skipped = new List<string>();
                if (Blacklisted > 0) skipped.Add($"{Blacklisted} blacklisted");
                if (Filtered > 0) skipped.Add($"{Filtered} by extension filter");
                if (NoUrl > 0) skipped.Add($"{NoUrl} without a file URL");
                if (skipped.Count > 0) parts.Add("skipped " + string.Join(", ", skipped));
                parts.Add($"{Failed} failed");

                var line = $"{label}: {string.Join(", ", parts)}.";
                if (Failed > 0 || label == "Stopped") Ui.Notice(line); else Ui.Success(line);
                if (outputDir != null && Directory.Exists(outputDir)) Ui.Line($"Folder: {Path.GetFullPath(outputDir)}");
            }

            public int ExitCode(bool stopped) => stopped ? 130 : Failed > 0 && Downloaded == 0 ? 1 : 0;
        }

        /// <summary>Runs work until it ends or Ctrl+C stops it; returns true when stopped.</summary>
        private static async Task<bool> Stoppable(Func<Task> work)
        {
            try
            {
                await work();
                return false;
            }
            catch (OperationCanceledException) when (Token.IsCancellationRequested)
            {
                return true;
            }
        }

        // Filename tokens that need each tag's type (artist, character, ...).
        private static readonly string[] TagTypeTokens = { "%artist%", "%copyright%", "%character%", "%circle%", "%faults%", "%general%" };

        // Danbooru 2 and e621 send tag categories with every post (see DanbooruJsonParser).
        private static bool SendsTagTypes(DanbooruProvider provider) =>
            provider.Preferred == PreferredMethod.Json && provider.QueryStringJson?.Contains("/posts.json") == true;

        private static bool _tagListFetched;

        /// <summary>
        /// Gives %artist%-style tokens real names: Gelbooru by looking up this page's tags,
        /// other boards without tag types by fetching the tag list once into tags.xml.
        /// </summary>
        private static async Task ResolveTagNamesAsync(CliWebClient client, SqliteProvider db, DanbooruProvider provider, List<DanbooruPost> posts, DownloadOptions opts)
        {
            if (!TagTypeTokens.Any(opts.FilenameFormatStr.Contains) || SendsTagTypes(provider)) return;
            try
            {
                if (provider.BoardType == BoardType.Gelbooru)
                {
                    // Only posts that will be downloaded need names.
                    var toName = posts.Where(p => !p.Hidden && !string.IsNullOrWhiteSpace(p.FileUrl)
                        && (opts.Force || db.GetDownloadedFileByProviderAndPostId(provider.Name, p.Id).Count == 0)).ToList();
                    await Tags.TagDownloader.ResolveTagTypesAsync(client, provider, toName, EffectiveDelayMs(opts, provider));
                }
                else if (!_tagListFetched && !Helper.IsTagsXmlExist())
                {
                    // ponytail: one tags.xml for all boards; a second board's names need "danboo tags download".
                    _tagListFetched = true;
                    Ui.Line($"The filename format needs tag types; downloading {provider.Name}'s tag list once (danboo tags download)...");
                    var tags = await Tags.TagDownloader.DownloadAsync(client, provider, 100, 50, EffectiveDelayMs(opts, provider), Logger.Debug);
                    DanbooruTagsDao.Save(AppPaths.TagsXml, tags);
                    DanbooruTagsDao.Instance = new DanbooruTagsDao(AppPaths.TagsXml);
                    foreach (var post in posts) post.TagsEntity = Helper.ParseTags(post.Tags, provider);
                    Ui.Line($"Saved {tags.Count} tags to {AppPaths.TagsXml}.");
                }
            }
            catch (Exception ex) when (!Token.IsCancellationRequested)
            {
                // Filenames then fall back to tags.xml or the "no tag" values.
                Logger.Warn($"Tag type lookup on {provider.Name} failed: {ex.Message}");
            }
        }

        private static async Task DownloadPostsAsync(CliWebClient client, SqliteProvider db, DanbooruProvider provider, List<DanbooruPost> posts, DownloadOptions opts, Tally tally)
        {
            var filenameFormat = new DanbooruFilenameFormat
            {
                BaseFolder = opts.OutputDir,
                FilenameFormat = opts.FilenameFormatStr,
            };

            await ResolveTagNamesAsync(client, db, provider, posts, opts);

            int requests = 0;
            for (int i = 0; i < posts.Count; i++)
            {
                Token.ThrowIfCancellationRequested();
                var post = posts[i];
                var prefix = $"[{(i + 1).ToString().PadLeft(posts.Count.ToString().Length)}/{posts.Count}] {post.Id}";
                void Skip(string reason)
                {
                    if (Logger.Verbose) Ui.Dim($"{prefix}  skipped: {reason}");
                }

                if (post.Hidden)
                {
                    tally.Blacklisted++;
                    Skip("blacklisted tag");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(post.FileUrl))
                {
                    tally.NoUrl++;
                    Skip("no file URL");
                    continue;
                }

                var extension = GetExtension(post.FileUrl);
                if (opts.ExtensionFilter != null && opts.ExtensionFilter.IsMatch(extension) == opts.ExcludeExtension)
                {
                    tally.Filtered++;
                    Skip($"extension filter ({extension})");
                    continue;
                }

                if (!opts.Force)
                {
                    var existing = db.GetDownloadedFileByProviderAndPostId(provider.Name, post.Id);
                    if (existing.Count > 0)
                    {
                        tally.AlreadyHad++;
                        Skip($"already downloaded: {existing[0].Path}");
                        continue;
                    }

                    if (opts.DedupByMd5 && !string.IsNullOrWhiteSpace(post.MD5))
                    {
                        var elsewhere = db.GetDownloadedFileByMd5(post.MD5);
                        if (elsewhere != null)
                        {
                            tally.AlreadyHad++;
                            Skip($"same image already downloaded from {elsewhere.ProviderName} #{elsewhere.PostId}: {elsewhere.Path}");
                            continue;
                        }
                    }
                }

                var baseFilename = Helper.MakeFilename(filenameFormat, post);
                if (OperatingSystem.IsWindows()) baseFilename = FitWindowsPath(baseFilename, extension);
                var fullPath = baseFilename + extension;
                // Another post can produce the same name (formats without %id%, or %id% cut
                // off by the length limit). Trust an existing file only if its MD5 matches;
                // otherwise keep both by adding the post id.
                if (File.Exists(fullPath) && !FileMatchesMd5(fullPath, post.MD5))
                {
                    fullPath = $"{baseFilename} - {post.Id}{extension}";
                }

                if (!opts.Force && File.Exists(fullPath))
                {
                    db.Insert(provider.Name, post.Id, Path.GetFileName(fullPath), fullPath, post.MD5);
                    tally.AlreadyHad++;
                    Skip($"file already on disk: {fullPath}");
                    continue;
                }

                var effectiveDelayMs = EffectiveDelayMs(opts, provider);
                if (effectiveDelayMs > 0 && requests > 0)
                {
                    await Task.Delay(effectiveDelayMs, Token);
                }
                requests++;

                Exception lastError = null;
                long bytes = -1;
                for (int attempt = 0; attempt <= opts.Retry; attempt++)
                {
                    try
                    {
                        bytes = await client.DownloadFileAsync(post.FileUrl, fullPath, referer: post.Referer, userAgent: provider.UserAgent);
                        break;
                    }
                    catch (Exception ex)
                    {
                        Token.ThrowIfCancellationRequested();
                        lastError = ex;
                        if (attempt < opts.Retry)
                        {
                            Logger.Warn($"{post.Id}: download failed ({ex.Message}), retrying ({attempt + 1}/{opts.Retry})...");
                            await Task.Delay(opts.RetryDelayMs, Token);
                        }
                    }
                }

                if (bytes >= 0)
                {
                    db.Insert(provider.Name, post.Id, Path.GetFileName(fullPath), fullPath, post.MD5);
                    tally.Downloaded++;
                    tally.Bytes += bytes;
                    var head = $"{prefix}  {Ui.Size(bytes),8}  ";
                    Ui.Line(head + Ui.Fit(Path.GetRelativePath(opts.OutputDir, fullPath), Ui.Width - head.Length));
                }
                else
                {
                    tally.Failed++;
                    Ui.Error($"{prefix}  failed: {lastError?.Message}");
                }
            }
        }

        /// <summary>
        /// Page through a provider's search results starting at --page (default: the first
        /// page), downloading each page, until a page comes back empty (end of gallery) or
        /// maxPages is reached. With stopWhenCaughtUp (artist refresh only), also stop at the
        /// first page that needed no new downloads - the newest-first ordering means
        /// everything older is already there. Full grabs (artist add, --all-pages) must not
        /// stop early: an interrupted or partial earlier run leaves known posts on page 1 but
        /// gaps further back.
        /// </summary>
        private static async Task DownloadGalleryAsync(
            CliWebClient client, SqliteProvider db, DanbooruProvider provider,
            string tag, Dictionary<string, string> flags, DownloadOptions opts, Tally tally, int maxPages, bool stopWhenCaughtUp = false)
        {
            int page = Flags.Int(flags, "page", 1, 1);

            for (int fetched = 0; fetched < maxPages; fetched++, page++)
            {
                var effectiveDelayMs = EffectiveDelayMs(opts, provider);
                if (effectiveDelayMs > 0 && fetched > 0)
                {
                    await Task.Delay(effectiveDelayMs, Token);
                }

                var pageFlags = new Dictionary<string, string>(flags, StringComparer.OrdinalIgnoreCase) { ["page"] = page.ToString() };
                var searchParam = BuildSearchParam(provider, tag, pageFlags);

                var (dao, error) = await FetchPostsWithRetry(client, provider, searchParam, opts.Retry, opts.RetryDelayMs);
                if (error != null)
                {
                    Ui.Error($"Page {page} failed: {error}");
                    tally.Failed++;
                    return;
                }

                var posts = dao.Posts ?? new List<DanbooruPost>();
                if (posts.Count == 0)
                {
                    if (fetched == 0) Ui.Line(page == 1 ? "No posts found." : $"Page {page} is empty.");
                    Logger.Debug($"Page {page}: empty, end of gallery.");
                    return;
                }

                tally.Pages++;
                tally.LastPageFull = posts.Count >= searchParam.Limit;
                Ui.Heading($"Page {page}: {posts.Count} post(s)");

                var (knownBefore, downloadedBefore, failedBefore) = (tally.AlreadyHad, tally.Downloaded, tally.Failed);
                await DownloadPostsAsync(client, db, provider, posts, opts, tally);

                // Filtered posts (blacklist, extension, no URL) never become "known", so judge
                // caught-up by "nothing new was needed" rather than "every post was known".
                if (stopWhenCaughtUp && tally.AlreadyHad > knownBefore && tally.Downloaded == downloadedBefore && tally.Failed == failedBefore)
                {
                    Logger.Debug($"Page {page}: entirely already-downloaded, caught up.");
                    return;
                }
            }
        }

        // ---- search ----

        private static async Task<int> RunSearch(Dictionary<string, string> flags)
        {
            var providers = LoadProviders(flags);
            var provider = RequireProvider(providers, flags.GetValueOrDefault("provider"));
            WarnIfLoginMissing(provider);
            var tag = flags.GetValueOrDefault("tags", "");
            var searchParam = BuildSearchParam(provider, tag, flags);

            using var client = new CliWebClient(new CliConfig());
            var (dao, error) = await FetchPostsWithRetry(client, provider, searchParam, Flags.Int(flags, "retry", 3, 0), Flags.Int(flags, "retry-delay", 3000, 0));
            if (error != null) { Ui.Error($"Error: {error}"); return 1; }

            var posts = dao.Posts ?? new List<DanbooruPost>();

            if (flags.ContainsKey("json"))
            {
                var json = System.Text.Json.JsonSerializer.Serialize(
                    posts.Select(p => new { p.Id, p.Rating, p.MD5, p.FileUrl, p.Width, p.Height, p.Tags, p.Hidden }),
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                Console.WriteLine(json);
                return 0;
            }

            Ui.Heading($"{posts.Count} post(s) from {provider.Name}, page {Flags.Int(flags, "page", 1, 1)}");
            var width = Ui.Width;
            foreach (var post in posts)
            {
                var tags = post.Hidden ? "[blacklisted] " + post.Tags : post.Tags;
                Ui.Line(Ui.Fit($"{post.Id,10}  {RatingName(post.Rating, provider),-12}  {$"{post.Width}x{post.Height}",-10}  {post.Referer}  {tags}", width));
            }

            if (posts.Count > 0)
            {
                var args = new List<string> { "download", "--provider", provider.Name };
                foreach (var (key, value) in flags.Where(f => f.Key is not ("provider" or "json" or "v" or "verbose")))
                {
                    args.Add("--" + key);
                    if (value != "true") args.Add(value);
                }
                Ui.Dim($"To download these: {Flags.ToCommandLine(args)}");
            }
            return 0;
        }

        // ---- download ----

        private static async Task<int> RunDownload(Dictionary<string, string> flags)
        {
            var providers = LoadProviders(flags);
            var provider = RequireProvider(providers, flags.GetValueOrDefault("provider"));
            WarnIfLoginMissing(provider);
            var tag = flags.GetValueOrDefault("tags", "");
            var opts = BuildDownloadOptions(flags);
            var db = OpenDb(flags);
            using var client = new CliWebClient(new CliConfig());

            if (flags.ContainsKey("track"))
            {
                return await TrackAndReport(client, db, provider, tag, Path.Combine(opts.OutputDir, FolderForSearch(tag)), flags);
            }

            var allPages = flags.ContainsKey("all-pages");
            var tally = new Tally();
            var stopped = await Stoppable(() => DownloadGalleryAsync(client, db, provider, tag, flags, opts, tally, allPages ? MaxPages(flags) : 1));
            WriteIndexIfNeeded(opts, $"{provider.Name} - {tag}", tally.Downloaded > 0);

            tally.Print(stopped ? "Stopped" : "Done", opts.OutputDir);
            if (!stopped && !allPages && tally.LastPageFull)
            {
                Ui.Dim("That was one page. Add --all-pages for every page, or --track to also keep the search up to date.");
            }
            return tally.ExitCode(stopped);
        }

        // ---- batch ----

        private static async Task<int> RunBatch(Dictionary<string, string> flags)
        {
            var jobsPath = flags.GetValueOrDefault("jobs");
            var providerNamesRaw = flags.GetValueOrDefault("provider-names");
            if (string.IsNullOrWhiteSpace(jobsPath) || string.IsNullOrWhiteSpace(providerNamesRaw))
            {
                throw new UsageException("--jobs and --provider-names are required.");
            }
            if (!File.Exists(jobsPath))
            {
                throw new UsageException($"Jobs file not found: {jobsPath}");
            }

            var providers = LoadProviders(flags);
            var selectedProviders = providerNamesRaw.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0)
                .Select(name => RequireProvider(providers, name)).ToList();
            selectedProviders.ForEach(WarnIfLoginMissing);

            var jobLines = File.ReadAllLines(jobsPath)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("#"))
                .ToList();

            if (jobLines.Count == 0)
            {
                throw new UsageException($"No job lines found in {jobsPath}.");
            }

            var opts = BuildDownloadOptions(flags);
            var db = OpenDb(flags);
            using var client = new CliWebClient(new CliConfig());

            var total = new Tally();
            int totalJobs = jobLines.Count * selectedProviders.Count, jobIndex = 0;
            var maxPages = flags.ContainsKey("all-pages") ? MaxPages(flags) : 1;
            var track = flags.ContainsKey("track");

            var stopped = await Stoppable(async () =>
            {
                foreach (var tag in jobLines)
                {
                    foreach (var provider in selectedProviders)
                    {
                        jobIndex++;
                        Ui.Heading($"--- Job {jobIndex}/{totalJobs}: {provider.Name} \"{tag}\" ---");
                        var job = new Tally();
                        try
                        {
                            if (track) await TrackAndDownloadAsync(client, db, provider, tag, Path.Combine(opts.OutputDir, FolderForSearch(tag)), flags, job);
                            else await DownloadGalleryAsync(client, db, provider, tag, flags, opts, job, maxPages);
                        }
                        finally
                        {
                            total.Add(job);
                        }
                        job.Print("Job");
                    }
                }
            });

            if (!track) WriteIndexIfNeeded(opts, string.Join(" | ", jobLines), total.Downloaded > 0);
            total.Print(stopped ? "Stopped" : $"Batch complete ({totalJobs} jobs)", opts.OutputDir);
            return total.ExitCode(stopped);
        }

        // ---- providers ----

        private static string LoginStatus(DanbooruProvider p) => p.LoginType switch
        {
            LoginType.ApiKey => LoginMissing(p) ? "API key missing" : "API key",
            LoginType.Cookie or LoginType.CookieAlwaysAsk => LoginMissing(p) ? "cookie missing" : "cookie",
            LoginType.UserPass => "password",
            _ => "none",
        };

        private static async Task<int> RunProviders(string[] rest)
        {
            var action = rest.Length > 0 && !rest[0].StartsWith("-") ? rest[0] : "list";
            var positionals = new List<string>();
            var flags = Parse(rest.Length > 0 && rest[0] == action ? rest[1..] : rest, "providers=", positionals);
            var providers = LoadProviders(flags);

            switch (action)
            {
                case "list":
                    Ui.Dim($"{"NAME",-28} {"TYPE",-9} {"LOGIN",-16} URL");
                    foreach (var p in providers)
                    {
                        var line = $"{p.Name,-28} {p.BoardType,-9} {LoginStatus(p),-16} {p.Url}";
                        if (LoginMissing(p)) Ui.Notice(line); else Ui.Line(line);
                    }
                    if (providers.Any(LoginMissing)) Ui.Dim("Save a missing login with: danboo login <name>");
                    return 0;

                case "show":
                    {
                        if (positionals.Count != 1) throw new UsageException("Usage: danboo providers show <name>");
                        var provider = RequireProvider(providers, positionals[0]);
                        Ui.Line($"Name:           {provider.Name}");
                        Ui.Line($"Url:            {provider.Url}");
                        Ui.Line($"Board type:     {provider.BoardType}");
                        Ui.Line($"Data format:    {provider.Preferred}");
                        Ui.Line($"Login:          {LoginStatus(provider)}");
                        // For cookie logins UserName holds the cookie, a credential.
                        if (provider.LoginType == LoginType.ApiKey) Ui.Line($"User:           {provider.UserName}");
                        Ui.Line($"Default limit:  {provider.DefaultLimit}");
                        Ui.Line($"Maximum limit:  {provider.HardLimit}");
                        Ui.Line($"Date format:    {provider.DateTimeFormat}");
                        return 0;
                    }

                case "check":
                    await RunProvidersCheck(providers);
                    return 0;

                default:
                    throw new UsageException($"Unknown providers action '{action}'. Use list, show <name> or check.");
            }
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
            Ui.Dim($"{"NAME",-28} {"STATUS",-20} URL");

            foreach (var p in providers)
            {
                string status;
                bool ok = false;
                try
                {
                    using var stream = await client.GetStreamAsync(p.Url, userAgent: p.UserAgent);
                    using var reader = new StreamReader(stream);
                    var body = await reader.ReadToEndAsync();

                    ok = !(body.Contains(Helper.CLOUDFLARE_STR) || body.Contains(Helper.DDOS_STR));
                    status = ok ? "OK" : "Cloudflare-blocked";
                }
                catch (Exception ex) when (!Token.IsCancellationRequested)
                {
                    status = $"unreachable ({ex.Message})";
                }

                var line = $"{p.Name,-28} {status,-20} {p.Url}";
                if (ok) Ui.Line(line); else Ui.Notice(line);
            }
        }

        // ---- tags ----

        private static async Task<int> RunTagsDownload(Dictionary<string, string> flags)
        {
            var providers = LoadProviders(flags);
            var provider = RequireProvider(providers, flags.GetValueOrDefault("provider"));

            if (SendsTagTypes(provider))
            {
                Ui.Line($"Not needed: {provider.Name} sends tag categories with every post, so %artist%/%character%/%copyright% already resolve.");
                return 0;
            }

            var output = flags.GetValueOrDefault("output") ?? AppPaths.TagsXml;
            // Conservative default: a very large --limit can itself trip a site's bot
            // detection (seen live against Konachan - 1000 got Cloudflare-walled, 50-100 is fine).
            var limitPerPage = Flags.Int(flags, "limit", 100, 1);
            // This is a per-tag-type page cap (up to 4 types fetched separately), not a
            // single shared total - the generic --max-pages default of 1000 would mean up to
            // 400,000 tags/requests here, so tags download gets its own, much smaller default.
            var maxPages = Flags.Int(flags, "max-pages", 50, 1);
            var delayMs = EffectiveDelayMs(Flags.Int(flags, "delay", 0, 0), provider);

            using var client = new CliWebClient(new CliConfig());
            List<DanbooruTag> tags;
            try
            {
                tags = await Tags.TagDownloader.DownloadAsync(client, provider, limitPerPage, maxPages, delayMs, Console.WriteLine);
            }
            catch (Exception ex) when (!Token.IsCancellationRequested)
            {
                Ui.Error($"Tag download failed: {ex.Message}");
                return 1;
            }

            DanbooruTagsDao.Save(output, tags);
            Ui.Success($"Saved {tags.Count} tags to {output}.");
            if (Path.GetFullPath(output) != Path.GetFullPath(AppPaths.TagsXml))
            {
                Logger.Warn($"Filenames use {AppPaths.TagsXml}; this file is used only if moved there.");
            }
            return 0;
        }

        // ---- login ----

        private static DanbooruProvider ProviderForLogin(List<string> positionals, Dictionary<string, string> flags, string usage)
        {
            if (positionals.Count == 0) throw new UsageException(usage);
            return RequireProvider(LoadProviders(flags), positionals[0]);
        }

        /// <summary>Asks for whichever login the site uses and saves it to .env.</summary>
        private static int RunLogin(string[] rest)
        {
            var positionals = new List<string>();
            var flags = Parse(rest, "providers=", positionals);
            if (positionals.Count > 1) throw new UsageException("Usage: danboo login <provider>   (quote names with spaces)");
            var provider = ProviderForLogin(positionals, flags, "Usage: danboo login <provider>");

            var kind = provider.LoginType switch
            {
                LoginType.ApiKey => "1",
                LoginType.Cookie or LoginType.CookieAlwaysAsk => "2",
                _ => null,
            };
            if (kind == null)
            {
                Ui.Line($"{provider.Name} works without a login. A login can unlock more posts or higher limits.");
                Console.Write("1) API key  2) cookie  0) cancel: ");
                kind = Console.ReadLine()?.Trim();
            }
            return kind switch
            {
                "1" => SaveApiKey(provider, null, null),
                "2" => SaveCookie(provider, null),
                _ => 0,
            };
        }

        private static int RunCookie(string[] rest)
        {
            const string usage = "Usage: danboo cookie set <provider> [<cookie>]";
            if (rest.Length == 0 || rest[0] != "set") throw new UsageException(usage);
            var positionals = new List<string>();
            var flags = Parse(rest[1..], "providers=", positionals);
            if (positionals.Count > 2) throw new UsageException(usage);
            return SaveCookie(ProviderForLogin(positionals, flags, usage), positionals.ElementAtOrDefault(1));
        }

        /// <summary>
        /// Sets API-key auth for a provider. The second value's meaning depends on the board:
        /// Danbooru-style boards want a username ("login"), Gelbooru-style boards want a
        /// numeric account ID ("user_id") - see DanbooruProvider.GetQueryUrl().
        /// </summary>
        private static int RunApiKey(string[] rest)
        {
            const string usage = "Usage: danboo apikey set <provider> [<api-key> <username-or-user-id>]";
            if (rest.Length == 0 || rest[0] != "set") throw new UsageException(usage);
            var positionals = new List<string>();
            var flags = Parse(rest[1..], "providers=", positionals);
            if (positionals.Count is 2 or > 3) throw new UsageException(usage);
            return SaveApiKey(ProviderForLogin(positionals, flags, usage), positionals.ElementAtOrDefault(1), positionals.ElementAtOrDefault(2));
        }

        private static int SaveApiKey(DanbooruProvider provider, string apiKey, string user)
        {
            var gelbooru = provider.BoardType == BoardType.Gelbooru;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Ui.Dim(gelbooru
                    ? $"Find both values at {provider.Url}/index.php?page=account&s=options under \"API Access Credentials\" (&api_key=...&user_id=...)."
                    : $"Find the API key in your account settings at {provider.Url} (Danbooru: My Account > API Key).");
                apiKey = Ui.ReadSecret("API key");
            }
            if (string.IsNullOrWhiteSpace(user))
            {
                Console.Write(gelbooru ? "User ID (the number after user_id=): " : "Username: ");
                user = Console.ReadLine()?.Trim();
            }
            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(user))
            {
                throw new UsageException("Nothing saved: both the API key and the user are required.");
            }

            var key = EnvFile.KeyFor(provider);
            var path = EnvFile.Write((key + "_API_KEY", apiKey), (key + "_USER", user), (key + "_COOKIE", null));
            Ui.Success($"API key for {provider.Name} saved to {Path.GetFullPath(path)}.");
            return 0;
        }

        private static int SaveCookie(DanbooruProvider provider, string cookie)
        {
            if (string.IsNullOrWhiteSpace(cookie))
            {
                Ui.Dim($"In your browser, sign in at {provider.Url}, press F12 and open the Network tab, reload the page, " +
                       "select the first request and copy the whole value of its \"Cookie\" request header.");
                cookie = Ui.ReadSecret("Cookie");
            }
            if (string.IsNullOrWhiteSpace(cookie)) throw new UsageException("Nothing saved: the cookie is empty.");

            var key = EnvFile.KeyFor(provider);
            var path = EnvFile.Write((key + "_COOKIE", cookie), (key + "_API_KEY", null), (key + "_USER", null));
            Ui.Success($"Cookie for {provider.Name} saved to {Path.GetFullPath(path)}.");
            return 0;
        }

        // ---- artist (tracked gallery + refresh) ----

        private static async Task<int> RunArtist(string[] rest)
        {
            if (rest.Length == 0) throw new UsageException("Usage: danboo artist add|add-many|list|refresh|remove|schedule");
            var action = rest[0];
            var args = rest[1..];
            var positionals = new List<string>();

            switch (action)
            {
                case "add":
                    return await RunArtistAdd(Parse(args, "provider= tags= " + DownloadSpec, positionals), positionals);
                case "add-many":
                    return await RunArtistAddMany(Parse(args, "file= " + DownloadSpec));
                case "list":
                    return RunArtistList(Parse(args, "db= providers="));
                case "remove":
                    return RunArtistRemove(Parse(args, "provider= tags= db= providers=", positionals), positionals);
                case "refresh":
                    return await RunArtistRefresh(Parse(args, "provider= tags= all " + DownloadSpec, positionals), positionals);
                case "schedule":
                    if (args.Length == 0) throw new UsageException("Usage: danboo artist schedule install|uninstall|status");
                    return RunArtistSchedule(args[0], Parse(args[1..], "interval-hours= db= providers="));
                default:
                    throw new UsageException($"Unknown artist action '{action}'. Use add, add-many, list, refresh, remove or schedule.");
            }
        }

        /// <summary>The provider and artist, from &lt;provider&gt; &lt;artist&gt; or --provider/--tags.</summary>
        private static (string provider, string tag) ArtistTarget(Dictionary<string, string> flags, List<string> positionals)
        {
            if (positionals.Count > 2) throw new UsageException($"Too many arguments. Quote a search with spaces: \"{string.Join(" ", positionals.Skip(1))}\"");
            return (flags.GetValueOrDefault("provider") ?? positionals.ElementAtOrDefault(0),
                    flags.GetValueOrDefault("tags") ?? positionals.ElementAtOrDefault(1));
        }

        private static async Task<int> RunArtistAdd(Dictionary<string, string> flags, List<string> positionals)
        {
            var (providerName, tag) = ArtistTarget(flags, positionals);
            if (string.IsNullOrWhiteSpace(providerName) || string.IsNullOrWhiteSpace(tag))
            {
                throw new UsageException("Usage: danboo artist add <provider> <artist> [--output <dir>]");
            }

            var provider = RequireProvider(LoadProviders(flags), providerName);
            WarnIfLoginMissing(provider);
            var outputDir = flags.GetValueOrDefault("output") ?? Path.Combine(AppPaths.DefaultOutput, FolderForSearch(tag));
            using var client = new CliWebClient(new CliConfig());
            return await TrackAndReport(client, OpenDb(flags), provider, tag, outputDir, flags);
        }

        private static async Task<int> TrackAndReport(CliWebClient client, SqliteProvider db, DanbooruProvider provider, string tag, string outputDir, Dictionary<string, string> flags)
        {
            var tally = new Tally();
            var stopped = await Stoppable(() => TrackAndDownloadAsync(client, db, provider, tag, outputDir, flags, tally));
            tally.Print(stopped ? "Stopped" : "Done", outputDir);
            if (stopped) Ui.Dim("The next \"danboo artist refresh\" picks up the rest of the gallery.");
            return tally.ExitCode(stopped);
        }

        /// <summary>
        /// Remembers the search and downloads its whole gallery into outputDir (the search's
        /// own folder). Shared by "artist add", "artist add-many", "download --track" and
        /// "batch --track", so all of them track and download the same way.
        /// </summary>
        private static async Task TrackAndDownloadAsync(CliWebClient client, SqliteProvider db, DanbooruProvider provider, string tag, string outputDir, Dictionary<string, string> flags, Tally tally)
        {
            var opts = BuildDownloadOptions(flags);
            opts.OutputDir = Path.GetFullPath(outputDir);
            // outputDir already belongs to this search, so do not nest by %searchtag% again.
            if (!flags.ContainsKey("filename-format")) opts.FilenameFormatStr = TrackedFilenameFormat;

            db.UpsertTrackedArtist(provider.Name, tag, opts.OutputDir, flags.GetValueOrDefault("filename-format"), flags.GetValueOrDefault("rating"));
            Ui.Line($"Tracking {provider.Name} \"{tag}\" -> {opts.OutputDir}");

            await DownloadGalleryAsync(client, db, provider, tag, flags, opts, tally, MaxPages(flags));
            WriteIndexIfNeeded(opts, $"{provider.Name} - {tag}", tally.Downloaded > 0);
            // Only a complete walk counts as refreshed: until then, refresh walks every page.
            if (tally.Failed == 0) db.TouchTrackedArtistRefreshed(provider.Name, tag);
        }

        /// <summary>
        /// Bulk-register + download several artists from a file: one "provider,tag,output_dir"
        /// (or "provider,tag,output_dir,rating") line each. Blank lines and lines starting
        /// with # are skipped, matching the "batch" jobs-file convention.
        /// </summary>
        private static async Task<int> RunArtistAddMany(Dictionary<string, string> flags)
        {
            var filePath = flags.GetValueOrDefault("file");
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new UsageException("Usage: danboo artist add-many --file <path>   (lines of provider,artist,output_dir[,rating])");
            }
            if (!File.Exists(filePath))
            {
                throw new UsageException($"File not found: {filePath}");
            }

            var providers = LoadProviders(flags);
            var lines = File.ReadAllLines(filePath)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("#"))
                .ToList();

            if (lines.Count == 0)
            {
                throw new UsageException($"No entries found in {filePath}.");
            }

            var db = OpenDb(flags);
            using var client = new CliWebClient(new CliConfig());
            int ok = 0, failed = 0;
            var total = new Tally();
            var interArtistDelayMs = Flags.Int(flags, "delay", 0, 0);

            var stopped = await Stoppable(async () =>
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    var parts = line.Split(',').Select(p => p.Trim()).ToArray();
                    if (parts.Length < 3)
                    {
                        Ui.Error($"Skipping malformed line (need provider,artist,output_dir): \"{line}\"");
                        failed++;
                        continue;
                    }

                    var provider = FindProvider(providers, parts[0]);
                    if (provider == null)
                    {
                        Ui.Error($"No single provider matches '{parts[0]}', skipping \"{line}\".");
                        failed++;
                        continue;
                    }

                    // --delay already spaces out requests *within* one artist's gallery walk
                    // (DownloadGalleryAsync); this spaces out the handoff *between* artists too,
                    // since consecutive lines are often the same provider.
                    var effectiveInterArtistDelayMs = EffectiveDelayMs(interArtistDelayMs, provider);
                    if (effectiveInterArtistDelayMs > 0 && i > 0)
                    {
                        await Task.Delay(effectiveInterArtistDelayMs, Token);
                    }

                    var lineFlags = new Dictionary<string, string>(flags, StringComparer.OrdinalIgnoreCase);
                    if (parts.Length >= 4 && !string.IsNullOrWhiteSpace(parts[3])) lineFlags["rating"] = parts[3];

                    Ui.Heading($"=== {i + 1}/{lines.Count}: {provider.Name} \"{parts[1]}\" ===");
                    var artist = new Tally();
                    try
                    {
                        await TrackAndDownloadAsync(client, db, provider, parts[1], parts[2], lineFlags, artist);
                    }
                    finally
                    {
                        total.Add(artist);
                    }
                    artist.Print("Artist");
                    if (artist.ExitCode(false) == 0) ok++; else failed++;
                }
            });

            total.Print(stopped ? "Stopped" : $"add-many complete ({ok} added, {failed} failed)");
            return stopped ? 130 : failed > 0 ? 1 : 0;
        }

        private static int RunArtistSchedule(string action, Dictionary<string, string> flags)
        {
            switch (action)
            {
                case "install":
                    {
                        var intervalHours = Flags.Int(flags, "interval-hours", 24, 1);
                        var dbPath = Path.GetFullPath(GetDbPath(flags));
                        var providersPath = Path.GetFullPath(GetProvidersPath(flags));
                        var (ok, message) = Scheduling.Scheduler.Install(intervalHours, dbPath, providersPath, Directory.GetCurrentDirectory());
                        if (ok) Ui.Success(message); else Ui.Error(message);
                        return ok ? 0 : 1;
                    }
                case "uninstall":
                    {
                        var (ok, message) = Scheduling.Scheduler.Uninstall();
                        Ui.Line(message);
                        return ok ? 0 : 1;
                    }
                case "status":
                    Ui.Line(Scheduling.Scheduler.Status());
                    return 0;
                default:
                    throw new UsageException("Usage: danboo artist schedule install [--interval-hours <n>] | uninstall | status");
            }
        }

        private static int RunArtistList(Dictionary<string, string> flags)
        {
            var artists = OpenDb(flags).ListTrackedArtists();
            if (artists.Count == 0)
            {
                Ui.Line("No tracked artists yet. Track one with: danboo artist add <provider> <artist>");
                return 0;
            }

            Ui.Dim($"{"PROVIDER",-28} {"ARTIST",-30} {"LAST REFRESH",-16} FOLDER");
            foreach (var a in artists)
            {
                // SQLite CURRENT_TIMESTAMP is UTC.
                var lastRefresh = a.LastRefreshedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "not complete";
                Ui.Line($"{a.ProviderName,-28} {a.Tag,-30} {lastRefresh,-16} {a.OutputDir}");
            }
            return 0;
        }

        private static int RunArtistRemove(Dictionary<string, string> flags, List<string> positionals)
        {
            var (providerName, tag) = ArtistTarget(flags, positionals);
            if (string.IsNullOrWhiteSpace(providerName) || string.IsNullOrWhiteSpace(tag))
            {
                throw new UsageException("Usage: danboo artist remove <provider> <artist>");
            }

            var db = OpenDb(flags);
            var tracked = FindTracked(db, providerName, tag);
            if (tracked == null || !db.RemoveTrackedArtist(tracked.ProviderName, tracked.Tag))
            {
                Ui.Error($"No tracked artist \"{tag}\" on {providerName}. \"danboo artist list\" shows them.");
                return 1;
            }
            Ui.Success($"Stopped tracking {tracked.ProviderName} \"{tracked.Tag}\" (downloaded files are kept).");
            return 0;
        }

        // Tracked rows store the provider's full name; accept the same short forms as --provider.
        private static TrackedArtist FindTracked(SqliteProvider db, string providerName, string tag) =>
            db.ListTrackedArtists().FirstOrDefault(a => string.Equals(a.Tag, tag, StringComparison.OrdinalIgnoreCase)
                && MatchProviders(new List<DanbooruProvider> { new DanbooruProvider { Name = a.ProviderName } }, providerName).Count == 1);

        private static async Task<int> RunArtistRefresh(Dictionary<string, string> flags, List<string> positionals)
        {
            var (providerName, tag) = ArtistTarget(flags, positionals);
            // No artist named: refresh all of them.
            var all = flags.ContainsKey("all") || (providerName == null && tag == null);
            if (!all && (string.IsNullOrWhiteSpace(providerName) || string.IsNullOrWhiteSpace(tag)))
            {
                throw new UsageException("Usage: danboo artist refresh [<provider> <artist> | --all]");
            }

            var providers = LoadProviders(flags);
            var dbPath = GetDbPath(flags);
            var db = OpenDb(flags);
            var targets = all ? db.ListTrackedArtists() : new List<TrackedArtist> { FindTracked(db, providerName, tag) };
            targets = targets.Where(t => t != null).ToList();

            if (targets.Count == 0)
            {
                Ui.Error(all ? "No tracked artists to refresh. Track one with: danboo artist add <provider> <artist>"
                             : $"No tracked artist \"{tag}\" on {providerName}. \"danboo artist list\" shows them.");
                return 1;
            }

            var maxPages = MaxPages(flags);
            var interArtistDelayMs = Flags.Int(flags, "delay", 0, 0);
            // Older rows hold folders relative to where "artist add" ran, which was next to the database.
            var dbDir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
            using var client = new CliWebClient(new CliConfig());

            var total = new Tally();
            var stopped = await Stoppable(async () =>
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    var artist = targets[i];
                    var provider = providers.FirstOrDefault(p => string.Equals(p.Name, artist.ProviderName, StringComparison.OrdinalIgnoreCase));
                    if (provider == null)
                    {
                        Ui.Error($"Provider '{artist.ProviderName}' (tracked for \"{artist.Tag}\") not found in {GetProvidersPath(flags)}, skipping.");
                        total.Failed++;
                        continue;
                    }

                    // Same reasoning as add-many: space out the handoff between artists, not
                    // just requests within one artist's own gallery walk.
                    var effectiveInterArtistDelayMs = EffectiveDelayMs(interArtistDelayMs, provider);
                    if (effectiveInterArtistDelayMs > 0 && i > 0)
                    {
                        await Task.Delay(effectiveInterArtistDelayMs, Token);
                    }

                    var outputDir = Path.GetFullPath(artist.OutputDir, dbDir);
                    Ui.Heading($"--- {i + 1}/{targets.Count}: {provider.Name} \"{artist.Tag}\" -> {outputDir} ---");

                    var artistFlags = new Dictionary<string, string>(flags, StringComparer.OrdinalIgnoreCase);
                    if (artist.Rating != null) artistFlags.TryAdd("rating", artist.Rating);
                    if (artist.FilenameFormat != null) artistFlags.TryAdd("filename-format", artist.FilenameFormat);

                    var opts = BuildDownloadOptions(artistFlags);
                    opts.OutputDir = outputDir;
                    if (!artistFlags.ContainsKey("filename-format")) opts.FilenameFormatStr = TrackedFilenameFormat;

                    var tally = new Tally();
                    try
                    {
                        // An add or refresh that never finished left gaps further back, so walk every page.
                        await DownloadGalleryAsync(client, db, provider, artist.Tag, artistFlags, opts, tally, maxPages, stopWhenCaughtUp: artist.LastRefreshedAt != null);
                    }
                    finally
                    {
                        total.Add(tally);
                    }
                    WriteIndexIfNeeded(opts, $"{provider.Name} - {artist.Tag}", tally.Downloaded > 0);
                    if (tally.Failed == 0) db.TouchTrackedArtistRefreshed(provider.Name, artist.Tag);
                    tally.Print("Artist");
                }
            });

            total.Print(stopped ? "Stopped" : $"Refresh complete ({targets.Count} artist(s))");
            return total.ExitCode(stopped);
        }
    }
}
