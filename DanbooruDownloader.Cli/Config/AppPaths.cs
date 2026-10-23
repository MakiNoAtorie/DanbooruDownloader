using System;
using System.IO;
using System.Text.Json;

namespace DanbooruDownloader.Cli.Config
{
    /// <summary>
    /// Where the CLI keeps its files. A file in the working directory or next to the binary
    /// wins, so existing setups keep working; otherwise the per-user data folder is used, so
    /// the download database and tracked artists do not depend on the working directory.
    /// </summary>
    public static class AppPaths
    {
        public static string DataHome
        {
            get
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DanbooruDownloader");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string Find(string name)
        {
            if (File.Exists(name)) return name;
            var shipped = Path.Combine(AppContext.BaseDirectory, name);
            return File.Exists(shipped) ? shipped : Path.Combine(DataHome, name);
        }

        public static string Db => Find("downloaded.db");
        public static string TagsXml => Find("tags.xml");
        public static string Env => Find(".env");

        public static string DefaultOutput
        {
            get
            {
                if (Directory.Exists("downloads")) return "downloads";
                var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                if (string.IsNullOrEmpty(pictures)) pictures = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Path.Combine(pictures, "DanbooruDownloader");
            }
        }
    }

    /// <summary>Choices the interactive menu offers again as defaults next time.</summary>
    public class UserSettings
    {
        public string LastProvider { get; set; }
        public string LastOutput { get; set; }

        private static string FilePath => AppPaths.Find("settings.json");

        public static UserSettings Load()
        {
            try
            {
                return File.Exists(FilePath) ? JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(FilePath)) ?? new UserSettings() : new UserSettings();
            }
            catch (Exception)
            {
                return new UserSettings();
            }
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
            }
            catch (Exception ex)
            {
                Program.Logger.Warn($"Could not save {FilePath}: {ex.Message}");
            }
        }
    }
}
