using DanbooruDownloader.Cli.Engine;
using DanbooruDownloader.Cli.Entity;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;

namespace DanbooruDownloader.Cli.DAO
{
    public class DanbooruPostDao
    {
        private string errorMessage;
        public string ErrorMessage => errorMessage;

        public bool HasError => !String.IsNullOrWhiteSpace(errorMessage);

        // used by sankaku
        public string NextId { get; set; }

        public DanbooruPostDaoOption Option { get; set; }

        public int PostCount { get; private set; }

        public int Offset { get; private set; }

        public int ActualCount { get; private set; }

        public List<DanbooruPost> Posts { get; private set; }

        public String RawData { get; set; }

        public bool Success { get; set; }

        public string ResponseMessage { get; set; }

        /// <summary>
        /// Parse a fetched XML/JSON/HTML post-list response and close the stream.
        /// </summary>
        public DanbooruPostDao(Stream input, DanbooruPostDaoOption option, int? currentPage)
        {
            errorMessage = "";
            string rawData = "";
            this.Option = option;
            try
            {
                using (StreamReader reader = new StreamReader(input))
                {
                    rawData = reader.ReadToEnd();
                }

                if (rawData.Contains(Helper.CLOUDFLARE_STR) || rawData.Contains(Helper.DDOS_STR))
                {
                    throw new InvalidOperationException($"Cloudflare DDoS protection enabled for : {option.Provider.Name}, please use the cookie method to access.");
                }

                switch (option.Provider.Preferred)
                {
                    case PreferredMethod.Xml:
                        ReadXML(rawData, option);
                        break;

                    case PreferredMethod.Json:
                        ReadJSON(rawData, option);
                        break;

                    case PreferredMethod.Html:
                        DanbooruSearchParam param = new DanbooruSearchParam()
                        {
                            Provider = option.Provider,
                            Tag = option.SearchTags,
                            Page = currentPage,
                            Option = option
                        };
                        if (option.Provider.BoardType == BoardType.Danbooru)
                        {
                            SankakuComplexParser parser = new SankakuComplexParser();
                            Posts = parser.Parse(rawData, param, ref errorMessage);
                            NextId = param.NextKey;
                            if (parser.TotalPost.HasValue) PostCount = parser.TotalPost.Value;
                        }
                        else if (option.Provider.BoardType == BoardType.Gelbooru)
                        {
                            GelbooruHtmlParser parser = new GelbooruHtmlParser();
                            Posts = parser.Parse(rawData, param, ref errorMessage);
                        }
                        else if (option.Provider.BoardType == BoardType.Shimmie2)
                        {
                            var parser = new ShimmieHtmlParser();
                            Posts = parser.Parse(rawData, param, ref errorMessage);
                        }
                        else
                        {
                            throw new NotImplementedException("No HTML Parser for: " + option.Provider.Name);
                        }
                        break;
                }
            }
            catch (Exception)
            {
                Helper.DumpRawData(rawData, Option.Provider, option.Query);
                throw;
            }
        }

        private void ReadXML(string rawData, DanbooruPostDaoOption option)
        {
            Posts = new List<DanbooruPost>();
            ActualCount = 0;

            // Issue #60: modify xml to insert html entity
            rawData = Regex.Replace(rawData, @"(<\?xml.*\?>)", "$1<!DOCTYPE document SYSTEM \"xhtml.ent\">");

            ProcessXML(rawData);
        }

