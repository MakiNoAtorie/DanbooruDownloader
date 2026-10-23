using DanbooruDownloader.Cli.DAO;
using DanbooruDownloader.Cli.Entity;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace DanbooruDownloader.Cli.Engine
{
    public static class ShimmieEngine
    {
        private static readonly XNamespace Media = "http://search.yahoo.com/mrss";

        /// <summary>
        /// Shimmie2 RSS feed: item title is "id - tags", the file is media:content (or, on
        /// older feeds, enclosure).
        /// </summary>
        public static List<DanbooruPost> ParseRSS(string xmldoc, DanbooruPostDaoOption option)
        {
            // DtdProcessing.Ignore: DanbooruPostDao inserts an external DOCTYPE that is not resolvable here.
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore };
            using var reader = XmlReader.Create(new StringReader(xmldoc), settings);
            var doc = XDocument.Load(reader);

            var posts = new List<DanbooruPost>();
            foreach (var item in doc.Descendants("item"))
            {
                var titleData = (item.Element("title")?.Value ?? "").Split('-', 2);
                var fileUrl = item.Element(Media + "content")?.Attribute("url")?.Value
                    ?? item.Element("enclosure")?.Attribute("url")?.Value;

                var post = new DanbooruPost
                {
                    Id = titleData[0].Trim(),
                    Tags = titleData.Length > 1 ? titleData[1].Trim() : "",
                    Referer = AppendHttp(item.Element("link")?.Value, option.Provider),
                    CreatedAt = item.Element("pubDate")?.Value,
                    PreviewUrl = AppendHttp(item.Element(Media + "thumbnail")?.Attribute("url")?.Value, option.Provider),
                    FileUrl = AppendHttp(fileUrl, option.Provider),
                    Provider = option.Provider,
                    Query = option.Query,
                    SearchTags = option.SearchTags,
                };
                post.TagsEntity = Helper.ParseTags(post.Tags, option.Provider);
                post.Hidden = Helper.CheckBlacklistedTag(post, option);
                post.CreatedAtDateTime = DanbooruPostDao.ParseDateTime(post.CreatedAt, option.Provider);
                posts.Add(post);
            }
            return posts;
        }

        private static string AppendHttp(string url, DanbooruProvider provider)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;
            return url.StartsWith("http") ? url : provider.Url + url;
        }

        public static string GetQueryString(DanbooruProvider provider, DanbooruSearchParam query)
        {
            var tags = query.Tag ?? "";
            while (tags.Contains("  "))
            {
                tags = tags.Replace("  ", " ");
            }
            tags = tags.Trim();
            tags = Helper.UrlEncode(tags);

            var page = 1;
            if (query.Page.HasValue && query.Page > 0)
            {
                page = query.Page.Value;
            }

            var queryStr = tags;
            if (!String.IsNullOrWhiteSpace(tags)) queryStr += "/";
            queryStr += page;

            return queryStr;
        }
    }
}
