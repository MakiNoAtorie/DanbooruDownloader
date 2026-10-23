using DanbooruDownloader.Cli.DAO;
using DanbooruDownloader.Cli.Entity;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DanbooruDownloader.Cli
{
    public static class Helper
    {
        public const string CLOUDFLARE_STR = "href=\"https://www.cloudflare.com?utm_source=challenge&utm_campaign=j\"";
        public const string DDOS_STR = "<title>Just a moment...</title>";
        public const string CLOUDFLARE_COOKIE = "<span id=\"challenge-error-text\">Enable JavaScript and cookies to continue</span>";

        /// <summary>
        /// Generate hashed password+salt using SHA1 (legacy UserPass login).
        /// </summary>
        public static string GeneratePasswordHash(string password, string salt)
        {
            string hash = "";
            string temp = salt.Replace("%PASSWORD%", password);

            byte[] result = SHA1.Create().ComputeHash(Encoding.ASCII.GetBytes(temp));

            foreach (byte b in result)
            {
                string hexValue = b.ToString("X").ToLower();
                hash += (hexValue.Length == 1 ? "0" : "") + hexValue;
            }

            return hash;
        }

        public static string SanitizeFilename(string input, bool allowPathSeparator = false)
        {
            if (input == null) return "";
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                if (allowPathSeparator && (c == '\\' || c == '/'))
                    continue;
                input = input.Replace(c, '_');
            }
            input = input.Replace(':', '_');
            if (!allowPathSeparator)
                input = input.Replace('\\', '_');
            return input;
        }

        /// <summary>
        /// Replacement for the legacy System.Web.HttpUtility.UrlEncode used throughout the
        /// original query-string builders (which encodes spaces as '+', not '%20').
        /// </summary>
        public static string UrlEncode(string value)
        {
            if (value == null) return "";
            return Uri.EscapeDataString(value).Replace("%20", "+");
        }

        public static string HtmlDecode(string value) => WebUtility.HtmlDecode(value);

        /// <summary>
        /// Decode JSON Encoded Unicode Character to C# string.
        /// </summary>
        public static string DecodeEncodedNonAsciiCharacters(string value)
        {
            value = HtmlAgilityPack.HtmlEntity.DeEntitize(value);

            return Regex.Replace(
                value,
                @"\\u(?<Value>[a-zA-Z0-9]{4})",
                m =>
                {
                    return ((char)int.Parse(m.Groups["Value"].Value, NumberStyles.HexNumber)).ToString();
                });
        }

        /// <summary>
        /// Replace password_hash and api_key params with '*'
        /// </summary>
        public static string RemoveAuthInfo(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;
            return Regex.Replace(url, @"([?&](?:password_hash|api_key)=)([^&]*)", m => m.Groups[1].Value + new string('*', m.Groups[2].Length));
        }

        public static bool IsTagsXmlExist()
        {
            return File.Exists(Config.AppPaths.TagsXml);
        }

        public static bool DumpRawData(string data, string filename)
        {
            // Debug aid only: without -v it would leave a file in the working folder on every failure.
            if (!Program.Logger.Verbose) return false;
            Program.Logger.Debug($"Saving the raw response to {Path.GetFullPath(filename)}");
            try
            {
                using (StreamWriter output = File.CreateText(filename))
                {
                    output.Write(data);
                    output.Flush();
                }
                return true;
            }
            catch (Exception ex)
            {
                Program.Logger.Error("Failed to create dump file: " + filename, ex);
                return false;
            }
        }

        public static bool DumpRawData(string data, DanbooruProvider provider, string query)
        {
            string filename = "Dump for List " + provider.Name + " Query " + query + ".txt";
            filename = Helper.SanitizeFilename(filename);
            return DumpRawData(data, filename);
        }

        public static bool CheckBlacklistedTag(DanbooruPost post, DanbooruPostDaoOption option)
        {
            if (option == null) return false;
            if (option.BlacklistedTagsUseRegex)
            {
                if (option.BlacklistedTagsRegex == null) return false;
                if (option.IsBlacklistOnlyForGeneral)
                {
                    return post.TagsEntity.Any(x => x.Type == DanbooruTagType.General && option.BlacklistedTagsRegex.IsMatch(x.Name));
                }
                else return post.TagsEntity.Any(x => option.BlacklistedTagsRegex.IsMatch(x.Name));
            }
            else
            {
                if (option.BlacklistedTags == null) return false;
                if (option.IsBlacklistOnlyForGeneral)
                {
                    foreach (var tag in option.BlacklistedTags)
                    {
                        if (post.TagsEntity.Any(x => x.Type == DanbooruTagType.General && x.Name.Equals(tag.Name, StringComparison.InvariantCultureIgnoreCase)))
                            return true;
                    }
                }
                else
                {
                    foreach (var tag in option.BlacklistedTags)
                    {
                        if (post.TagsEntity.Any(x => x.Name.Equals(tag.Name, StringComparison.InvariantCultureIgnoreCase)))
                            return true;
                    }
                }
            }
            return false;
        }

        public static string FormatByteSize(long size)
        {
            double sizeD = size;
            if (size < 1024)
                return String.Format("{0} bytes", size);
            else
            {
                sizeD = sizeD / 1024;
                if (sizeD >= 1 && sizeD < 1024)
                    return String.Format("{0:F2} Kbytes", sizeD);
                sizeD = sizeD / 1024;
                if (sizeD >= 1 && sizeD < 1024)
                    return String.Format("{0:F2} Mbytes", sizeD);
                sizeD = sizeD / 1024;
                return String.Format("{0:F2} Gbytes", sizeD);
            }
        }

        public static List<DanbooruTag> ParseTags(string p, DanbooruProvider provider)
        {
            return DanbooruTagsDao.Instance.ParseTagsString(p);
        }

        public static string RemoveControlCharacters(string inString)
        {
            if (inString == null) return null;

            StringBuilder newString = new StringBuilder();
            foreach (char ch in inString)
            {
                if (!char.IsControl(ch))
                {
                    newString.Append(ch);
                }
            }
            return newString.ToString();
        }

        public static string FixUrl(string url, bool useHttps = false, string hostname = null)
        {
            if (String.IsNullOrWhiteSpace(url)) return url;

            // escape json url
            url = url.Replace(@"\/", @"/");

            // e.g.: //example.com/something.jpg
            if (url.StartsWith("//"))
            {
                url = (useHttps ? "https:" : "http:") + url;
            }
            // e.g.: /some/relative/url.jpg
            else if (url.StartsWith("/") && hostname != null)
            {
                if (!hostname.StartsWith("http"))
                {
                    hostname = (useHttps ? "https://" : "http://") + hostname;
                }
                url = hostname + url;
            }

            url = WebUtility.HtmlDecode(url);

            return url;
        }

        public const string PRIORITY_TAGS = @"included_tags.txt";

        // Stand-ins for the WinForms app's user-configurable Properties.Settings.Default
        // "no tag" replacement values (tagNoArtistValue etc.) - default to empty, same as
        // the WinForms app's shipped defaults.
        public static string NoArtistTagValue = "";
        public static string NoCharacterTagValue = "";
        public static string NoCircleTagValue = "";
        public static string NoCopyrightTagValue = "";
        public static string NoFaultTagValue = "";
        public static string DefaultDateTimeFormat = "yyyy-MM-dd HH:mm:ss";

        /// <summary>
        /// Build a download filename from a DanbooruFilenameFormat template and a post -
        /// same %token% syntax as the WinForms app (FormAddBatchJob/FormMain use the same
        /// format strings), so existing filename-format strings the user already has keep working.
        /// </summary>
        public static string MakeFilename(DanbooruFilenameFormat format, DanbooruPost post)
        {
            string filename = format.FilenameFormat;
            string provider = post.Provider.Name;
            string query = post.Query;
            string searchTags = post.SearchTags;
            string fileUrlPath = post.FileUrl.Split('?', '#')[0];
            string originalFileName = Uri.UnescapeDataString(Path.GetFileNameWithoutExtension(fileUrlPath.Split('/').Last()));

            filename = Helper.SanitizeFilename(filename, true);

            filename = filename.Replace("%provider%", Helper.SanitizeFilename(provider));
            filename = filename.Replace("%id%", post.Id);
            filename = filename.Replace("%rating%", post.Rating);
            filename = filename.Replace("%md5%", post.MD5);
            filename = filename.Replace("%searchParam%", Helper.SanitizeFilename(query));
            // Fall back to a real name instead of leaving this blank (and producing an ugly
            // double-slash / empty path segment) when nothing was actually searched for -
            // e.g. a "browse latest" download with no tags.
            var searchTagValue = String.IsNullOrWhiteSpace(searchTags) ? "latest" : searchTags;
            filename = filename.Replace("%searchtag%", Helper.SanitizeFilename(searchTagValue));
            filename = filename.Replace("%originalFilename%", Helper.SanitizeFilename(originalFileName));

            string dtFormat = string.IsNullOrWhiteSpace(DefaultDateTimeFormat) ? "yyyy-mm-dd HH:mm:ss" : DefaultDateTimeFormat;
            filename = filename.Replace("%uploadDateTime%", Helper.SanitizeFilename(post.CreatedAtDateTime.ToString(dtFormat)));

            var groupedTags = post.TagsEntity;

            // custom sort to prioritize some tags based on file definition (Issue #46, #81)
            if (File.Exists(PRIORITY_TAGS))
            {
                var priorityTags = ReadTagsFromTextFile(PRIORITY_TAGS);
                groupedTags.Sort((a, b) =>
                {
                    var containA = priorityTags.Exists(x => Regex.IsMatch(a.Name, x.Name));
                    var containB = priorityTags.Exists(x => Regex.IsMatch(b.Name, x.Name));

                    if (containA && !containB) return -1;
                    if (!containA && containB) return 1;
                    return a.CompareTo(b);
                });
            }
            else
            {
                groupedTags.Sort();
            }

            groupedTags = RemoveIgnoredTags(format, groupedTags);

            var artist = FilterTags(post, groupedTags, DanbooruTagType.Artist, format.ArtistGroupLimit, format.ArtistGroupReplacement, format.MissingTagReplacement, format.IsReplaceMode, format.TagReplaceUnderscoreToSpace);
            filename = filename.Replace("%artist%", Helper.SanitizeFilename(artist).Trim());

            var copyright = FilterTags(post, groupedTags, DanbooruTagType.Copyright, format.CopyrightGroupLimit, format.CopyrightGroupReplacement, format.MissingTagReplacement, format.IsReplaceMode, format.TagReplaceUnderscoreToSpace);
            filename = filename.Replace("%copyright%", Helper.SanitizeFilename(copyright.Trim()));

            var character = FilterTags(post, groupedTags, DanbooruTagType.Character, format.CharacterGroupLimit, format.CharacterGroupReplacement, format.MissingTagReplacement, format.IsReplaceMode, format.TagReplaceUnderscoreToSpace);
            filename = filename.Replace("%character%", Helper.SanitizeFilename(character.Trim()));

            var circle = FilterTags(post, groupedTags, DanbooruTagType.Circle, format.CircleGroupLimit, format.CircleGroupReplacement, format.MissingTagReplacement, format.IsReplaceMode, format.TagReplaceUnderscoreToSpace);
            filename = filename.Replace("%circle%", Helper.SanitizeFilename(circle.Trim()));

            var faults = FilterTags(post, groupedTags, DanbooruTagType.Faults, format.FaultsGroupLimit, format.FaultsGroupReplacement, format.MissingTagReplacement, format.IsReplaceMode, format.TagReplaceUnderscoreToSpace);
            filename = filename.Replace("%faults%", Helper.SanitizeFilename(faults.Trim()));

            var general = FilterTags(post, groupedTags, DanbooruTagType.General, 0, "", "", format.IsReplaceMode, format.TagReplaceUnderscoreToSpace);
            filename = filename.Replace("%general%", Helper.SanitizeFilename(general.Trim()));

            var allTempTags = groupedTags.Select(x => x.Name).ToList();
            if (format.TagReplaceUnderscoreToSpace)
            {
                for (int i = 0; i < allTempTags.Count; i++)
                {
                    allTempTags[i] = allTempTags[i].Replace("_", " ").Trim();
                }
            }
            filename = filename.Replace("%tags%", Helper.SanitizeFilename(string.Join(" ", allTempTags)));

            // Limit each generated path segment, never the base folder: truncating the joined
            // path could cut into the base folder and write files somewhere else.
            filename = string.Join("/", filename.Split('/', '\\').Select(s => LimitPathSegment(s, format.Limit)));

            if (format.BaseFolder.EndsWith("/") || format.BaseFolder.EndsWith("\\"))
                filename = format.BaseFolder + filename;
            else if (!String.IsNullOrWhiteSpace(format.BaseFolder))
                filename = format.BaseFolder + "/" + filename;

            return filename;
        }

        // macOS/Linux cap a file name at 255 UTF-8 bytes (CJK tags take 3 bytes per char), so
        // cap bytes too, leaving room for the extension and the ".!tmp" download suffix.
        private const int MaxSegmentUtf8Bytes = 200;

        private static string LimitPathSegment(string segment, int charLimit)
        {
            var fullLength = segment.Length;
            if (segment.Length > charLimit) segment = segment[..charLimit];
            while (Encoding.UTF8.GetByteCount(segment) > MaxSegmentUtf8Bytes) segment = segment[..^1];
            if (segment.Length > 0 && char.IsHighSurrogate(segment[^1])) segment = segment[..^1];
            if (segment.Length < fullLength) segment = CutAtTagBoundary(segment);
            segment = segment.Trim();
            // A server-supplied tag of "." or ".." must not become a directory traversal.
            if (segment is "." or "..") return "_";
            // Windows cannot create files or folders named like devices (con, nul, com1.jpg, ...).
            return WindowsReservedName.IsMatch(segment) ? segment + "_" : segment;
        }

        /// <summary>
        /// For a name already cut to length: drop the partial tag at the end, so names do not
        /// end in the middle of a tag. Keeps the cut when that would remove most of the name.
        /// </summary>
        internal static string CutAtTagBoundary(string cut)
        {
            var space = cut.LastIndexOf(' ');
            if (space > cut.Length / 2) cut = cut[..space];
            return cut.TrimEnd(' ', '-', '.');
        }

        private static readonly Regex WindowsReservedName = new Regex(@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\..*)?$", RegexOptions.IgnoreCase);

        private static List<DanbooruTag> ReadTagsFromTextFile(string filename)
        {
            var list = new List<DanbooruTag>();
            using (var file = new StreamReader(filename))
            {
                string line;
                while ((line = file.ReadLine()) != null)
                {
                    list.Add(new DanbooruTag { Name = line, Id = "-1", Count = -1, Type = DanbooruTagType.Unknown });
                }
            }
            return list;
        }

        private static List<DanbooruTag> RemoveIgnoredTags(DanbooruFilenameFormat format, List<DanbooruTag> groupedTags)
        {
            if (format.IgnoredTags == null) return groupedTags;
            foreach (DanbooruTag ignoredTag in format.IgnoredTags)
            {
                if (format.IgnoreTagsUseRegex)
                {
                    if (!String.IsNullOrWhiteSpace(format.IgnoredTagsRegex))
                    {
                        Regex re = new Regex(format.IgnoredTagsRegex, RegexOptions.IgnoreCase);
                        if (format.IgnoredTagsOnlyForGeneral)
                            groupedTags.RemoveAll(x => x.Type == DanbooruTagType.General && re.IsMatch(x.Name));
                        else
                            groupedTags.RemoveAll(x => re.IsMatch(x.Name));
                    }
                }
                else
                {
                    groupedTags.RemoveAll(x => x.Name == ignoredTag.Name);
                }
            }
            return groupedTags;
        }

        /// <summary>
        /// Return string from tags filtered by tag type, replacing with tagReplacement if
        /// the count exceeds tagLimit, or missingTagReplacement if there are none.
        /// </summary>
        private static string FilterTags(DanbooruPost post, List<DanbooruTag> groupedTags, DanbooruTagType tagType, int tagLimit, string tagReplacement, string missingTagReplacement, bool isReplaceMode, bool isReplaceUnderScore)
        {
            var selectedTags = post.TagsEntity.Where(x => x.Type == tagType).Select(x => x.Name).ToList();
            var tagStr = "";
            if (selectedTags != null)
            {
                if (isReplaceUnderScore)
                {
                    for (int i = 0; i < selectedTags.Count; i++)
                    {
                        selectedTags[i] = selectedTags[i].Replace("_", " ").Trim();
                    }
                }
                if (tagLimit > 0 && selectedTags.Count >= tagLimit)
                {
                    if (isReplaceMode && !string.IsNullOrWhiteSpace(tagReplacement))
                    {
                        tagStr = isReplaceUnderScore ? tagReplacement.Replace("_", " ").Trim() : tagReplacement;
                    }
                    else
                    {
                        tagStr = String.Join(" ", selectedTags.Take(tagLimit));
                    }
                }
                else
                {
                    tagStr = string.Join(" ", selectedTags);
                }
            }

            if (string.IsNullOrWhiteSpace(tagStr))
            {
                switch (tagType)
                {
                    case DanbooruTagType.Artist: tagStr = NoArtistTagValue; break;
                    case DanbooruTagType.Character: tagStr = NoCharacterTagValue; break;
                    case DanbooruTagType.Circle: tagStr = NoCircleTagValue; break;
                    case DanbooruTagType.Copyright: tagStr = NoCopyrightTagValue; break;
                    case DanbooruTagType.Faults: tagStr = NoFaultTagValue; break;
                    default: tagStr = missingTagReplacement; break;
                }
            }
            return tagStr;
        }

        public static List<Cookie> ParseCookie(string cookiesStr, string url)
        {
            var cookies = new List<Cookie>();
            try
            {
                var temp = cookiesStr.Split(';');
                foreach (var cookieStr in temp)
                {
                    if (!cookieStr.Contains("=")) continue;
                    // Values may contain '=' (base64 padding), so split on the first one only.
                    var temp2 = cookieStr.Split('=', 2);
                    var name = temp2[0].Trim();
                    var value = temp2[1].Trim();
                    var path = "/";
                    Uri uri = new Uri(url);
                    var c = new Cookie(name, value, path, uri.Authority);
                    cookies.Add(c);
                }
            }
            catch (Exception ex)
            {
                // The cookie string is a credential; never log it.
                Program.Logger.Error("Failed to parse cookie value", ex);
            }
            return cookies;
        }
    }
}
