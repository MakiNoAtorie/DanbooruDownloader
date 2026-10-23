using DanbooruDownloader.Cli.Entity;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DanbooruDownloader.Cli.Config
{
    /// <summary>
    /// Per-provider credentials from a gitignored .env file and environment variables, so
    /// secrets stay out of DanbooruProviderList.xml (a tracked file). Variables, where NAME
    /// is the provider name upper-cased with non-alphanumerics as "_" (gelbooru.com ->
    /// GELBOORU_COM): NAME_API_KEY + NAME_USER (API-key login), NAME_COOKIE (cookie login).
    /// Environment variables override the file; both override the provider list in memory only.
    /// </summary>
    public static class EnvFile
    {
        public const string FileName = ".env";

        public static string DefaultPath => File.Exists(AppPaths.Env) ? AppPaths.Env : null;

        public static Dictionary<string, string> Parse(IEnumerable<string> lines)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                if (line.StartsWith("export ")) line = line["export ".Length..];
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var value = line[(eq + 1)..].Trim();
                if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0]) value = value[1..^1];
                values[line[..eq].Trim()] = value;
            }
            return values;
        }

        public static string KeyFor(DanbooruProvider provider) =>
            Regex.Replace(provider.Name.ToUpperInvariant(), "[^A-Z0-9]+", "_").Trim('_');

        /// <summary>
        /// Sets (or, with a null value, removes) these keys in the .env file, keeping its other
        /// lines. Creates the file in the data folder when there is none yet.
        /// </summary>
        public static string Write(params (string key, string value)[] entries)
        {
            var path = AppPaths.Env;
            var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
            foreach (var (key, value) in entries)
            {
                lines.RemoveAll(l => Regex.IsMatch(l, $@"^\s*(export\s+)?{Regex.Escape(key)}\s*=", RegexOptions.IgnoreCase));
                if (value != null) lines.Add($"{key}={value}");
            }
            File.WriteAllLines(path, lines);
            return path;
        }

        public static void Apply(List<DanbooruProvider> providers, string envPath)
        {
            var file = envPath != null ? Parse(File.ReadAllLines(envPath)) : new Dictionary<string, string>();
            string Get(string key)
            {
                var value = Environment.GetEnvironmentVariable(key);
                if (string.IsNullOrWhiteSpace(value)) file.TryGetValue(key, out value);
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }

            foreach (var provider in providers.Where(p => !string.IsNullOrWhiteSpace(p.Name)))
            {
                var key = KeyFor(provider);
                var apiKey = Get(key + "_API_KEY");
                var cookie = Get(key + "_COOKIE");
                if (apiKey != null)
                {
                    provider.LoginType = LoginType.ApiKey;
                    provider.ApiKey = apiKey;
                    provider.UserName = Get(key + "_USER") ?? provider.UserName;
                }
                else if (cookie != null)
                {
                    provider.LoginType = LoginType.Cookie;
                    provider.UserName = cookie;
                }
            }
        }
    }
}
