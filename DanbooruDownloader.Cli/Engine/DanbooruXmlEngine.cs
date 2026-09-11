using DanbooruDownloader.Cli.Entity;
using System;
using System.Collections.Generic;

namespace DanbooruDownloader.Cli.Engine
{
    /// <summary>
    /// Only the static query-string builder is ported: in the original WinForms app this
    /// class also implemented IEngine, but its instance Parse() method dereferenced a
    /// SearchParam property that unconditionally threw NotImplementedException - genuinely
    /// dead code (its own doc comment said "not used yet"). Actual XML parsing goes through
    /// DanbooruPostDao instead; this class is kept only for DanbooruProvider.GetQueryString's
    /// static call.
    /// </summary>
    public static class DanbooruXmlEngine
    {
        public static string GetQueryString(DanbooruProvider provider, DanbooruSearchParam query)
        {
            var tags = query.Tag ?? "";
            while (tags.Contains("  "))
            {
                tags = tags.Replace("  ", " ");
            }
            tags = tags.Trim();
            tags = Helper.UrlEncode(tags);

            List<string> queryList = new List<string>();
            List<string> tagsList = new List<string>();

            if (tags.Length > 0) tagsList.Add(tags.Replace(' ', '+'));

            if (!String.IsNullOrWhiteSpace(query.Rating)) tagsList.Add(query.IsNotRating ? "-" + query.Rating : "" + query.Rating);

            if (!String.IsNullOrWhiteSpace(query.Source)) tagsList.Add("source:" + query.Source);

            if (!String.IsNullOrWhiteSpace(query.OrderBy)) tagsList.Add(query.OrderBy);

            if (tagsList.Count > 0) queryList.Add("tags=" + String.Join("+", tagsList));

            if (query.Limit > 0) queryList.Add("limit=" + query.Limit);

            if (query.Page > 0)
            {
                if (provider.BoardType == BoardType.Danbooru) queryList.Add("page=" + query.Page);
                else if (provider.BoardType == BoardType.Gelbooru) queryList.Add("pid=" + query.Page);
            }

            return queryList.Count > 0 ? String.Join("&", queryList) : "";
        }
    }
}
