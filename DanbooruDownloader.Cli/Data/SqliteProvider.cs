using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace DanbooruDownloader.Cli.Data
{
    // Port of DanbooruDBProvider/SQLiteProvider.cs onto Microsoft.Data.Sqlite (which has
    // native macOS/ARM64 support, unlike the WinForms app's Stub.System.Data.SQLite.Core -
    // a Windows-only native interop package).
    public class SqliteProvider
    {
        private readonly string connectionString;

        public SqliteProvider(string dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath))
            {
                dbPath = "sqllite.db";
            }
            this.connectionString = "Data Source=" + dbPath;

            var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }

        private const string CREATE_SQL = @"
CREATE TABLE IF NOT EXISTS downloaded_files (
    id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT UNIQUE,
    provider_name TEXT NOT NULL,
    post_id TEXT NOT NULL,
    filename TEXT NOT NULL,
    path TEXT NOT NULL,
    download_timestamp TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
)
";

        private const string CREATE_TRACKED_ARTISTS_SQL = @"
CREATE TABLE IF NOT EXISTS tracked_artists (
    id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT UNIQUE,
    provider_name TEXT NOT NULL,
    tag TEXT NOT NULL,
    output_dir TEXT NOT NULL,
    filename_format TEXT,
    rating TEXT,
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_refreshed_at TEXT,
    UNIQUE(provider_name, tag)
)
";

        public bool Create()
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            var cmd = connection.CreateCommand();
            cmd.CommandText = CREATE_SQL + ";" + CREATE_TRACKED_ARTISTS_SQL;
            var result = cmd.ExecuteNonQuery();
            return result > -1;
        }

        private const string INSERT_SQL = @"
INSERT INTO downloaded_files (provider_name, post_id, filename, path)
VALUES (@provider_name, @post_id, @filename, @path)
";

        public bool Insert(string providerName, string postId, string filename, string path)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            var cmd = connection.CreateCommand();
            cmd.CommandText = INSERT_SQL;
            cmd.Parameters.AddWithValue("@provider_name", providerName);
            cmd.Parameters.AddWithValue("@post_id", postId);
            cmd.Parameters.AddWithValue("@filename", filename);
            cmd.Parameters.AddWithValue("@path", path);
            var result = cmd.ExecuteNonQuery();
            return result > -1;
        }

        private const string SELECT_SQL = @"
SELECT * FROM downloaded_files
WHERE provider_name = @provider_name and post_id = @post_id
";

        public List<DownloadedFile> GetDownloadedFileByProviderAndPostId(string providerName, string postId)
        {
            var result = new List<DownloadedFile>();
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            var cmd = connection.CreateCommand();
            cmd.CommandText = SELECT_SQL;
            cmd.Parameters.AddWithValue("@provider_name", providerName);
            cmd.Parameters.AddWithValue("@post_id", postId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new DownloadedFile
                {
                    Id = reader.GetInt32(0),
                    ProviderName = reader.GetString(1),
                    PostId = reader.GetString(2),
                    Filename = reader.GetString(3),
                    Path = reader.GetString(4),
                    Timestamp = DateTime.ParseExact(reader.GetString(5), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                });
            }
            return result;
        }

        private const string UPSERT_TRACKED_ARTIST_SQL = @"
INSERT INTO tracked_artists (provider_name, tag, output_dir, filename_format, rating)
VALUES (@provider_name, @tag, @output_dir, @filename_format, @rating)
ON CONFLICT(provider_name, tag) DO UPDATE SET
    output_dir = excluded.output_dir,
    filename_format = excluded.filename_format,
    rating = excluded.rating
";

        public void UpsertTrackedArtist(string providerName, string tag, string outputDir, string filenameFormat, string rating)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            var cmd = connection.CreateCommand();
            cmd.CommandText = UPSERT_TRACKED_ARTIST_SQL;
            cmd.Parameters.AddWithValue("@provider_name", providerName);
            cmd.Parameters.AddWithValue("@tag", tag);
            cmd.Parameters.AddWithValue("@output_dir", outputDir);
            cmd.Parameters.AddWithValue("@filename_format", (object)filenameFormat ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@rating", (object)rating ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        public void TouchTrackedArtistRefreshed(string providerName, string tag)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE tracked_artists SET last_refreshed_at = CURRENT_TIMESTAMP WHERE provider_name = @provider_name AND tag = @tag";
            cmd.Parameters.AddWithValue("@provider_name", providerName);
            cmd.Parameters.AddWithValue("@tag", tag);
            cmd.ExecuteNonQuery();
        }

        public bool RemoveTrackedArtist(string providerName, string tag)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM tracked_artists WHERE provider_name = @provider_name AND tag = @tag";
            cmd.Parameters.AddWithValue("@provider_name", providerName);
            cmd.Parameters.AddWithValue("@tag", tag);
            return cmd.ExecuteNonQuery() > 0;
        }

        public List<TrackedArtist> ListTrackedArtists()
        {
            var result = new List<TrackedArtist>();
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT id, provider_name, tag, output_dir, filename_format, rating, created_at, last_refreshed_at FROM tracked_artists ORDER BY provider_name, tag";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(ReadTrackedArtist(reader));
            }
            return result;
        }

        public TrackedArtist GetTrackedArtist(string providerName, string tag)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT id, provider_name, tag, output_dir, filename_format, rating, created_at, last_refreshed_at FROM tracked_artists WHERE provider_name = @provider_name AND tag = @tag";
            cmd.Parameters.AddWithValue("@provider_name", providerName);
            cmd.Parameters.AddWithValue("@tag", tag);

            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadTrackedArtist(reader) : null;
        }

        private static TrackedArtist ReadTrackedArtist(SqliteDataReader reader)
        {
            return new TrackedArtist
            {
                Id = reader.GetInt32(0),
                ProviderName = reader.GetString(1),
                Tag = reader.GetString(2),
                OutputDir = reader.GetString(3),
                FilenameFormat = reader.IsDBNull(4) ? null : reader.GetString(4),
                Rating = reader.IsDBNull(5) ? null : reader.GetString(5),
                CreatedAt = DateTime.ParseExact(reader.GetString(6), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                LastRefreshedAt = reader.IsDBNull(7) ? null : DateTime.ParseExact(reader.GetString(7), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            };
        }
    }
}
