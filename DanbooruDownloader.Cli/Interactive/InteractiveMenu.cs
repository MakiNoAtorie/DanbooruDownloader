using DanbooruDownloader.Cli.Entity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DanbooruDownloader.Cli.Interactive
{
    /// <summary>
    /// A numbered-menu front end over the existing flag-based commands - launched when
    /// "danboo" is run with no arguments, so double-clicking a launcher (or just typing
    /// "danboo") gives a usable prompt-driven experience instead of a bare usage dump.
    /// Every option here just builds the same flags dictionary the CLI parser would and
    /// calls straight into Program's existing Run* methods - no separate logic to keep
    /// in sync.
    /// </summary>
    public static class InteractiveMenu
    {
        // Asked once per session, then reused - re-prompting for these on every single
        // action (there are 8+ menu actions that all need one or both) turned out to be
        // real friction, and worse, a real bug: in testing, a "0" meant for a submenu's
        // "Back" choice landed on one of these prompts instead and was used as a literal
        // path, crashing with "Failed to read providers from 0: Could not find file...".
        private static string _providersPath;
        private static string _dbPath;

        private static string GetProvidersPath() => _providersPath ??= Prompt("Providers file", "DanbooruProviderList.xml");
        private static string GetDbPath() => _dbPath ??= Prompt("Dedup/tracking DB", "downloaded.db");

        public static async Task<int> RunAsync()
        {
            Console.WriteLine("DanbooruDownloader CLI - interactive mode");
            Console.WriteLine("(full flag reference: danboo --help)");

            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("1) Search");
                Console.WriteLine("2) Download");
                Console.WriteLine("3) Batch download (from a jobs file)");
                Console.WriteLine("4) Tracked artists (add / list / remove / refresh)");
                Console.WriteLine("5) Providers (list / show / check)");
                Console.WriteLine("6) Set a cookie for a provider");
                Console.WriteLine("7) Download tags.xml (enables %artist%/%character% filenames)");
                Console.WriteLine("8) Scheduled auto-refresh (launchd)");
                Console.WriteLine("0) Exit");

                var choice = Prompt("Choose an option", "0");

                try
                {
                    switch (choice)
                    {
                        case "1": await DoSearch(); break;
                        case "2": await DoDownload(); break;
                        case "3": await DoBatch(); break;
                        case "4": await DoArtistMenu(); break;
                        case "5": await DoProvidersMenu(); break;
                        case "6": await DoCookie(); break;
                        case "7": await DoTagsDownload(); break;
                        case "8": await DoScheduleMenu(); break;
                        case "0": case "q": case "exit": case "quit":
                            Console.WriteLine("Bye.");
                            return 0;
                        default:
                            Console.WriteLine("Not a valid option.");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error: {ex.Message}");
                }
            }
        }

        // ---- prompt helpers ----

        private static string Prompt(string label, string defaultValue = "")
        {
            Console.Write(string.IsNullOrEmpty(defaultValue) ? $"{label}: " : $"{label} [{defaultValue}]: ");
            var input = Console.ReadLine();
            return string.IsNullOrWhiteSpace(input) ? defaultValue : input.Trim();
        }

        private static string PromptRequired(string label)
        {
            while (true)
            {
                var value = Prompt(label);
                if (!string.IsNullOrWhiteSpace(value)) return value;
                Console.WriteLine("This is required.");
            }
        }

        private static bool Confirm(string label, bool defaultValue)
        {
            var suffix = defaultValue ? "Y/n" : "y/N";
            var input = Prompt($"{label} ({suffix})");
            if (string.IsNullOrWhiteSpace(input)) return defaultValue;
            return input.Equals("y", StringComparison.OrdinalIgnoreCase) || input.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private static void SetIfNotEmpty(Dictionary<string, string> flags, string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) flags[key] = value;
        }

        /// <summary>
        /// Lists providers with numbers so the user can pick "3" instead of typing an exact
        /// name; also accepts a name typed directly.
        /// </summary>
        private static DanbooruProvider PickProvider(List<DanbooruProvider> providers)
        {
            for (int i = 0; i < providers.Count; i++)
            {
                Console.WriteLine($"  {i + 1,3}) {providers[i].Name}");
            }

            while (true)
            {
                var input = PromptRequired("Provider (number or name)");
                if (int.TryParse(input, out var index) && index >= 1 && index <= providers.Count)
                {
                    return providers[index - 1];
                }
                var byName = Program.FindProvider(providers, input);
                if (byName != null) return byName;
                Console.WriteLine("Not found - try again.");
            }
        }

        private static List<DanbooruProvider> LoadProvidersOrThrow(string providersPath)
        {
            var providers = Program.LoadProviders(providersPath, out var error);
            if (error != null) throw new InvalidOperationException(error);
            return providers;
        }

        private static string RatingPrompt()
        {
            Console.WriteLine("  1) any   2) safe   3) questionable   4) explicit");
            var choice = Prompt("Rating", "1");
            return choice switch
            {
                "2" => "safe",
                "3" => "questionable",
                "4" => "explicit",
                _ => "",
            };
        }

        // ---- actions ----

        private static async Task DoSearch()
        {
            Console.WriteLine("\n== Search ==");
            var providersPath = GetProvidersPath();
            var providers = LoadProvidersOrThrow(providersPath);
            var provider = PickProvider(providers);
            var tags = Prompt("Tags (space-separated, blank = browse latest)");
            var rating = RatingPrompt();
            var limit = Prompt("Limit", provider.DefaultLimit.ToString());
            var json = Confirm("Show as JSON", false);

            var flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["provider"] = provider.Name,
                ["providers"] = providersPath,
            };
            SetIfNotEmpty(flags, "tags", tags);
            SetIfNotEmpty(flags, "rating", rating);
            SetIfNotEmpty(flags, "limit", limit);
            if (json) flags["json"] = "true";

            await Program.RunSearch(flags);
        }

        private static async Task DoDownload()
        {
            Console.WriteLine("\n== Download ==");
            var providersPath = GetProvidersPath();
            var providers = LoadProvidersOrThrow(providersPath);
            var provider = PickProvider(providers);
            var tags = Prompt("Tags (space-separated, blank = browse latest)");
            var rating = RatingPrompt();
            var limit = Prompt("Limit", provider.DefaultLimit.ToString());
            var output = Prompt("Output folder", "downloads");
            var allPages = Confirm("Download the whole gallery (every page)", false);
            var htmlIndex = Confirm("Generate a browsable index.html", true);

            var flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["provider"] = provider.Name,
                ["providers"] = providersPath,
                ["output"] = output,
            };
            SetIfNotEmpty(flags, "tags", tags);
            SetIfNotEmpty(flags, "rating", rating);
            SetIfNotEmpty(flags, "limit", limit);
            if (allPages) flags["all-pages"] = "true";
            if (htmlIndex) flags["html-index"] = "true";

            await Program.RunDownload(flags);
        }

        private static async Task DoBatch()
        {
            Console.WriteLine("\n== Batch download ==");
            var jobsFile = PromptRequired("Jobs file (one tag query per line)");
            var providersPath = GetProvidersPath();
            var providers = LoadProvidersOrThrow(providersPath);
            Console.WriteLine("Pick providers to run each job against (comma-separated numbers or names):");
            for (int i = 0; i < providers.Count; i++) Console.WriteLine($"  {i + 1,3}) {providers[i].Name}");
            var providerSelection = PromptRequired("Providers");
            var providerNames = ResolveProviderNames(providers, providerSelection);
            var output = Prompt("Output folder", "downloads");
            var allPages = Confirm("Download whole galleries (every page)", false);
            var htmlIndex = Confirm("Generate a browsable index.html", true);

            var flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["jobs"] = jobsFile,
                ["provider-names"] = string.Join(",", providerNames),
                ["providers"] = providersPath,
                ["output"] = output,
            };
            if (allPages) flags["all-pages"] = "true";
            if (htmlIndex) flags["html-index"] = "true";

            await Program.RunBatch(flags);
        }

        private static List<string> ResolveProviderNames(List<DanbooruProvider> providers, string selection)
        {
            var result = new List<string>();
            foreach (var token in selection.Split(','))
            {
                var t = token.Trim();
                if (t.Length == 0) continue;
                if (int.TryParse(t, out var index) && index >= 1 && index <= providers.Count)
                {
                    result.Add(providers[index - 1].Name);
                }
                else
                {
                    var p = Program.FindProvider(providers, t);
                    result.Add(p?.Name ?? t);
                }
            }
            return result;
        }

        private static async Task DoArtistMenu()
        {
            Console.WriteLine("\n== Tracked artists ==");
            Console.WriteLine("1) Add (track + download the whole gallery)");
            Console.WriteLine("2) Add many (from a file)");
            Console.WriteLine("3) List tracked");
            Console.WriteLine("4) Remove (stop tracking)");
            Console.WriteLine("5) Refresh one");
            Console.WriteLine("6) Refresh all");
            Console.WriteLine("0) Back");
            var choice = Prompt("Choose an option", "0");
            if (choice == "0") return;

            // Only prompt for the tracking DB once a real action is chosen (not on "Back") -
            // GetDbPath() itself only prompts the first time it's ever called this session.
            var flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["db"] = GetDbPath() };

            switch (choice)
            {
                case "1":
                    {
                        var providersPath = GetProvidersPath();
                        var providers = LoadProvidersOrThrow(providersPath);
                        var provider = PickProvider(providers);
                        var tag = PromptRequired("Artist tag");
                        var output = PromptRequired("Output folder for this artist");
                        flags["provider"] = provider.Name;
                        flags["providers"] = providersPath;
                        flags["tags"] = tag;
                        flags["output"] = output;
                        await Program.RunArtist("add", flags);
                        break;
                    }
                case "2":
                    {
                        var file = PromptRequired("File (lines of provider,tag,output_dir[,rating])");
                        flags["file"] = file;
                        flags["providers"] = GetProvidersPath();
                        await Program.RunArtist("add-many", flags);
                        break;
                    }
                case "3":
                    await Program.RunArtist("list", flags);
                    break;
                case "4":
                    {
                        var providersPath = GetProvidersPath();
                        var providers = LoadProvidersOrThrow(providersPath);
                        var provider = PickProvider(providers);
                        var tag = PromptRequired("Artist tag");
                        flags["provider"] = provider.Name;
                        flags["tags"] = tag;
                        await Program.RunArtist("remove", flags);
                        break;
                    }
                case "5":
                    {
                        var providersPath = GetProvidersPath();
                        var providers = LoadProvidersOrThrow(providersPath);
                        var provider = PickProvider(providers);
                        var tag = PromptRequired("Artist tag");
                        flags["provider"] = provider.Name;
                        flags["providers"] = providersPath;
                        flags["tags"] = tag;
                        await Program.RunArtist("refresh", flags);
                        break;
                    }
                case "6":
                    flags["all"] = "true";
                    await Program.RunArtist("refresh", flags);
                    break;
                default:
                    Console.WriteLine("Not a valid option.");
                    break;
            }
        }

        private static async Task DoProvidersMenu()
        {
            Console.WriteLine("\n== Providers ==");
            Console.WriteLine("1) List");
            Console.WriteLine("2) Show details");
            Console.WriteLine("3) Check reachability (Cloudflare, etc.)");
            Console.WriteLine("0) Back");
            var choice = Prompt("Choose an option", "0");
            if (choice == "0") return;

            var providersPath = GetProvidersPath();

            switch (choice)
            {
                case "1":
                    await Program.RunProviders(new[] { "list", "--providers", providersPath });
                    break;
                case "2":
                    {
                        var providers = LoadProvidersOrThrow(providersPath);
                        var provider = PickProvider(providers);
                        await Program.RunProviders(new[] { "show", provider.Name, "--providers", providersPath });
                        break;
                    }
                case "3":
                    await Program.RunProviders(new[] { "check", "--providers", providersPath });
                    break;
                default:
                    Console.WriteLine("Not a valid option.");
                    break;
            }
        }

        private static async Task DoCookie()
        {
            Console.WriteLine("\n== Set cookie ==");
            var providersPath = GetProvidersPath();
            var providers = LoadProvidersOrThrow(providersPath);
            var provider = PickProvider(providers);
            var value = PromptRequired("Cookie value");
            Program.RunCookie(new[] { "set", provider.Name, value, "--providers", providersPath });
        }

        private static async Task DoTagsDownload()
        {
            Console.WriteLine("\n== Download tags.xml ==");
            var providersPath = GetProvidersPath();
            var providers = LoadProvidersOrThrow(providersPath);
            var provider = PickProvider(providers);
            var output = Prompt("Save to", "tags.xml");

            var flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["provider"] = provider.Name,
                ["providers"] = providersPath,
                ["output"] = output,
            };
            await Program.RunTagsDownload(flags);
        }

        private static async Task DoScheduleMenu()
        {
            Console.WriteLine("\n== Scheduled auto-refresh ==");
            Console.WriteLine("1) Install (runs 'refresh all tracked artists' automatically)");
            Console.WriteLine("2) Uninstall");
            Console.WriteLine("3) Status");
            Console.WriteLine("0) Back");
            var choice = Prompt("Choose an option", "0");

            switch (choice)
            {
                case "1":
                    {
                        var hours = Prompt("Refresh every how many hours", "24");
                        var dbPath = GetDbPath();
                        var providersPath = GetProvidersPath();
                        var flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["interval-hours"] = hours,
                            ["db"] = dbPath,
                            ["providers"] = providersPath,
                        };
                        Program.RunArtistSchedule("install", flags);
                        break;
                    }
                case "2":
                    Program.RunArtistSchedule("uninstall", new Dictionary<string, string>());
                    break;
                case "3":
                    Program.RunArtistSchedule("status", new Dictionary<string, string>());
                    break;
                case "0":
                    break;
                default:
                    Console.WriteLine("Not a valid option.");
                    break;
            }
        }
    }
}
