using DanbooruDownloader.Cli.DAO;
using DanbooruDownloader.Cli.Entity;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace DanbooruDownloader.Cli.Engine
{
    /// <summary>
    /// Parses post-list JSON in the shapes boards use today: Danbooru 2 (/posts.json, an
    /// array with tag_string_* per category), e621 (/posts.json, {"posts": [...]} with nested
    /// file/preview/sample/tags objects) and Moebooru (/post/index.json, a flat array).
    /// Error responses are objects with a message/reason instead of posts.
    /// </summary>
    public static class DanbooruJsonParser
    {
        // Category names as Danbooru (tag_string_<name>) and e621 (tags.<name>) spell them.
        private static readonly (string Name, DanbooruTagType Type)[] Categories =
        {
            ("artist", DanbooruTagType.Artist),
            ("copyright", DanbooruTagType.Copyright),
            ("character", DanbooruTagType.Character),
            ("meta", DanbooruTagType.Faults),
            ("general", DanbooruTagType.General),
            ("species", DanbooruTagType.General),
            ("lore", DanbooruTagType.General),
            ("invalid", DanbooruTagType.General),
        };

        public static List<DanbooruPost> Parse(string json, DanbooruPostDaoOption option, out string errorMessage)
        {
            errorMessage = null;
            var posts = new List<DanbooruPost>();
            if (string.IsNullOrWhiteSpace(json)) return posts;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            JsonElement list;
            if (root.ValueKind == JsonValueKind.Array)
            {
                list = root;
            }
            else if (Prop(root, "posts") is JsonElement p && p.ValueKind == JsonValueKind.Array)
            {
                list = p;
            }
            else
            {
                errorMessage = $"{option.Provider.Name} API error: {Str(root, "message", "reason", "error") ?? "unexpected response"}";
                return posts;
            }

            foreach (var el in list.EnumerateArray())
            {
                posts.Add(ParsePost(el, option));
            }
            return posts;
        }

        private static DanbooruPost ParsePost(JsonElement el, DanbooruPostDaoOption option)
        {
            var provider = option.Provider;
            var post = new DanbooruPost
            {
                Id = Str(el, "id"),
                Source = Str(el, "source") ?? First(el, "sources"),
                Score = Str(el, "score.total", "score"),
                Rating = Str(el, "rating"),
                MD5 = Str(el, "md5", "file.md5"),
                FileUrl = Url(Str(el, "file_url", "file.url"), provider),
                Width = Int(el, "image_width", "width", "file.width"),
                Height = Int(el, "image_height", "height", "file.height"),
                Filesize = Int(el, "file_size", "file.size"),
                PreviewUrl = Url(Str(el, "preview_file_url", "preview_url", "preview.url"), provider),
                PreviewWidth = Int(el, "preview_width", "preview.width"),
                PreviewHeight = Int(el, "preview_height", "preview.height"),
                SampleUrl = Url(Str(el, "large_file_url", "sample_url", "sample.url"), provider),
                SampleWidth = Int(el, "sample_width", "sample.width"),
                SampleHeight = Int(el, "sample_height", "sample.height"),
                JpegUrl = Url(Str(el, "jpeg_url"), provider),
                JpegWidth = Int(el, "jpeg_width"),
                JpegHeight = Int(el, "jpeg_height"),
                ParentId = Str(el, "parent_id", "relationships.parent_id"),
                HasChildren = Str(el, "has_children", "relationships.has_children") == "true",
                CreatorId = Str(el, "uploader_id", "creator_id"),
                CreatedAt = Str(el, "created_at"),
                Provider = provider,
                Query = option.Query,
                SearchTags = option.SearchTags,
            };
            post.Status = Str(el, "status")
                ?? (Str(el, "is_deleted", "flags.deleted") == "true" ? "deleted"
                : Str(el, "is_pending", "flags.pending") == "true" ? "pending"
                : "active");
            post.CreatedAtDateTime = ParseDate(post.CreatedAt, provider);

            // Boards that send tag categories with the post need no tags.xml for %artist% etc.
            var categorized = CategorizedTags(el);
            if (categorized.Count > 0)
            {
                post.TagsEntity = categorized;
                post.Tags = string.Join(" ", categorized.Select(t => t.Name));
            }
            else
            {
                post.Tags = Str(el, "tag_string", "tags") ?? "";
                post.TagsEntity = Helper.ParseTags(post.Tags, provider);
            }

            post.Hidden = Helper.CheckBlacklistedTag(post, option);
            var postPath = provider.QueryStringJson?.Contains("/posts") == true ? "/posts/" : "/post/show/";
            post.Referer = provider.Url + postPath + post.Id;
            return post;
        }

        private static List<DanbooruTag> CategorizedTags(JsonElement el)
        {
            var tags = new List<DanbooruTag>();
            foreach (var (name, type) in Categories)
            {
                IEnumerable<string> names = Prop(el, "tags." + name) is JsonElement arr && arr.ValueKind == JsonValueKind.Array
                    ? arr.EnumerateArray().Select(x => x.GetString())
                    : (Str(el, "tag_string_" + name) ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                tags.AddRange(names
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Select(n => new DanbooruTag { Name = n, Type = type, Count = -1, Id = "-1" }));
            }
            return tags;
        }

        private static DateTime ParseDate(string value, DanbooruProvider provider)
        {
            if (string.IsNullOrWhiteSpace(value)) return DateTime.MinValue;
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
                return DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
                return dto.LocalDateTime;
            return DanbooruPostDao.ParseDateTime(value, provider);
        }

        private static string Url(string url, DanbooruProvider provider) => Helper.FixUrl(url, useHttps: true, hostname: provider.Url);

        // Dotted path into nested objects, e.g. "file.url"; null when missing or JSON null.
        private static JsonElement? Prop(JsonElement el, string path)
        {
            foreach (var part in path.Split('.'))
            {
                if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(part, out el)) return null;
            }
            return el.ValueKind == JsonValueKind.Null ? null : el;
        }

        // First non-empty scalar among the paths, as text.
        private static string Str(JsonElement el, params string[] paths)
        {
            foreach (var path in paths)
            {
                if (Prop(el, path) is not JsonElement v) continue;
                switch (v.ValueKind)
                {
                    case JsonValueKind.String:
                        var s = v.GetString();
                        if (!string.IsNullOrEmpty(s)) return s;
                        break;
                    case JsonValueKind.Number:
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        return v.GetRawText();
                }
            }
            return null;
        }

        private static int Int(JsonElement el, params string[] paths) =>
            int.TryParse(Str(el, paths), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : -1;

        private static string First(JsonElement el, string path) =>
            Prop(el, path) is JsonElement arr && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0
                ? arr[0].GetString()
                : null;
    }
}