        private void ProcessXML(string rawData)
        {
            RawData = rawData;
            using (StringReader strReader = new StringReader(rawData))
            {
                using (XmlTextReader reader = new XmlTextReader(strReader))
                {
                    if (Option.Provider.BoardType == BoardType.Shimmie2)
                    {
                        Posts = ShimmieEngine.ParseRSS(rawData, Option);
                        return;
                    }

                    while (reader.Read())
                    {
                        if (reader.NodeType != XmlNodeType.Element) continue;

                        var nodeName = reader.Name.ToLowerInvariant();
                        if (nodeName.Equals("posts"))
                        {
                            while (reader.MoveToNextAttribute())
                            {
                                if (reader.Name.ToLowerInvariant().Equals("count"))
                                    PostCount = int.Parse(reader.Value);
                                else if (reader.Name.ToLowerInvariant().Equals("offset"))
                                    Offset = int.Parse(reader.Value);
                            }
                        }
                        else if (nodeName.Equals("response"))
                        {
                            Success = true;
                            while (reader.MoveToNextAttribute())
                            {
                                if (reader.Name.ToLowerInvariant().Equals("reason")) ResponseMessage = reader.Value;
                                else if (reader.Name.ToLowerInvariant().Equals("success")) Success = bool.Parse(reader.Value);
                            }
                            SetErrorIfFailed();
                        }
                        else if (nodeName.Equals("result"))
                        {
                            while (reader.MoveToNextAttribute())
                            {
                                if (reader.Name.ToLowerInvariant().Equals("success"))
                                {
                                    Success = bool.Parse(reader.Value);
                                    ResponseMessage = reader.ReadString();
                                    SetErrorIfFailed();
                                }
                            }
                        }
                        else if (nodeName.Equals("post"))
                        {
                            DanbooruPost post = new DanbooruPost();
                            ParsePostAttributes(reader, post);

                            post.Hidden = Helper.CheckBlacklistedTag(post, Option);
                            post.Provider = Option.Provider;
                            post.Query = Option.Query;
                            post.SearchTags = Option.SearchTags;
                            if (Option.Provider.BoardType == BoardType.Danbooru || Option.Provider.BoardType == BoardType.Shimmie2)
                                post.Referer = Option.Provider.Url + @"/post/show/" + post.Id;
                            else if (Option.Provider.BoardType == BoardType.Gelbooru)
                                post.Referer = Option.Provider.Url + @"/index.php?page=post&s=view&id=" + post.Id;

                            Posts.Add(post);
                            ActualCount++;
                        }
                    }
                }
            }
        }

        private void ParsePostAttributes(XmlTextReader reader, DanbooruPost post)
        {
            // Inline attribute type (old API)
            while (reader.MoveToNextAttribute())
            {
                switch (reader.Name.ToLowerInvariant())
                {
                    case "id": post.Id = reader.Value; break;
                    case "tags":
                        post.Tags = reader.Value;
                        post.TagsEntity = Helper.ParseTags(post.Tags, Option.Provider);
                        break;

                    case "source": post.Source = reader.Value; break;
                    case "creator_id": post.CreatorId = reader.Value; break;
                    case "file_url": post.FileUrl = AppendHttp(reader.Value); break;
                    case "width": TryParseInt(reader.Value, v => post.Width = v); break;
                    case "height": TryParseInt(reader.Value, v => post.Height = v); break;
                    case "change": post.Change = reader.Value; break;
                    case "score": post.Score = reader.Value; break;
                    case "rating": post.Rating = reader.Value; break;
                    case "status": post.Status = reader.Value; break;
                    case "has_children":
                        if (Int32.TryParse(reader.Value, out int num)) post.HasChildren = Convert.ToBoolean(num);
                        else post.HasChildren = Convert.ToBoolean(reader.Value);
                        break;

                    case "created_at":
                        post.CreatedAt = reader.Value;
                        post.CreatedAtDateTime = ParseDateTime(post.CreatedAt, Option.Provider);
                        break;

                    case "md5": post.MD5 = reader.Value; break;
                    case "preview_url": post.PreviewUrl = AppendHttp(reader.Value); break;
                    case "preview_width": TryParseInt(reader.Value, v => post.PreviewWidth = v); break;
                    case "preview_height": TryParseInt(reader.Value, v => post.PreviewHeight = v); break;
                    case "parent_id": post.ParentId = reader.Value; break;
                    case "sample_url": post.SampleUrl = AppendHttp(reader.Value); break;
                    case "sample_width": TryParseInt(reader.Value, v => post.SampleWidth = v); break;
                    case "sample_height": TryParseInt(reader.Value, v => post.SampleHeight = v); break;
                    case "jpeg_url": post.JpegUrl = AppendHttp(reader.Value); break;
                    case "jpeg_width": TryParseInt(reader.Value, v => post.JpegWidth = v); break;
                    case "jpeg_height": TryParseInt(reader.Value, v => post.JpegHeight = v); break;
                }
            }

            // Issue #258: newer API returns child elements instead of attributes
            if (String.IsNullOrEmpty(post.Id))
            {
                using (var innerReader = reader.ReadSubtree())
                {
                    innerReader.Read();
                    while (!innerReader.EOF)
                    {
                        if (innerReader.NodeType == XmlNodeType.Element)
                        {
                            var nodeName = innerReader.Name.ToLowerInvariant();
                            switch (nodeName)
                            {
                                case "id": post.Id = innerReader.ReadElementContentAsString(); break;
                                case "created_at":
                                    post.CreatedAt = innerReader.ReadElementContentAsString();
                                    post.CreatedAtDateTime = ParseDateTime(post.CreatedAt, Option.Provider);
                                    break;
                                case "score": post.Score = innerReader.ReadElementContentAsString(); break;
                                case "width": post.Width = innerReader.ReadElementContentAsInt(); break;
                                case "height": post.Height = innerReader.ReadElementContentAsInt(); break;
                                case "md5": post.MD5 = innerReader.ReadElementContentAsString(); break;
                                case "rating":
                                    post.Rating = innerReader.ReadElementContentAsString();
                                    post.Rating = post.Rating.Substring(0, 1);
                                    break;
                                case "source": post.Source = innerReader.ReadElementContentAsString(); break;
                                case "change": post.Change = innerReader.ReadElementContentAsString(); break;
                                case "parent_id": post.ParentId = innerReader.ReadElementContentAsString(); break;
                                case "preview_height": post.PreviewHeight = innerReader.ReadElementContentAsInt(); break;
                                case "preview_width": post.PreviewWidth = innerReader.ReadElementContentAsInt(); break;
                                case "tags":
                                    post.Tags = innerReader.ReadElementContentAsString();
                                    post.TagsEntity = Helper.ParseTags(post.Tags, Option.Provider);
                                    break;
                                case "file_url": post.FileUrl = AppendHttp(innerReader.ReadElementContentAsString()); break;
                                case "preview_url": post.PreviewUrl = AppendHttp(innerReader.ReadElementContentAsString()); break;
                                case "sample_url": post.SampleUrl = AppendHttp(innerReader.ReadElementContentAsString()); break;
                                case "sample_height": post.SampleHeight = innerReader.ReadElementContentAsInt(); break;
                                case "sample_width": post.SampleWidth = innerReader.ReadElementContentAsInt(); break;
                                case "status": post.Status = innerReader.ReadElementContentAsString(); break;
                                case "has_children": post.HasChildren = innerReader.ReadElementContentAsBoolean(); break;
                                default: innerReader.Read(); break;
                            }
                        }
                        else
                        {
                            innerReader.Read();
                        }
                    }
                }
            }
        }

