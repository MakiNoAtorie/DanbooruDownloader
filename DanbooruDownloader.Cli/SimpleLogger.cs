using System;

namespace DanbooruDownloader.Cli
{
    // Minimal stand-in for the WinForms app's log4net-backed Program.Logger,
    // used unchanged by ported Engine/DAO/Helper code.
    public sealed class SimpleLogger
    {
        public bool Verbose { get; set; }

        public void Debug(string message)
        {
            if (Verbose) Write("DEBUG", message);
        }

        public void Info(string message) => Write("INFO", message);

        public void InfoFormat(string format, params object[] args) => Write("INFO", string.Format(format, args));

        public void Warn(string message) => Write("WARN", message);

        public void WarnFormat(string format, params object[] args) => Write("WARN", string.Format(format, args));

        public void Error(string message) => Write("ERROR", message);

        public void Error(string message, Exception ex) => Write("ERROR", $"{message}: {ex.Message}");

        private static void Write(string level, string message)
        {
            Console.Error.WriteLine($"[{level}] {message}");
        }
    }
}
