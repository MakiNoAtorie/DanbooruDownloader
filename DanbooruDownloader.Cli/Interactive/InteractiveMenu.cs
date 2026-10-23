using DanbooruDownloader.Cli.Config;
using DanbooruDownloader.Cli.Data;
using DanbooruDownloader.Cli.Entity;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DanbooruDownloader.Cli.Interactive
{
    /// <summary>
    /// A numbered-menu front end over the flag-based commands - launched when "danboo" is run
    /// with no arguments. Every action builds a command line, shows it, and runs it through
    /// Program.RunCommand, so the menu has no separate logic to keep in sync and teaches the
    /// equivalent command as it goes.
    /// </summary>
    public static class InteractiveMenu
    {
        private static UserSettings _settings = new UserSettings();

        public static async Task<int> RunAsync()
        {
            _settings = UserSettings.Load();
            Ui.Heading("DanbooruDownloader - interactive mode");
            Ui.Dim("Press Enter to accept the value in [brackets]. \"danboo --help\" lists the commands.");

            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("1) Download");
                Console.WriteLine("2) Search");
                Console.WriteLine("3) Refresh tracked artists");
                Console.WriteLine("4) Tracked artists (add, list, refresh one, remove)");
                Console.WriteLine("5) Batch download from a file");
                Console.WriteLine("6) Settings (sites, logins, automatic refresh, tag names)");
                Console.WriteLine("0) Exit");

                try
                {
                    switch (Prompt("Choose", "1"))
                    {
                        case "1": await DoDownload(); break;
                        case "2": await DoSearch(); break;
                        case "3": await Run(new List<string> { "artist", "refresh" }, confirm: false); break;
                        case "4": await DoArtistMenu(); break;
                        case "5": await DoBatch(); break;
                        case "6": await DoSettingsMenu(); break;
                        case "0": case "q": case "exit": case "quit":
                            return 0;
                        default:
                            Console.WriteLine("Not a valid option.");
                            break;
                    }
                }
                catch (EndOfStreamException)
                {
                    return 0;
                }
                catch (Exception ex)
                {
                    Ui.Error($"Error: {ex.Message}");
                }
            }
        }

        // ---- prompt helpers ----

        private static string Prompt(string label, string defaultValue = "")
        {
            Console.Write(string.IsNullOrEmpty(defaultValue) ? $"{label}: " : $"{label} [{defaultValue}]: ");
            var input = Console.ReadLine();
            // Closed stdin returns null forever; stop instead of looping.
            if (input == null) throw new EndOfStreamException("Input closed.");
            return string.IsNullOrWhiteSpace(input) ? defaultValue : input.Trim();
        }

        private static string PromptRequired(string label, string defaultValue = "")
        {
            while (true)
            {
                var value = Prompt(label, defaultValue);
                if (!string.IsNullOrWhiteSpace(value)) return value;
                Console.WriteLine("This is required.");
            }
        }

        private static bool Confirm(string label, bool defaultValue)
        {
            var input = Prompt($"{label} ({(defaultValue ? "Y/n" : "y/N")})");
            if (string.IsNullOrWhiteSpace(input)) return defaultValue;
            return input.StartsWith("y", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Shows numbered options on one line and returns the chosen index.</summary>
        private static int Choose(string label, string[] options, int defaultIndex = 0)
        {
            Console.WriteLine("  " + string.Join("   ", options.Select((o, i) => $"{i + 1}) {o}")));
            while (true)
            {
                if (int.TryParse(Prompt(label, (defaultIndex + 1).ToString()), out var n) && n >= 1 && n <= options.Length) return n - 1;
                Console.WriteLine("Not a valid option.");
            }
        }

        /// <summary>Shows the command line, asks to start it, then runs it like typed arguments.</summary>
        private static async Task Run(List<string> args, bool confirm = true)
        {
            Ui.Dim("> " + Flags.ToCommandLine(args));
            if (confirm && !Confirm("Start", true)) return;
            Console.WriteLine();
            await Program.RunCommand(args.ToArray());
        }

        private static List<DanbooruProvider> LoadProviders()
        {
            var providers = Program.LoadProviders(Program.DefaultProvidersPath, out var error);
            if (error != null) throw new InvalidOperationException(error);
            return providers;
        }

        /// <summary>Numbered providers in columns; takes a number or (part of) a name, defaulting to the last one used.</summary>
        private static DanbooruProvider PickProvider(List<DanbooruProvider> providers)
        {
            var cellWidth = providers.Max(p => p.Name.Length) + 7;
            var columns = Math.Clamp(Math.Min(Ui.Width, 160) / cellWidth, 1, 4);
            for (int i = 0; i < providers.Count; i += columns)
            {
                Console.WriteLine(string.Concat(providers.Skip(i).Take(columns).Select((p, j) => $"  {i + j + 1,2}) {p.Name}".PadRight(cellWidth))).TrimEnd());
            }

            var last = Program.FindProvider(providers, _settings.LastProvider);
            while (true)
            {
                var input = PromptRequired("Site (number or name)", last?.Name ?? "");
                var provider = int.TryParse(input, out var index) && index >= 1 && index <= providers.Count
                    ? providers[index - 1]
                    : null;
                var matches = provider != null ? new List<DanbooruProvider> { provider } : Program.MatchProviders(providers, input);
                if (matches.Count == 1)
                {
                    _settings.LastProvider = matches[0].Name;
                    _settings.Save();
                    if (Program.LoginMissing(matches[0])) Ui.Notice($"{matches[0].Name} needs a login that is not saved yet (Settings > Log in to a site).");
                    return matches[0];
                }
                Console.WriteLine(matches.Count == 0 ? "No site matches that." : $"That matches {string.Join(", ", matches.Select(p => p.Name))}; type more of the name.");
            }
        }

        private static string PickRating(DanbooruProvider provider)
        {
            var options = provider.UsesGeneralSensitiveRatings
                ? new[] { "any", "general", "sensitive", "questionable", "explicit" }
                : new[] { "any", "safe", "questionable", "explicit" };
            var index = Choose("Rating", options);
            return index == 0 ? null : options[index];
        }

        private static readonly string[] HowMuch = { "first page", "every page", "every page and keep it updated (track)" };

        private static List<string> DownloadArgs(DanbooruProvider provider, string tags, string rating, int howMuch)
        {
            var args = new List<string> { "download", "--provider", provider.Name };
            if (!string.IsNullOrWhiteSpace(tags)) args.AddRange(new[] { "--tags", tags });
            if (rating != null) args.AddRange(new[] { "--rating", rating });
            if (howMuch == 1) args.Add("--all-pages");
            if (howMuch == 2) args.Add("--track");
            return args;
        }

        /// <summary>
        /// The remembered output folder, plus the less common options only when asked for,
        /// so a plain download stays a few Enter presses.
        /// </summary>
        private static void AddOptions(List<string> args, bool outputIsBaseFolder = true)
        {
            var output = _settings.LastOutput;
            if (Confirm("Change other options (folder, blacklist, filenames, delay)", false))
            {
                if (outputIsBaseFolder)
                {
                    output = Prompt("Download folder", output ?? AppPaths.DefaultOutput);
                    _settings.LastOutput = Path.GetFullPath(output) == Path.GetFullPath(AppPaths.DefaultOutput) ? null : output;
                    _settings.Save();
                }
                var blacklist = Prompt("Skip posts with any of these tags (comma-separated)");
                if (blacklist.Length > 0) args.AddRange(new[] { "--blacklist", blacklist });
                var format = Prompt("Filename format (Enter for the default; e.g. %artist% - %id%)");
                if (format.Length > 0) args.AddRange(new[] { "--filename-format", format });
                var limit = Prompt("Posts per page (Enter for the site's default)");
                if (limit.Length > 0) args.AddRange(new[] { "--limit", limit });
                var delay = Prompt("Wait between requests in ms", "0");
                if (delay != "0") args.AddRange(new[] { "--delay", delay });
                if (!Confirm("Write a browsable index.html", true)) args.Add("--no-html-index");
            }
            if (outputIsBaseFolder && output != null) args.AddRange(new[] { "--output", output });
        }

        // ---- actions ----

        private static async Task DoDownload()
        {
            Ui.Heading("\n== Download ==");
            var provider = PickProvider(LoadProviders());
            var tags = Prompt("Tags (space-separated; Enter for the newest posts)");
            var howMuch = Choose("How much", HowMuch);
            var args = DownloadArgs(provider, tags, PickRating(provider), howMuch);
            AddOptions(args);
            await Run(args);
        }

        private static async Task DoSearch()
        {
            Ui.Heading("\n== Search ==");
            var provider = PickProvider(LoadProviders());
            var tags = Prompt("Tags (space-separated; Enter for the newest posts)");
            var rating = PickRating(provider);

            var args = new List<string> { "search", "--provider", provider.Name };
            if (!string.IsNullOrWhiteSpace(tags)) args.AddRange(new[] { "--tags", tags });
            if (rating != null) args.AddRange(new[] { "--rating", rating });
            await Run(args, confirm: false);

            Console.WriteLine();
            var next = Choose("Download", new[] { "no" }.Concat(HowMuch).ToArray());
            if (next == 0) return;
            var download = DownloadArgs(provider, tags, rating, next - 1);
            AddOptions(download);
            await Run(download);
        }

        private static async Task DoBatch()
        {
            Ui.Heading("\n== Batch download ==");
            string jobsFile;
            while (!File.Exists(jobsFile = PromptRequired("File with one tag search per line")))
            {
                Console.WriteLine("File not found.");
            }

            var providers = LoadProviders();
            for (int i = 0; i < providers.Count; i++) Console.WriteLine($"  {i + 1,2}) {providers[i].Name}");
            var selection = PromptRequired("Sites to run each line on (numbers or names, comma-separated)", Program.FindProvider(providers, _settings.LastProvider)?.Name ?? "");
            var names = selection.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0)
                .Select(s => int.TryParse(s, out var n) && n >= 1 && n <= providers.Count ? providers[n - 1].Name : s);

            var args = new List<string> { "batch", "--jobs", jobsFile, "--provider-names", string.Join(",", names) };
            var howMuch = Choose("How much per line", HowMuch);
            if (howMuch == 1) args.Add("--all-pages");
            if (howMuch == 2) args.Add("--track");
            AddOptions(args);
            await Run(args);
        }

        private static TrackedArtist PickTracked()
        {
            var artists = new SqliteProvider(AppPaths.Db).ListTrackedArtists();
            if (artists.Count == 0)
            {
                Console.WriteLine("No tracked artists yet.");
                return null;
            }
            for (int i = 0; i < artists.Count; i++) Console.WriteLine($"  {i + 1,2}) {artists[i].Tag}  ({artists[i].ProviderName})");
            while (true)
            {
                var input = Prompt("Artist number (0 to go back)", "0");
                if (input == "0") return null;
                if (int.TryParse(input, out var n) && n >= 1 && n <= artists.Count) return artists[n - 1];
                Console.WriteLine("Not a valid number.");
            }
        }

        private static async Task DoArtistMenu()
        {
            Ui.Heading("\n== Tracked artists ==");
            Console.WriteLine("1) Add an artist (download everything, then keep it updated)");
            Console.WriteLine("2) Add many from a file");
            Console.WriteLine("3) List");
            Console.WriteLine("4) Refresh one");
            Console.WriteLine("5) Remove");
            Console.WriteLine("0) Back");

            switch (Prompt("Choose", "0"))
            {
                case "1":
                    {
                        var provider = PickProvider(LoadProviders());
                        var tag = PromptRequired("Artist tag");
                        var args = new List<string> { "artist", "add", provider.Name, tag };
                        var rating = PickRating(provider);
                        if (rating != null) args.AddRange(new[] { "--rating", rating });
                        if (_settings.LastOutput != null) args.AddRange(new[] { "--output", Path.Combine(_settings.LastOutput, Program.FolderForSearch(tag)) });
                        AddOptions(args, outputIsBaseFolder: false);
                        await Run(args);
                        break;
                    }
                case "2":
                    {
                        var file = PromptRequired("File with lines of provider,artist,output_dir[,rating]");
                        await Run(new List<string> { "artist", "add-many", "--file", file });
                        break;
                    }
                case "3":
                    await Run(new List<string> { "artist", "list" }, confirm: false);
                    break;
                case "4":
                    {
                        var artist = PickTracked();
                        if (artist != null) await Run(new List<string> { "artist", "refresh", artist.ProviderName, artist.Tag }, confirm: false);
                        break;
                    }
                case "5":
                    {
                        var artist = PickTracked();
                        if (artist != null) await Run(new List<string> { "artist", "remove", artist.ProviderName, artist.Tag });
                        break;
                    }
                case "0":
                    break;
                default:
                    Console.WriteLine("Not a valid option.");
                    break;
            }
        }

        private static async Task DoSettingsMenu()
        {
            Ui.Heading("\n== Settings ==");
            Ui.Dim($"Data folder: {AppPaths.DataHome}");
            Ui.Dim($"Downloads:   {Path.GetFullPath(_settings.LastOutput ?? AppPaths.DefaultOutput)}");
            Console.WriteLine("1) List sites");
            Console.WriteLine("2) Site details");
            Console.WriteLine("3) Check which sites are reachable");
            Console.WriteLine("4) Log in to a site (API key or cookie)");
            Console.WriteLine("5) Automatic refresh of tracked artists");
            Console.WriteLine("6) Download tag names for %artist%-style filenames");
            Console.WriteLine("0) Back");

            switch (Prompt("Choose", "0"))
            {
                case "1":
                    await Run(new List<string> { "providers", "list" }, confirm: false);
                    break;
                case "2":
                    await Run(new List<string> { "providers", "show", PickProvider(LoadProviders()).Name }, confirm: false);
                    break;
                case "3":
                    await Run(new List<string> { "providers", "check" }, confirm: false);
                    break;
                case "4":
                    await Run(new List<string> { "login", PickProvider(LoadProviders()).Name }, confirm: false);
                    break;
                case "5":
                    {
                        await Run(new List<string> { "artist", "schedule", "status" }, confirm: false);
                        var action = Choose("Change", new[] { "nothing", "install or change the interval", "remove" });
                        if (action == 1) await Run(new List<string> { "artist", "schedule", "install", "--interval-hours", Prompt("Refresh every how many hours", "24") });
                        if (action == 2) await Run(new List<string> { "artist", "schedule", "uninstall" });
                        break;
                    }
                case "6":
                    await Run(new List<string> { "tags", "download", "--provider", PickProvider(LoadProviders()).Name });
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
