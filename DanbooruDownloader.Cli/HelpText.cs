using DanbooruDownloader.Cli.Config;
using System.Collections.Generic;

namespace DanbooruDownloader.Cli
{
    public static class HelpText
    {
        public static string General => $"""
            danboo - download images from Danbooru-style sites

            Usage: danboo <command> [options]       (no command: interactive menu)

            Commands:
              download    Download a tag search: one page, every page, or tracked
              search      List posts without downloading them
              batch       Download many tag searches listed in a file
              artist      Track artists and fetch only their new posts (add, list, refresh, remove, schedule)
              login       Save an API key or cookie for a site
              providers   List, show and check the configured sites
              tags        Download tag names for %artist%-style filename tokens

            Run "danboo <command> --help" for its options. "danboo --version" prints the version.

            Data folder:  {AppPaths.DataHome}
            Downloads:    {System.IO.Path.GetFullPath(AppPaths.DefaultOutput)}
            """;

        private const string Target = """
              --provider <name>      Site from "danboo providers list"; a unique part of the name works (e621, yandere)
              --tags "<tags>"        Space-separated tags; leave out for the newest posts
            """;

        private const string Filters = """
              --rating <r>           safe | questionable | explicit; Danbooru and Gelbooru also general | sensitive
              --not-rating           Exclude --rating instead
              --limit <n>            Posts per page (default: the site's own)
              --page <n>             Page to start at; 1 is the first page on every site
              --blacklist <t,t>      Skip posts that have any of these tags
              --blacklist-regex <r>  Same, as a regular expression
              --retry <n>            Retries per request (default 3)
              --retry-delay <ms>     Wait between retries (default 3000)
            """;

        private static string Download => $"""
              --output <dir>         Base folder (default: {AppPaths.DefaultOutput})
              --all-pages            Download every page, not only the first
              --track                Download every page and remember the search for "artist refresh"
            {Gallery}
            """;

        private const string Gallery = """
              --filename-format <f>  %token% template (default: %searchtag%/%rating% - %id% - %tags%)
              --max-pages <n>        Page cap when walking every page (default 1000)
              --force                Download again even if already downloaded
              --dedup-by-md5         Also skip images already downloaded from another site
              --extension-filter <r> Only download file extensions that match this regex
              --exclude-extension    Skip matching extensions instead
              --no-html-index        Do not write a browsable index.html into the output folder
              --delay <ms>           Wait between requests (default 0; 150 for Gelbooru with an API key)
            """;

        private static string Common => $"""
              --db <path>            Download database (default: {AppPaths.Db})
              --providers <path>     Site list (default: {Program.DefaultProvidersPath})
              -v, --verbose          Also show skipped posts and request URLs
            """;

        public static readonly Dictionary<string, System.Func<string>> Commands = new()
        {
            ["download"] = () => $"""
                Usage: danboo download --provider <name> [--tags "<tags>"] [options]

                Downloads the first page of results. --all-pages gets every page; --track also
                remembers the search, so "danboo artist refresh" later gets only new posts.

                {Target}
                {Filters}
                {Download}
                {Common}
                """,
            ["search"] = () => $"""
                Usage: danboo search --provider <name> [--tags "<tags>"] [options]

                Lists one page of posts without downloading them.

                {Target}
                {Filters}
                  --json                 Print the posts as JSON
                  --providers <path>     Site list (default: {Program.DefaultProvidersPath})
                  -v, --verbose          Show request URLs
                """,
            ["batch"] = () => $"""
                Usage: danboo batch --jobs <file> --provider-names <p1,p2,...> [options]

                Runs each line of --jobs (one tag search per line; blank lines and lines that
                start with # are skipped) against each provider in --provider-names.

                  --jobs <file>          The file of tag searches
                  --provider-names <p>   Comma-separated sites; unique parts of names work
                {Filters}
                {Download}
                {Common}
                """,
            ["artist"] = () => $"""
                Usage:
                  danboo artist add <provider> <artist> [--output <dir>] [options]
                  danboo artist add-many --file <path> [options]
                  danboo artist list
                  danboo artist refresh [<provider> <artist> | --all] [options]
                  danboo artist remove <provider> <artist>
                  danboo artist schedule install [--interval-hours <n>] | uninstall | status

                "add" downloads an artist's whole gallery and remembers it. "refresh" goes back
                to new posts only, page by page, until a page has nothing new. "add" is the same
                as "danboo download --track", but --output is the artist's own folder (default:
                {AppPaths.DefaultOutput}/<artist>).

                "add-many" reads lines of "provider,artist,output_dir[,rating]".

                "schedule install" runs "artist refresh --all" every --interval-hours (default 24)
                with launchd on macOS or Task Scheduler on Windows.

                --provider and --tags still work in place of <provider> <artist>.

                  --output <dir>         ("add" only) The artist's folder
                {Filters}
                {Gallery}
                {Common}
                """,
            ["login"] = () => $"""
                Usage:
                  danboo login <provider>                       Asks for what the site needs
                  danboo apikey set <provider> [<key> <user>]   API key login (user: Danbooru username or Gelbooru user ID)
                  danboo cookie set <provider> [<cookie>]       Cookie login (copy the Cookie header from your browser)

                Values left out are asked for without echoing them. They are saved in the .env
                file ({AppPaths.Env}), not in DanbooruProviderList.xml. In .env, NAME is the
                provider name upper-cased with other characters as "_" (gelbooru.com -> GELBOORU_COM):
                  NAME_API_KEY=...  NAME_USER=...   API key login
                  NAME_COOKIE=...                   cookie login
                Environment variables of the same names take precedence over the file.

                  --providers <path>     Site list (default: {Program.DefaultProvidersPath})
                """,
            ["providers"] = () => $"""
                Usage: danboo providers list | show <name> | check

                  list    The configured sites and whether their login is set
                  show    One site's settings
                  check   Contacts each site and reports blocked or unreachable ones

                  --providers <path>     Site list (default: {Program.DefaultProvidersPath})
                """,
            ["tags"] = () => $"""
                Usage: danboo tags download --provider <name> [--output <file>] [options]

                Saves a site's tag names and types, so %artist%, %character%, %copyright% and
                %circle% filename tokens resolve. Danbooru, e621 and Gelbooru do not need this.
                download --filename-format with these tokens fetches it on first use.

                  --output <file>        Default: {AppPaths.TagsXml}
                  --limit <n>            Tags per request (default 100)
                  --max-pages <n>        Pages per tag type (default 50)
                  --delay <ms>           Wait between requests
                  --providers <path>     Site list (default: {Program.DefaultProvidersPath})
                """,
        };

        static HelpText()
        {
            Commands["cookie"] = Commands["login"];
            Commands["apikey"] = Commands["login"];
        }
    }
}
