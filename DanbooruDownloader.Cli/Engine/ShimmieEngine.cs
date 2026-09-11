using DanbooruDownloader.Cli.Entity;
using System;
using System.Collections.Generic;

namespace DanbooruDownloader.Cli.Engine
{
    public static class ShimmieEngine
    {
        /// <summary>
        /// RSS-feed post-list parsing (Shimmie2 boards) is deferred - not needed for the
        /// XML/JSON/HTML providers exercised so far. GetQueryString below (used by every
        /// Shimmie2 provider's query building, regardless of parse method) is fully ported.
        /// </summary>
        public static List<DanbooruPost> ParseRSS(string xmldoc, DanbooruPostDaoOption option)
        {
            throw new NotImplementedException("Shimmie2 RSS post-list parsing is not yet ported to the CLI.");
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
