using System;
using System.Collections.Generic;
using System.Linq;

namespace DanbooruDownloader.Cli
{
    /// <summary>A mistake in how a command was called; Main prints the message and exits 2.</summary>
    public class UsageException : Exception
    {
        public UsageException(string message) : base(message) { }
    }

    public static class Flags
    {
        /// <summary>
        /// Parses --flags against a spec such as "provider= tags= force": a trailing "=" means
        /// the flag takes a value. Unknown flags and stray arguments are errors, because a
        /// silently ignored typo (--tag for --tags) downloads the wrong posts.
        /// </summary>
        public static Dictionary<string, string> Parse(string[] args, string spec, List<string> positionals = null)
        {
            var valued = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var switches = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "v", "verbose" };
            foreach (var name in spec.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (name.EndsWith('=')) valued.Add(name[..^1]); else switches.Add(name);
            }

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (!IsFlag(arg))
                {
                    if (positionals == null) throw new UsageException($"Unexpected argument '{arg}'. Values go after a flag, e.g. --tags \"{arg}\".");
                    positionals.Add(arg);
                    continue;
                }

                var key = arg.TrimStart('-');
                string value = null;
                var eq = key.IndexOf('=');
                if (eq > 0) (key, value) = (key[..eq], key[(eq + 1)..]);

                if (valued.Contains(key))
                {
                    if (value == null)
                    {
                        if (i + 1 >= args.Length || args[i + 1].StartsWith("--")) throw new UsageException($"--{key} needs a value.");
                        value = args[++i];
                    }
                    result[key] = value;
                }
                else if (switches.Contains(key) && value == null)
                {
                    result[key] = "true";
                }
                else
                {
                    var known = valued.Concat(switches).Where(k => k.Length > 1);
                    var guess = known.OrderBy(k => Distance(k, key)).FirstOrDefault();
                    var hint = guess != null && (Distance(guess, key) <= 2 || guess.StartsWith(key, StringComparison.OrdinalIgnoreCase)) ? $" Did you mean --{guess}?" : "";
                    throw new UsageException($"Unknown option {arg}.{hint}");
                }
            }
            return result;
        }

        private static bool IsFlag(string s) => s.StartsWith("--") || (s.Length == 2 && s[0] == '-' && char.IsLetter(s[1]));

        public static int? Int(Dictionary<string, string> flags, string key, int min = 0)
        {
            if (!flags.TryGetValue(key, out var raw)) return null;
            if (!int.TryParse(raw, out var value)) throw new UsageException($"--{key} must be a whole number (got '{raw}').");
            if (value < min) throw new UsageException($"--{key} must be at least {min}.");
            return value;
        }

        public static int Int(Dictionary<string, string> flags, string key, int defaultValue, int min) => Int(flags, key, min) ?? defaultValue;

        internal static int Distance(string a, string b)
        {
            a = a.ToLowerInvariant();
            b = b.ToLowerInvariant();
            var d = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) d[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                int prev = d[0];
                d[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int temp = d[j];
                    d[j] = Math.Min(Math.Min(d[j] + 1, d[j - 1] + 1), prev + (a[i - 1] == b[j - 1] ? 0 : 1));
                    prev = temp;
                }
            }
            return d[b.Length];
        }

        /// <summary>Renders arguments as a command line a user can copy, e.g. into a script.</summary>
        public static string ToCommandLine(IEnumerable<string> args) =>
            "danboo " + string.Join(" ", args.Select(a => a.Length == 0 || a.Any(c => char.IsWhiteSpace(c) || c is '"' or '(' or ')' or '&' or '|' or '*') ? $"\"{a.Replace("\"", "\\\"")}\"" : a));
    }
}
