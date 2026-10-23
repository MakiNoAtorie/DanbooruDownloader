using DanbooruDownloader.Cli.Config;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace DanbooruDownloader.Cli.Scheduling
{
    /// <summary>
    /// Runs "artist refresh --all" on an interval with the platform's own scheduler:
    /// launchd on macOS, Task Scheduler on Windows. Linux gets a crontab line to add.
    /// </summary>
    public static class Scheduler
    {
        private const string TaskName = "DanbooruDownloader Artist Refresh";

        private static string WindowsScript => Path.Combine(AppPaths.DataHome, "artist-refresh.cmd");
        private static string WindowsLog => Path.Combine(AppPaths.DataHome, "artist-refresh.log");

        public static (bool ok, string message) Install(int intervalHours, string dbPath, string providersPath, string workingDirectory)
        {
            if (OperatingSystem.IsMacOS()) return LaunchdScheduler.Install(intervalHours, dbPath, providersPath, workingDirectory);

            var (programArgs, error) = LaunchdScheduler.ResolveProgramArguments();
            if (error != null) return (false, error);
            var command = string.Join(" ", programArgs.Concat(new[] { "artist", "refresh", "--all", "--db", dbPath, "--providers", providersPath }).Select(a => $"\"{a}\""));

            if (!OperatingSystem.IsWindows())
            {
                var hours = intervalHours < 24 ? $"*/{intervalHours} * * *" : "0 * * *";
                return (false, $"Automatic scheduling is built in for macOS and Windows only. Add this line with 'crontab -e':\n0 {hours} cd \"{workingDirectory}\" && {command}");
            }

            // HOURLY accepts 1-23; whole days use DAILY.
            string schedule;
            if (intervalHours < 24) schedule = $"/SC HOURLY /MO {intervalHours}";
            else if (intervalHours % 24 == 0) schedule = $"/SC DAILY /MO {intervalHours / 24}";
            else return (false, "On Windows, --interval-hours must be below 24 or a whole number of days (24, 48, ...).");

            // A script keeps the working directory (for .env and relative paths) and the log,
            // which a task's /TR command line (max 261 characters) has no room for.
            File.WriteAllText(WindowsScript, $"@echo off\r\ncd /d \"{workingDirectory}\"\r\n{command} >> \"{WindowsLog}\" 2>&1\r\n");
            var (exitCode, output) = RunSchtasks($"/Create /F /TN \"{TaskName}\" /TR \"\\\"{WindowsScript}\\\"\" {schedule}");
            return exitCode == 0
                ? (true, $"Installed. Refreshing all tracked artists every {intervalHours}h (Task Scheduler: \"{TaskName}\").\nLog: {WindowsLog}")
                : (false, $"schtasks failed: {output.Trim()}");
        }

        public static (bool ok, string message) Uninstall()
        {
            if (OperatingSystem.IsMacOS()) return LaunchdScheduler.Uninstall();
            if (!OperatingSystem.IsWindows()) return (true, "Nothing to uninstall: remove the line from 'crontab -e'.");

            var (exitCode, _) = RunSchtasks($"/Delete /F /TN \"{TaskName}\"");
            if (File.Exists(WindowsScript)) File.Delete(WindowsScript);
            return (true, exitCode == 0 ? "Uninstalled." : "Not installed.");
        }

        public static string Status()
        {
            if (OperatingSystem.IsMacOS()) return LaunchdScheduler.Status();
            if (!OperatingSystem.IsWindows()) return "Check 'crontab -l' for an 'artist refresh' line.";

            var (exitCode, output) = RunSchtasks($"/Query /TN \"{TaskName}\" /FO LIST");
            if (exitCode != 0) return "Not installed.";
            var next = output.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith("Next Run Time", StringComparison.OrdinalIgnoreCase))?.Trim();
            return $"Installed (Task Scheduler: \"{TaskName}\"). {next}\nLog: {WindowsLog}";
        }

        private static (int exitCode, string output) RunSchtasks(string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks", arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                using var process = Process.Start(psi);
                var output = process!.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                process.WaitForExit();
                return (process.ExitCode, output);
            }
            catch (Exception ex)
            {
                return (1, ex.Message);
            }
        }
    }
}
