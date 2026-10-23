using System;
using System.IO;
using System.Text;

namespace DanbooruDownloader.Cli
{
    /// <summary>Console output with color only on a real terminal (https://no-color.org).</summary>
    public static class Ui
    {
        private static readonly bool NoColor = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
        private static readonly bool OutColor = !NoColor && !Console.IsOutputRedirected;
        private static readonly bool ErrColor = !NoColor && !Console.IsErrorRedirected;

        public static void Line(string message) => Console.WriteLine(message);
        public static void Success(string message) => Write(Console.Out, OutColor, ConsoleColor.Green, message);
        public static void Dim(string message) => Write(Console.Out, OutColor, ConsoleColor.DarkGray, message);
        public static void Notice(string message) => Write(Console.Out, OutColor, ConsoleColor.Yellow, message);
        public static void Heading(string message) => Write(Console.Out, OutColor, ConsoleColor.Cyan, message);
        public static void Error(string message) => Write(Console.Error, ErrColor, ConsoleColor.Red, message);
        public static void Warn(string message) => Write(Console.Error, ErrColor, ConsoleColor.Yellow, message);
        public static void DimErr(string message) => Write(Console.Error, ErrColor, ConsoleColor.DarkGray, message);

        private static void Write(TextWriter writer, bool color, ConsoleColor foreground, string message)
        {
            if (!color)
            {
                writer.WriteLine(message);
                return;
            }
            Console.ForegroundColor = foreground;
            writer.WriteLine(message);
            Console.ResetColor();
        }

        /// <summary>Usable line width; unlimited when output goes to a file or pipe.</summary>
        public static int Width
        {
            get
            {
                if (Console.IsOutputRedirected) return int.MaxValue;
                try { return Math.Max(40, Console.WindowWidth - 1); }
                catch (IOException) { return 120; }
            }
        }

        public static string Fit(string text, int width) =>
            width <= 1 || text.Length <= width ? text : text[..(width - 1)] + "…";

        public static string Size(long bytes) => bytes switch
        {
            >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
            >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
            >= 1L << 10 => $"{bytes / 1024.0:0} KB",
            _ => $"{bytes} B",
        };

        /// <summary>Reads a line without echoing it, so secrets stay off the screen and out of shell history.</summary>
        public static string ReadSecret(string label)
        {
            Console.Write($"{label}: ");
            if (Console.IsInputRedirected) return Console.ReadLine()?.Trim();

            var sb = new StringBuilder();
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter) break;
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (sb.Length > 0) sb.Length--;
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    sb.Append(key.KeyChar);
                }
            }
            Console.WriteLine();
            return sb.ToString().Trim();
        }
    }
}