        // A failed API response (bad API key, rate limit, ...) has no posts; without this it
        // would look like an empty page, which gallery walks treat as the end of the gallery.
        private void SetErrorIfFailed()
        {
            if (!Success) errorMessage = $"{Option.Provider.Name} API error: {(String.IsNullOrWhiteSpace(ResponseMessage) ? "request failed" : ResponseMessage)}";
        }

        private static void TryParseInt(string value, Action<int> assign)
        {
            assign(-1);
            if (Int32.TryParse(value, out int result)) assign(result);
        }

        public static DateTime ParseDateTime(string dtStr, DanbooruProvider provider)
        {
            DateTime dt = DateTime.MinValue;
            if (!String.IsNullOrWhiteSpace(dtStr) && provider.DateTimeFormat != DanbooruProviderDao.DATETIME_FORMAT_NA)
            {
                if (provider.DateTimeFormat == DanbooruProviderDao.DATETIME_FORMAT_UNIX)
                {
                    double.TryParse(dtStr, out double timestamp);
                    dt = new DateTime(1970, 1, 1).AddSeconds(timestamp);
                }
                else
                {
                    var result = DateTime.TryParseExact(dtStr, provider.DateTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt);
                    if (!result)
                    {
                        Program.Logger.WarnFormat("Invalid format: {1} for {2} ==> {0}.", dt, provider.DateTimeFormat, provider.Name);
                    }
                }
            }
            return dt;
        }

        public void ReadJSON(string rawData, DanbooruPostDaoOption option)
        {
            RawData = rawData;
            Posts = DanbooruJsonParser.Parse(rawData, option, out var error);
            ActualCount = Posts.Count;
            if (error != null) errorMessage = error;
        }

        private string AppendHttp(string url)
        {
            url = Helper.FixUrl(url, hostname: Option.Provider.Url);
            return url.Replace(@"\", "");
        }
    }
}
