using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace DanbooruDownloader.Cli.Scheduling
{
    /// <summary>
    /// Installs/removes a macOS launchd agent that runs "danboo artist refresh --all" on an
    /// interval - the actual automation behind "artist refresh" being schedulable, since a
    /// command you have to remember to run isn't really automatic.
    /// </summary>
    public static class LaunchdScheduler
    {
        private const string Label = "com.danboodownloader.artistrefresh";

        private static string PlistPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", $"{Label}.plist");

        public static (bool ok, string message) Install(int intervalHours, string dbPath, string providersPath, string workingDirectory)
        {
            var (programArgs, error) = ResolveProgramArguments();
            if (error != null) return (false, error);

            var args = new System.Collections.Generic.List<string>(programArgs)
            {
                "artist", "refresh", "--all", "--db", dbPath, "--providers", providersPath
            };

            var plist = BuildPlist(args, intervalHours, workingDirectory);

            var dir = Path.GetDirectoryName(PlistPath);
            Directory.CreateDirectory(dir!);
            File.WriteAllText(PlistPath, plist);

            // Unload first in case an older version is already loaded, then load the new one.
            RunLaunchctl($"unload \"{PlistPath}\"", ignoreErrors: true);
            var (exitCode, output) = RunLaunchctl($"load \"{PlistPath}\"", ignoreErrors: false);

            if (exitCode != 0)
            {
                return (false, $"Wrote {PlistPath} but 'launchctl load' failed: {output}");
            }

            return (true, $"Installed. Refreshing all tracked artists every {intervalHours}h via {PlistPath}.\nLogs: ~/Library/Logs/{Label}.log");
        }

        public static (bool ok, string message) Uninstall()
        {
            if (!File.Exists(PlistPath))
            {
                return (true, "Not installed (no plist found).");
            }

            RunLaunchctl($"unload \"{PlistPath}\"", ignoreErrors: true);
            File.Delete(PlistPath);
            return (true, $"Uninstalled ({PlistPath} removed).");
        }

        public static string Status()
        {
            if (!File.Exists(PlistPath))
            {
                return "Not installed.";
            }

            var (_, output) = RunLaunchctl("list", ignoreErrors: true);
            var loaded = output.Contains(Label);
            return loaded
                ? $"Installed and loaded ({PlistPath})."
                : $"Plist exists at {PlistPath} but is not currently loaded - try 'artist schedule install' again.";
        }

        /// <summary>
        /// Figures out how to re-invoke this same CLI from a launchd job: "dotnet &lt;dll&gt;"
        /// for a framework-dependent build (the common case, `dotnet run`/`dotnet build`
        /// output), or the executable path directly for a self-contained publish.
        /// </summary>
        internal static (string[] args, string error) ResolveProgramArguments()
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath))
            {
                return (null, "Could not determine the current executable path.");
            }

            var exeName = Path.GetFileNameWithoutExtension(processPath);
            if (string.Equals(exeName, "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                // Framework-dependent: re-invoke via "dotnet <dll>".
                var dllPath = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrWhiteSpace(dllPath))
                {
                    return (null, "Could not determine the CLI's .dll path for a 'dotnet <dll>' launchd command.");
                }
                return (new[] { processPath, dllPath }, null);
            }

            // Self-contained/AOT publish: the process itself is the CLI.
            return (new[] { processPath }, null);
        }

        private static string BuildPlist(System.Collections.Generic.List<string> args, int intervalHours, string workingDirectory)
        {
            var sb = new StringBuilder();
            sb.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
            sb.AppendLine("""<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">""");
            sb.AppendLine("""<plist version="1.0">""");
            sb.AppendLine("<dict>");
            sb.AppendLine($"  <key>Label</key><string>{Label}</string>");
            sb.AppendLine("  <key>ProgramArguments</key>");
            sb.AppendLine("  <array>");
            foreach (var a in args)
            {
                sb.AppendLine($"    <string>{System.Security.SecurityElement.Escape(a)}</string>");
            }
            sb.AppendLine("  </array>");
            sb.AppendLine($"  <key>WorkingDirectory</key><string>{System.Security.SecurityElement.Escape(workingDirectory)}</string>");
            sb.AppendLine($"  <key>StartInterval</key><integer>{intervalHours * 3600}</integer>");
            sb.AppendLine("  <key>RunAtLoad</key><false/>");
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", $"{Label}.log");
            sb.AppendLine($"  <key>StandardOutPath</key><string>{System.Security.SecurityElement.Escape(logPath)}</string>");
            sb.AppendLine($"  <key>StandardErrorPath</key><string>{System.Security.SecurityElement.Escape(logPath)}</string>");
            sb.AppendLine("</dict>");
            sb.AppendLine("</plist>");
            return sb.ToString();
        }

        private static (int exitCode, string output) RunLaunchctl(string arguments, bool ignoreErrors)
        {
            try
            {
                var psi = new ProcessStartInfo("launchctl", arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                using var process = Process.Start(psi);
                var stdout = process!.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                return (process.ExitCode, stdout + stderr);
            }
            catch (Exception ex)
            {
                if (ignoreErrors) return (0, ex.Message);
                return (-1, ex.Message);
            }
        }
    }
}
