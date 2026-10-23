using System.Text;
using DanbooruDownloader.Cli.DAO;
using DanbooruDownloader.Cli.Entity;

namespace DanbooruDownloader.Cli.Tests
{
    public class ParsingAndAuthTests
    {
        private static DanbooruProvider Provider(BoardType boardType) => new DanbooruProvider
        {
            Name = "P",
            Url = "https://example.com",
            BoardType = boardType,
            Preferred = PreferredMethod.Xml,
            DateTimeFormat = DanbooruProviderDao.DATETIME_FORMAT_NA,
        };

        private static DanbooruPostDao Parse(string body, DanbooruProvider provider)
        {
            var option = new DanbooruPostDaoOption { Provider = provider, Query = "q", SearchTags = "q" };
            return new DanbooruPostDao(new MemoryStream(Encoding.UTF8.GetBytes(body)), option, 1);
        }

        private static DanbooruPost Post(string fileUrl, params string[] tags) => new DanbooruPost
        {
            Id = "7",
            Rating = "s",
            FileUrl = fileUrl,
            Provider = new DanbooruProvider { Name = "P" },
            TagsEntity = tags.Select(t => new DanbooruTag { Name = t }).ToList(),
        };

        [Fact]
        public void Limit_never_truncates_the_base_folder()
        {
            var baseFolder = "/" + new string('b', 300);
            var format = new DanbooruFilenameFormat { FilenameFormat = "%id% - %tags%", BaseFolder = baseFolder };

            var result = Helper.MakeFilename(format, Post("https://x/y.jpg", "tag"));

            Assert.Equal(baseFolder + "/7 - tag", result);
        }

        [Fact]
        public void Segment_is_capped_in_utf8_bytes()
        {
            var cjkTag = new string('髪', 140); // 140 chars, 420 UTF-8 bytes
            var format = new DanbooruFilenameFormat { FilenameFormat = "%tags%" };

            var result = Helper.MakeFilename(format, Post("https://x/y.jpg", cjkTag));

            Assert.True(Encoding.UTF8.GetByteCount(result) <= 200);
            Assert.StartsWith("髪", result);
        }

        [Fact]
        public void File_url_without_extension_does_not_throw()
        {
            var format = new DanbooruFilenameFormat { FilenameFormat = "%originalFilename%" };

            Assert.Equal("image", Helper.MakeFilename(format, Post("https://x/image?token=a.b")));
        }

        [Fact]
        public void Dot_dot_segment_is_neutralized()
        {
            var format = new DanbooruFilenameFormat { FilenameFormat = "%tags%/%id%" };

            Assert.Equal("_/7", Helper.MakeFilename(format, Post("https://x/y.jpg", "..")));
        }

        [Fact]
        public void Gelbooru_api_key_auth_is_encoded_and_masked()
        {
            var provider = Provider(BoardType.Gelbooru);
            provider.LoginType = LoginType.ApiKey;
            provider.ApiKey = "k&y";
            provider.UserName = "12";

            var url = provider.ApplyAuth("https://example.com/tag.xml");

            Assert.Equal("https://example.com/tag.xml?api_key=k%26y&user_id=12", url);
            Assert.Equal("https://example.com/tag.xml?api_key=*****&user_id=12", Helper.RemoveAuthInfo(url));
        }

        [Fact]
        public void Failed_api_response_is_an_error_not_an_empty_page()
        {
            var dao = Parse("<?xml version=\"1.0\" encoding=\"UTF-8\"?><response success=\"false\" reason=\"Invalid API key\"/>", Provider(BoardType.Gelbooru));

            Assert.True(dao.HasError);
            Assert.Contains("Invalid API key", dao.ErrorMessage);
        }

        [Fact]
        public void Attribute_style_posts_parse()
        {
            var dao = Parse("<?xml version=\"1.0\" encoding=\"UTF-8\"?><posts count=\"1\" offset=\"0\"><post id=\"5\" md5=\"abc\" rating=\"s\" tags=\"a b\" file_url=\"//img.example.com/x.png\"/></posts>", Provider(BoardType.Danbooru));

            Assert.False(dao.HasError);
            var post = Assert.Single(dao.Posts);
            Assert.Equal("5", post.Id);
            Assert.Equal("http://img.example.com/x.png", post.FileUrl);
        }

        [Fact]
        public void Shimmie_rss_parses()
        {
            var rss = """
                <?xml version="1.0" encoding="utf-8" ?>
                <rss version="2.0" xmlns:media="http://search.yahoo.com/mrss"><channel>
                  <item><title>42 - tag_a tag_b</title><link>/post/view/42</link><pubDate>x</pubDate>
                    <media:thumbnail url="/thumb/42.jpg"/><media:content url="https://cdn.example.com/42.jpg"/></item>
                </channel></rss>
                """;

            var dao = Parse(rss, Provider(BoardType.Shimmie2));

            var post = Assert.Single(dao.Posts);
            Assert.Equal("42", post.Id);
            Assert.Equal("tag_a tag_b", post.Tags);
            Assert.Equal("https://cdn.example.com/42.jpg", post.FileUrl);
            Assert.Equal("https://example.com/thumb/42.jpg", post.PreviewUrl);
        }
    }
}
