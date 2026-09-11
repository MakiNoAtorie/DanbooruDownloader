using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DanbooruDownloader.Cli.Entity
{
    public class DanbooruPostDaoOption
    {
        public DanbooruProvider Provider { get; set; }

        public string Query { get; set; }

        public string SearchTags { get; set; }

        public string Url { get; set; }

        public string Referer { get; set; }

        public List<DanbooruTag> BlacklistedTags { get; set; }

        public Regex BlacklistedTagsRegex { get; set; }

        public bool BlacklistedTagsUseRegex { get; set; }

        public List<DanbooruTag> IgnoredTags { get; set; }

        public Regex IgnoredTagsRegex { get; set; }

        public bool IgnoredTagsUseRegex { get; set; }

        public bool IsBlacklistOnlyForGeneral { get; set; }
    }
}
