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
                        queryStr = $"{queryStr}&login={UserName}&password_hash={hash}";
                    }
                    break;

                case LoginType.Cookie:
                    ReloadCookie(UserName);
                    break;

                case LoginType.CookieAlwaysAsk:
                    // Prompted here (lazily, only for the provider actually in use) rather
                    // than while the whole provider list loads - see DanbooruProviderDao.Read.
                    Console.Write($"Please input the cookie value for {Name}: ");
                    var cookie = Console.ReadLine();
                    Program.Logger.InfoFormat("Using cookie value for {0} = {1}", Name, cookie);
                    ReloadCookie(cookie);
                    break;

                case LoginType.ApiKey:
                    // Gelbooru's DAPI wants api_key+user_id (a numeric account ID), not
                    // Danbooru's api_key+login (a username) - confirmed live: Danbooru-shaped
                    // requests to Gelbooru's API came back 401 with an empty body regardless
                    // of a valid session cookie, but api_key+user_id (from the account's own
                    // API Access Credentials page) works. UserName holds "the second
                    // credential" either way, just named/used differently per board.
                    queryStr = BoardType == BoardType.Gelbooru
                        ? $"{queryStr}&api_key={ApiKey}&user_id={UserName}"
                        : $"{queryStr}&api_key={ApiKey}&login={UserName}";
                    break;
            }

            return $"{Url}{queryStr}";
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
