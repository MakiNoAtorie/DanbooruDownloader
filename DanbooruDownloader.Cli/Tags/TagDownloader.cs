using DanbooruDownloader.Cli.Entity;
using DanbooruDownloader.Cli.Net;
using DanbooruDownloader.Cli.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace DanbooruDownloader.Cli.Tags
{
    /// <summary>
    /// Downloads a provider's full tag list into the same DanbooruTagCollection XML shape
    /// DanbooruTagsDao already reads (tags.xml) - so once this has run, ParseTags/MakeFilename
    /// automatically pick up real artist/character/copyright categorization instead of
    /// "Unknown" for every tag, no other code changes needed.
    ///
    /// Verified live: both Danbooru-style (`/tag.xml`) and Gelbooru-style
    /// (`index.php?page=dapi&amp;s=tag&amp;q=index`) tag APIs already return
    /// &lt;tags&gt;&lt;tag id="" name="" count="" type="" ambiguous=""/&gt;...&lt;/tags&gt; -
    /// the exact wire shape DanbooruTagCollection/DanbooruTag already deserialize, so this
    /// reuses that XmlSerializer directly instead of hand-parsing.
    /// </summary>
    public static class TagDownloader
    {
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(DanbooruTagCollection));

        // The types MakeFilename actually substitutes into named tokens (%artist%,
        // %copyright%, %character%, %circle%). Fetched explicitly by type on boards that
        // support it, because sorting by count alone (the naive approach) buries these -
        // an individual artist's tag has a far lower count than generic descriptive tags
        // like "long_hair", so a plain count-ordered dump of even several thousand tags can
        // come back with zero artist tags. Confirmed live against Konachan.
        private static readonly DanbooruTagType[] NamedTokenTypes =
        {
            DanbooruTagType.Artist, DanbooruTagType.Copyright, DanbooruTagType.Character, DanbooruTagType.Circle
        };

        public static async Task<List<DanbooruTag>> DownloadAsync(CliWebClient client, DanbooruProvider provider, int limitPerPage, int maxPagesPerType, int delayMs, Action<string> log)
        {
            var all = new List<DanbooruTag>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (provider.BoardType == BoardType.Gelbooru)
            {
                // Gelbooru's DAPI silently ignores a type= filter (confirmed live - it still
                // returns mixed types), so there's no cheap way to target just artist/
                // character/copyright tags here. Best effort: a plain count-ordered dump,
                // which will still miss rare tags unless maxPagesPerType is large.
                log("Note: this board doesn't support filtering tags by type, so rare tags (most artist names) may be under-represented unless you raise --max-pages a lot.");
                await FetchPagesAsync(client, provider, limitPerPage, maxPagesPerType, type: null, delayMs, all, seenNames, log);
                return all;
            }

            foreach (var type in NamedTokenTypes)
            {
                await FetchPagesAsync(client, provider, limitPerPage, maxPagesPerType, type, delayMs, all, seenNames, log);
            }

            return all;
        }

        private static async Task FetchPagesAsync(
            CliWebClient client, DanbooruProvider provider, int limitPerPage, int maxPages,
            DanbooruTagType? type, int delayMs, List<DanbooruTag> all, HashSet<string> seenNames, Action<string> log)
        {
            int page = PageIndexing.FirstPage(provider.BoardType);
            for (int fetched = 0; fetched < maxPages; fetched++, page++)
            {
                // This loop can add up to a lot of requests (up to 4 tag types x maxPages
                // each) - the same rapid-fire pattern that tripped Cloudflare during testing
                // (see the large-limit note below), so it gets the same politeness delay as
                // post downloads rather than firing requests back-to-back. Applied before
                // every request, including the first - simpler and safer than trying to
                // track "first request overall" across nested per-type loops.
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs);
                }

                var url = provider.ApplyAuth(BuildTagUrl(provider, limitPerPage, page, type));
                log($"GET {Helper.RemoveAuthInfo(url)}");

                string rawXml;
                using (var stream = await client.GetStreamAsync(url))
                using (var reader = new StreamReader(stream))
                {
                    rawXml = await reader.ReadToEndAsync();
                }

                // A large --limit (or just enough total requests) can itself trip Cloudflare's
                // bot detection (seen live: a provider that worked fine at limit=50 came back
                // with a "Just a moment..." challenge page - not valid XML - at limit=1000).
                // Surface that clearly instead of a bare XML-parse exception.
                if (rawXml.Contains(DanbooruDownloader.Cli.Helper.CLOUDFLARE_STR) || rawXml.Contains(DanbooruDownloader.Cli.Helper.DDOS_STR))
                {
                    throw new InvalidOperationException($"Cloudflare blocked the tag request at page {page} (try a smaller --limit or add --delay).");
                }

                DanbooruTagCollection collection;
                using (var stringReader = new StringReader(rawXml))
                {
                    collection = (DanbooruTagCollection)Serializer.Deserialize(stringReader);
                }

                if (collection?.Tag == null || collection.Tag.Length == 0)
                {
                    log($"{DescribeType(type)} page {page}: empty, done.");
                    break;
                }

                int added = 0;
                foreach (var tag in collection.Tag)
                {
                    if (string.IsNullOrWhiteSpace(tag.Name)) continue;
                    if (type.HasValue && tag.Type != type.Value) continue; // Gelbooru ignores type=, filter client-side too
                    if (seenNames.Add(tag.Name)) { all.Add(tag); added++; }
                }

                log($"{DescribeType(type)} page {page}: {collection.Tag.Length} tag(s), {added} new (total {all.Count}).");

                if (collection.Tag.Length < limitPerPage)
                {
                    break; // short page: last one
                }
            }
        }

        private static string DescribeType(DanbooruTagType? type) => type?.ToString() ?? "all";

        private static string BuildTagUrl(DanbooruProvider provider, int limit, int page, DanbooruTagType? type)
        {
            return provider.BoardType switch
            {
                BoardType.Gelbooru => $"{provider.Url}/index.php?page=dapi&s=tag&q=index&limit={limit}&pid={page}&orderby=count",
                // Danbooru-style (Moebooru, verified on Konachan): type= filters server-side
                // when given, otherwise falls back to count ordering. Shimmie2 has no tag API;
                // it falls through here and fails with a parse error.
                _ => type.HasValue
                    ? $"{provider.Url}/tag.xml?limit={limit}&page={page}&type={(int)type.Value}"
                    : $"{provider.Url}/tag.xml?limit={limit}&page={page}&order=count",
            };
        }
    }
}
