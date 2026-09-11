using DanbooruDownloader.Cli.DAO;
using DanbooruDownloader.Cli.Entity;
using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DanbooruDownloader.Cli.Engine
{
    public class SankakuComplexParser : IEngine
    {
        private static bool isHttps(DanbooruProvider provider)
        {
            return provider.Url.ToLowerInvariant().StartsWith("https");
        }

        /// <summary>
        /// Parse the post details after added to the download list or from batch job.
        /// </summary>
        public static DanbooruPost ParsePost(DanbooruPost post, string postHtml, bool overideTagParsing)
        {
            try
            {
                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(postHtml);
                string file_url = "";
                string sample_url = "";

                if (overideTagParsing)
                {
                    ReparseTags(post, doc);
                }

                if (post.PreviewUrl != null && post.PreviewUrl.EndsWith("download-preview.png"))
                {
                    var links = doc.DocumentNode.SelectNodes("//a");
                    foreach (var link in links)
                    {
                        if (link.InnerText == "Save this file (right click and save as)")
                        {
                            file_url = Helper.FixUrl(link.GetAttributeValue("href", ""), isHttps(post.Provider));
                            post.MD5 = file_url.Substring(file_url.LastIndexOf("/") + 1);
                            post.MD5 = post.MD5.Substring(0, 32);
                            break;
                        }
                        if (link.InnerText == "Download")
                        {
                            file_url = Helper.FixUrl(link.GetAttributeValue("href", ""), isHttps(post.Provider));
                            break;
                        }
                    }
                }
                else
                {
                    var lowresElement = doc.DocumentNode.SelectSingleNode("//a[@id='lowres']");
                    if (lowresElement != null)
                    {
                        sample_url = Helper.FixUrl(lowresElement.GetAttributeValue("href", ""), isHttps(post.Provider));
                    }
                    var highresElement = doc.DocumentNode.SelectSingleNode("//a[@id='highres']");
                    if (highresElement != null)
                    {
                        file_url = Helper.FixUrl(highresElement.GetAttributeValue("href", ""), isHttps(post.Provider));
                    }
                }

                post.FileUrl = file_url;
                if (!string.IsNullOrWhiteSpace(file_url) && string.IsNullOrWhiteSpace(sample_url))
                    sample_url = file_url;
                post.SampleUrl = sample_url;

                post.CreatedAt = "N/A";
                post.CreatedAtDateTime = DateTime.MinValue;
                try
                {
                    var lis = doc.DocumentNode.SelectNodes("//div[@id='post-view']//div[@id='stats']//a");
                    if (lis != null)
                    {
                        foreach (var item in lis)
                        {
                            if (item.Attributes.Contains("href") && item.Attributes["href"].Value.Contains("?tags=date"))
                            {
                                post.CreatedAt = item.Attributes["title"].Value;
                                post.CreatedAtDateTime = DanbooruPostDao.ParseDateTime(post.CreatedAt, post.Provider);
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Program.Logger.Error("Unable to parse date", ex);
                }

                return post;
            }
            catch (Exception ex)
            {
                string filename = "Dump for Post " + post.Id + post.Provider.Name + " Query " + post.Query + ".txt";
                bool result = Helper.DumpRawData(postHtml, filename);
                if (!result) Program.Logger.Error("Failed to dump rawdata to: " + filename, ex);
                throw;
            }
        }

        private static void ReparseTags(DanbooruPost post, HtmlDocument doc)
        {
            post.TagsEntity.Clear();
            var tags = doc.DocumentNode.SelectNodes("//ul[@id='tag-sidebar']/li");
            if (tags == null) return;

            foreach (var tag in tags)
            {
                var tagEntity = new DanbooruTag();
                var cls = tag.Attributes["class"].Value;
                switch (cls)
                {
                    case "tag-type-idol": tagEntity.Type = DanbooruTagType.Artist; break;
                    case "tag-type-artist": tagEntity.Type = DanbooruTagType.Artist; break;
                    case "tag-type-photo_set": tagEntity.Type = DanbooruTagType.Circle; break;
                    case "tag-type-studio": tagEntity.Type = DanbooruTagType.Circle; break;
                    case "tag-type-meta": tagEntity.Type = DanbooruTagType.Faults; break;
                    case "tag-type-medium": tagEntity.Type = DanbooruTagType.Faults; break;
                    case "tag-type-general": tagEntity.Type = DanbooruTagType.General; break;
                    case "tag-type-copyright": tagEntity.Type = DanbooruTagType.Copyright; break;
                    case "tag-type-character": tagEntity.Type = DanbooruTagType.Character; break;
                    case "tag-type-genre": tagEntity.Type = DanbooruTagType.Character; break;
                    default: tagEntity.Type = DanbooruTagType.Unknown; break;
                }
                tagEntity.Name = Helper.DecodeEncodedNonAsciiCharacters(tag.InnerText.Trim());
                if (String.IsNullOrWhiteSpace(tagEntity.Name))
                {
                    tagEntity.Name = Helper.DecodeEncodedNonAsciiCharacters(tag.SelectSingleNode(".//a").InnerText);
                }

                post.TagsEntity.Add(tagEntity);
                tag.Remove();
            }
            post.TagsEntity = post.TagsEntity.OrderByDescending(x => x.Type).ThenBy(x => x.Name).ToList();
        }

        /// <summary>
        /// Parse search page result and return the images with initial tags.
        /// </summary>
        public List<DanbooruPost> Parse(string data, DanbooruSearchParam searchParam, ref string errorMessage)
        {
            try
            {
                this.SearchParam = searchParam;
                this.RawData = data;

                List<DanbooruPost> posts = new List<DanbooruPost>();

                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(data);

                var sankakuPremiumError = doc.DocumentNode.SelectSingleNode("//div[@class='post-premium-browsing_error']");
                if (sankakuPremiumError != null)
                {
                    errorMessage = $"Sankaku Premium Error: {sankakuPremiumError.InnerText.Trim()}";
                    Program.Logger.Error(errorMessage);
                    return posts;
                }

                var nodeIds = new string[] { "popular-preview", "has-mail-notice" };
                foreach (var nodeId in nodeIds)
                {
                    var node = doc.DocumentNode.SelectSingleNode($"//div[@id='{nodeId}']");
                    node?.Remove();
                }

                var nodeClasses = new string[] { "carousel" };
                foreach (var nodeClass in nodeClasses)
                {
                    var node = doc.DocumentNode.SelectSingleNode($"//div[@class='{nodeClass}']");
                    node?.Remove();
                }

                var articles = doc.DocumentNode.SelectNodes("//article[contains(@class, 'post-preview')]");
                if (articles != null && articles.Count > 0)
                {
                    foreach (var article in articles)
                    {
                        HtmlDocument articleDoc = new HtmlDocument();
                        articleDoc.LoadHtml(article.InnerHtml);

                        DanbooruPost post = new DanbooruPost();
                        post.Id = article.GetAttributeValue("id", "-1");

                        post.Provider = searchParam.Provider;
                        post.SearchTags = searchParam.Tag;
                        post.Query = GenerateQueryString(searchParam);

                        var a = articleDoc.DocumentNode.SelectSingleNode("//a");
                        if (a == null)
                        {
                            Program.Logger.Warn($"Cannot get post link for {post.Id}.");
                            continue;
                        }
                        post.Referer = Helper.FixUrl(searchParam.Provider.Url + a.GetAttributeValue("href", ""), isHttps(post.Provider));

                        var img = articleDoc.DocumentNode.SelectSingleNode("//img");
                        if (img == null)
                        {
                            Program.Logger.Warn($"Cannot get image thumbs for {post.Id}.");
                            continue;
                        }

                        if (img.GetAttributeValue("src", "").Contains("images/no-visibility.svg"))
                        {
                            Program.Logger.Warn($"No access for post {post.Id}.");
                            continue;
                        }

                        var title = img.GetAttributeValue("data-auto_page", "");
                        post.Tags = title.Substring(0, title.LastIndexOf("Rating:")).Trim();
                        post.Tags = Helper.DecodeEncodedNonAsciiCharacters(post.Tags);
                        post.TagsEntity = Helper.ParseTags(post.Tags, SearchParam.Provider);

                        post.Hidden = Helper.CheckBlacklistedTag(post, searchParam.Option);

                        var status = img.GetAttributeValue("class", "").Replace("preview", "").Trim();
                        if (status.Contains("deleted")) post.Status = "deleted";
                        else if (status.Contains("pending")) post.Status = "pending";
                        else post.Status = status;

                        post.PreviewUrl = Helper.FixUrl(img.GetAttributeValue("src", ""), isHttps(post.Provider));
                        post.PreviewHeight = img.GetAttributeValue("height", 0);
                        post.PreviewWidth = img.GetAttributeValue("width", 0);

                        post.Source = "";
                        post.Score = title.Substring(title.LastIndexOf("Score:") + 6);
                        post.Score = post.Score.Substring(0, post.Score.IndexOf(" ")).Trim();

                        string resolution = title.Substring(title.LastIndexOf("Size:") + 5);
                        resolution = resolution.Substring(0, resolution.IndexOf(" ")).Trim();
                        string[] resArr = resolution.Split('x');
                        post.Width = Int32.Parse(resArr[0]);
                        post.Height = Int32.Parse(resArr[1]);

                        string rating = title.Substring(title.LastIndexOf("Rating:") + 7, 5);
                        rating = rating.Substring(0, rating.IndexOf(" ")).Trim();
                        post.Rating = rating.ToLower();

                        post.CreatorId = title.Substring(title.LastIndexOf("User:") + 5);

                        post.MD5 = post.PreviewUrl.Substring(post.PreviewUrl.LastIndexOf("/") + 1);
                        post.MD5 = post.MD5.Substring(0, post.MD5.LastIndexOf("."));

                        posts.Add(post);
                    }
                }

                var siteTitle = doc.DocumentNode.SelectSingleNode("//*[@id='site-title']");
                if (siteTitle != null)
                {
                    var strTitle = siteTitle.InnerText.Split('\n').First();
                    string strCount;

                    if (!Regex.IsMatch(strTitle, @".* = \d+") && strTitle.LastIndexOf("(") > 0)
                    {
                        strCount = strTitle.Substring(strTitle.LastIndexOf("("));
                    }
                    else
                    {
                        strCount = strTitle.Split('=').Last().Trim();
                    }
                    strCount = strCount.Replace("(", "").Replace(",", "").Replace(".", "").Replace(")", "");
                    Int32.TryParse(strCount, out int count);
                    TotalPost = count;
                }
                else
                {
                    TotalPost = posts.Count;
                }

                var tag_count = doc.DocumentNode.SelectSingleNode("//span[@class='tag-count']");
                if (tag_count != null)
                {
                    Int32.TryParse(tag_count.InnerText, out int count);
                    TotalPost = count;
                }

                if (!SearchParam.Page.HasValue && SearchParam.Page > 0) SearchParam.Page = 1;
                Offset = TotalPost * SearchParam.Page;

                if (searchParam.Page >= 25 && posts.Count >= 25)
                {
                    searchParam.NextKey = posts[posts.Count - 1].Id;
                }

                return posts;
            }
            catch (Exception ex)
            {
                var filename = Helper.SanitizeFilename($"Dump for Sankaku Image List - {searchParam.Tag} - page {searchParam.Page}.txt");
                var result = Helper.DumpRawData(data, filename);
                if (!result) Program.Logger.Error($"Failed to dump rawdata to: {filename}", ex);
                throw;
            }
        }

        public int? TotalPost { get; set; }
        public int? Offset { get; set; }
        public string RawData { get; set; }
        public string ResponseMessage { get; set; }
        public bool Success { get; set; }
        public DanbooruSearchParam SearchParam { get; set; }

        public string GenerateQueryString(DanbooruSearchParam query)
        {
            var parameters = new List<String>();
            var tags = new List<String>();

            if (!String.IsNullOrEmpty(query.NextKey))
            {
                parameters.Add("next=" + query.NextKey);
            }

            if (!String.IsNullOrWhiteSpace(query.Tag))
            {
                tags.Add(query.Tag.Replace(' ', '+'));
            }
            if (!String.IsNullOrWhiteSpace(query.Source))
            {
                tags.Add("source:" + query.Source);
            }
            if (!String.IsNullOrWhiteSpace(query.OrderBy))
            {
                tags.Add(query.OrderBy);
            }
            if (!String.IsNullOrWhiteSpace(query.Rating))
            {
                tags.Add(query.IsNotRating ? "-" + query.Rating : query.Rating);
            }
            if (tags.Count > 0)
            {
                parameters.Add("tags=" + String.Join("+", tags));
            }

            if (query.Page.HasValue && query.Page > 1)
            {
                parameters.Add("page=" + query.Page.Value.ToString());
            }
            else
            {
                parameters.Add("commit=Search");
            }

            return String.Join("&", parameters);
        }

        public int GetNextPage()
        {
            if (!SearchParam.Page.HasValue) SearchParam.Page = 1;
            return SearchParam.Page.Value + 1;
        }

        public int GetPrevPage()
        {
            if (!SearchParam.Page.HasValue) SearchParam.Page = 1;
            var temp = SearchParam.Page.Value - 1;
            return temp > 0 ? temp : 1;
        }
    }
}
