using System.Text;
using DanbooruDownloader.Cli.DAO;
using DanbooruDownloader.Cli.Entity;

namespace DanbooruDownloader.Cli.Tests
{
    public class JsonAndRatingTests
    {
        private static DanbooruProvider Provider(string url, string jsonQuery) => new DanbooruProvider
        {
            Name = "P",
            Url = url,
            BoardType = BoardType.Danbooru,
            Preferred = PreferredMethod.Json,
            QueryStringJson = jsonQuery,
            DateTimeFormat = DanbooruProviderDao.DATETIME_FORMAT_NA,
        };

        private static DanbooruPostDao Parse(string body, DanbooruProvider provider)
        {
            var option = new DanbooruPostDaoOption { Provider = provider, Query = "q", SearchTags = "q" };
            return new DanbooruPostDao(new MemoryStream(Encoding.UTF8.GetBytes(body)), option, 1);
        }

        [Fact]
        public void Danbooru2_posts_carry_tag_categories()
        {
            var json = """
                [{"id":12434629,"created_at":"2026-10-23T13:25:18.392-04:00","score":3,"md5":"306809","rating":"g",
                  "image_width":1976,"image_height":2048,"tag_string":"1girl riders0418 touhou nagae_iku",
                  "tag_string_general":"1girl","tag_string_artist":"riders0418","tag_string_copyright":"touhou",
                  "tag_string_character":"nagae_iku","tag_string_meta":"","file_size":791436,"parent_id":null,
                  "has_children":false,"file_url":"https://cdn.donmai.us/original/30/68/306809.jpg"}]
                """;

            var post = Assert.Single(Parse(json, Provider("https://danbooru.donmai.us", "/posts.json?%_query%")).Posts);

            Assert.Equal("12434629", post.Id);
            Assert.Equal("306809", post.MD5);
            Assert.Equal(1976, post.Width);
            Assert.Equal("3", post.Score);
            Assert.Null(post.ParentId);
            Assert.Equal("https://danbooru.donmai.us/posts/12434629", post.Referer);
            Assert.Equal(2026, post.CreatedAtDateTime.Year);
            Assert.Contains(post.TagsEntity, t => t.Name == "riders0418" && t.Type == DanbooruTagType.Artist);
            Assert.Contains(post.TagsEntity, t => t.Name == "nagae_iku" && t.Type == DanbooruTagType.Character);

            var format = new DanbooruFilenameFormat { FilenameFormat = "%artist% - %character% - %id%" };
            Assert.Equal("riders0418 - nagae_iku - 12434629", Helper.MakeFilename(format, post));
        }

        [Fact]
        public void E621_nested_posts_parse()
        {
            var json = """
                {"posts":[{"id":6802574,"created_at":"2026-10-23T14:56:46.773-04:00",
                  "file":{"width":2400,"height":2400,"ext":"png","size":183920,"md5":"4e33","url":"https://static1.e621.net/data/4e/33/4e33.png"},
                  "preview":{"width":256,"height":256,"url":"https://static1.e621.net/p.jpg"},
                  "score":{"up":5,"down":0,"total":5},"rating":"s","sources":["https://src.example"],
                  "tags":{"general":["wolf"],"species":["canine"],"artist":["jamoart"],"character":[],"copyright":[],"meta":["hi_res"]},
                  "relationships":{"parent_id":null,"has_children":true}}]}
                """;

            var post = Assert.Single(Parse(json, Provider("https://e621.net", "/posts.json?%_query%")).Posts);

            Assert.Equal("4e33", post.MD5);
            Assert.Equal("https://static1.e621.net/data/4e/33/4e33.png", post.FileUrl);
            Assert.Equal(2400, post.Height);
            Assert.Equal("5", post.Score);
            Assert.Equal("https://src.example", post.Source);
            Assert.True(post.HasChildren);
            Assert.Contains(post.TagsEntity, t => t.Name == "jamoart" && t.Type == DanbooruTagType.Artist);
            Assert.Contains(post.TagsEntity, t => t.Name == "canine" && t.Type == DanbooruTagType.General);
        }

        [Fact]
        public void Moebooru_flat_posts_parse()
        {
            var json = """[{"id":5,"tags":"a b","created_at":1333000193,"md5":"m","file_url":"//files.example.com/x.png","width":10,"height":20,"rating":"s"}]""";

            var post = Assert.Single(Parse(json, Provider("https://yande.re", "/post/index.json?%_query%")).Posts);

            Assert.Equal("a b", post.Tags);
            Assert.Equal("https://files.example.com/x.png", post.FileUrl);
            Assert.Equal("https://yande.re/post/show/5", post.Referer);
            Assert.Equal(2012, post.CreatedAtDateTime.Year);
        }

        [Fact]
        public void Error_object_is_reported()
        {
            var dao = Parse("""{"success":false,"error":"PostQuery::TagLimitError","message":"You cannot search for more than 2 tags at a time."}""",
                Provider("https://danbooru.donmai.us", "/posts.json?%_query%"));

            Assert.True(dao.HasError);
            Assert.Contains("more than 2 tags", dao.ErrorMessage);
        }

        [Fact]
        public void Empty_results_are_not_errors()
        {
            Assert.Empty(Parse("[]", Provider("https://danbooru.donmai.us", "/posts.json?%_query%")).Posts);
            Assert.Empty(Parse("""{"posts":[]}""", Provider("https://e621.net", "/posts.json?%_query%")).Posts);
        }

        [Theory]
        [InlineData("https://danbooru.donmai.us", "safe", "rating:general")]
        [InlineData("https://gelbooru.com", "rating:Safe", "rating:general")]
        [InlineData("https://danbooru.donmai.us", "sensitive", "rating:sensitive")]
        [InlineData("https://yande.re", "safe", "rating:safe")]
        [InlineData("https://yande.re", "explicit", "rating:explicit")]
        [InlineData("https://yande.re", "", null)]
        public void Rating_is_normalized_per_board(string url, string input, string? expected)
        {
            Assert.Equal(expected, Program.NormalizeRating(input, new DanbooruProvider { Url = url }));
        }

        [Fact]
        public void Existing_file_is_trusted_only_when_md5_matches()
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "abc");
                Assert.True(Program.FileMatchesMd5(path, "900150983CD24FB0D6963F7D28E17F72"));
                Assert.False(Program.FileMatchesMd5(path, "00000000000000000000000000000000"));
                Assert.True(Program.FileMatchesMd5(path, null));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
