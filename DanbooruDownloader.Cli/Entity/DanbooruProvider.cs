using DanbooruDownloader.Cli.Engine;
using System;
using System.Net;

namespace DanbooruDownloader.Cli.Entity
{
    public enum PreferredMethod
    {
        Xml,
        Json,
        Html
    }

    public enum BoardType
    {
        Danbooru,
        Gelbooru,
        Shimmie2
    }

    public enum LoginType
    {
        Anonymous,
        UserPass,
        Cookie,
        CookieAlwaysAsk,
        ApiKey
    }

    public class DanbooruProvider
    {
        // Shared cookie jar used by CliWebClient for all requests (replaces the
        // WinForms app's static ExtendedWebClient.CookieJar).
        public static readonly CookieContainer CookieJar = new CookieContainer();

        public string Name { get; set; }

        public int DefaultLimit { get; set; }

        public int HardLimit { get; set; }

        public PreferredMethod Preferred { get; set; }

        public string QueryStringXml { get; set; }

        public string QueryStringJson { get; set; }

        public string QueryStringHtml { get; set; }

        public string Url { get; set; }

        public string UserName { get; set; }

        public string Password { get; set; }

        public LoginType LoginType { get; set; }

        public string PasswordSalt { get; set; }

        public string PasswordHash { get; set; }

        public string ApiKey { get; set; }

        public BoardType BoardType { get; set; }

        public bool TagDownloadUseLoop { get; set; }

        public string DateTimeFormat { get; set; }

        public override string ToString()
        {
            return Name;
        }

        public string GetQueryString(DanbooruSearchParam searchParam)
        {
            string queryStr;
            if (BoardType == BoardType.Shimmie2)
            {
                if (Preferred == PreferredMethod.Html)
                {
                    queryStr = new ShimmieHtmlParser().GenerateQueryString(searchParam);
                }
                else
                {
                    queryStr = ShimmieEngine.GetQueryString(this, searchParam);
                }
            }
            else if (Url.Contains("sankakucomplex.com"))
            {
                queryStr = new SankakuComplexParser().GenerateQueryString(searchParam);
            }
            else
            {
                queryStr = DanbooruXmlEngine.GetQueryString(this, searchParam);
            }

            return queryStr;
        }

        public string GetQueryUrl(DanbooruSearchParam searchParam)
        {
            var queryStr = GetQueryString(searchParam);

            var queryRootUrl = "";
            switch (Preferred)
            {
                case PreferredMethod.Xml:
                    queryRootUrl = QueryStringXml;
                    break;

                case PreferredMethod.Json:
                    queryRootUrl = QueryStringJson;
                    break;

                case PreferredMethod.Html:
                    queryRootUrl = QueryStringHtml;
                    break;
            }

            queryStr = queryRootUrl.Replace("%_query%", queryStr);

            return ApplyAuth($"{Url}{queryStr}");
        }

        private bool _cookieAsked;

        /// <summary>
        /// Appends credential query parameters to a request URL, or loads the cookie jar, per
        /// LoginType. Every request to this provider (posts and tags) goes through here.
        /// </summary>
        public string ApplyAuth(string url)
        {
            var sep = url.Contains('?') ? "&" : "?";
            switch (LoginType)
            {
                case LoginType.UserPass:
                    {
                        var hash = PasswordHash;
                        if (String.IsNullOrWhiteSpace(hash))
                        {
                            hash = Helper.GeneratePasswordHash(Password, PasswordSalt);
                            PasswordHash = hash;
                        }
                        return $"{url}{sep}login={Uri.EscapeDataString(UserName ?? "")}&password_hash={hash}";
                    }

                case LoginType.Cookie:
                    ReloadCookie(UserName);
                    return url;

                case LoginType.CookieAlwaysAsk:
                    // Prompted lazily (only for the provider actually in use), and only once
                    // per run - a gallery walk calls this for every page.
                    if (!_cookieAsked)
                    {
                        Console.Write($"Please input the cookie value for {Name}: ");
                        ReloadCookie(Console.ReadLine());
                        _cookieAsked = true;
                    }
                    return url;

                case LoginType.ApiKey:
                    // Gelbooru's DAPI wants api_key+user_id (a numeric account ID), not
                    // Danbooru's api_key+login (a username) - confirmed live: Danbooru-shaped
                    // requests to Gelbooru's API came back 401 with an empty body regardless
                    // of a valid session cookie, but api_key+user_id (from the account's own
                    // API Access Credentials page) works. UserName holds "the second
                    // credential" either way, just named/used differently per board.
                    var key = Uri.EscapeDataString(ApiKey ?? "");
                    var user = Uri.EscapeDataString(UserName ?? "");
                    return BoardType == BoardType.Gelbooru
                        ? $"{url}{sep}api_key={key}&user_id={user}"
                        : $"{url}{sep}api_key={key}&login={user}";

                default:
                    return url;
            }
        }

        public void ReloadCookie(string newCookie)
        {
            if (String.IsNullOrWhiteSpace(newCookie)) return;

            UserName = newCookie;
            var cookies = Helper.ParseCookie(newCookie, Url);
            foreach (var cookie in cookies)
            {
                CookieJar.Add(cookie);
            }
        }
    }
}
