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
            if (Verbose) Ui.DimErr(message);
        }

        public void Info(string message) => Console.Error.WriteLine(message);

        public void InfoFormat(string format, params object[] args) => Info(string.Format(format, args));

        public void Warn(string message) => Ui.Warn($"Warning: {message}");

        public void WarnFormat(string format, params object[] args) => Warn(string.Format(format, args));

        public void Error(string message) => Ui.Error($"Error: {message}");

        public void Error(string message, Exception ex) => Error($"{message}: {ex.Message}");
    }
}
