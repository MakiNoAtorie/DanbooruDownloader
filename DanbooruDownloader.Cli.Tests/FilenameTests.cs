using DanbooruDownloader.Cli.Entity;

namespace DanbooruDownloader.Cli.Tests
{
    public class FilenameTests
    {
        private static DanbooruPost MakePost(string id, string rating, string md5, string fileUrl, List<DanbooruTag>? tags = null)
        {
            return new DanbooruPost
            {
                Id = id,
                Rating = rating,
                MD5 = md5,
                FileUrl = fileUrl,
                Provider = new DanbooruProvider { Name = "TestProvider" },
                Query = "q",
                SearchTags = "search",
                TagsEntity = tags ?? new List<DanbooruTag>(),
            };
        }

        [Fact]
        public void Basic_tokens_are_substituted()
        {
            var format = new DanbooruFilenameFormat { FilenameFormat = "%provider%/%rating% - %id% - %md5%" };
            var post = MakePost("123", "s", "abc123", "https://example.com/image.jpg");

            var result = Helper.MakeFilename(format, post);

            Assert.Equal("TestProvider/s - 123 - abc123", result);
        }

        [Fact]
        public void Artist_token_resolves_from_tags_entity()
        {
            var format = new DanbooruFilenameFormat { FilenameFormat = "%artist%" };
            var post = MakePost("1", "s", "md5", "https://x/y.jpg", new List<DanbooruTag>
            {
                new DanbooruTag { Name = "some_artist", Type = DanbooruTagType.Artist },
                new DanbooruTag { Name = "long_hair", Type = DanbooruTagType.General },
            });

            var result = Helper.MakeFilename(format, post);

            Assert.Equal("some_artist", result);
        }

        [Fact]
        public void Missing_artist_falls_back_to_configured_replacement()
        {
            var format = new DanbooruFilenameFormat { FilenameFormat = "%artist%" };
            var post = MakePost("1", "s", "md5", "https://x/y.jpg");

            var result = Helper.MakeFilename(format, post);

            Assert.Equal(Helper.NoArtistTagValue, result);
        }

        [Fact]
        public void Invalid_filename_characters_are_sanitized()
        {
            // ':' and '/' are handled explicitly by SanitizeFilename on every platform
            // (colon always replaced; '/' is a path separator). Other characters like '*'
            // are only invalid on Windows, so aren't asserted here - this runs on macOS/Linux.
            var post = MakePost("1", "s", "md5", "https://x/y.jpg", new List<DanbooruTag>
            {
                new DanbooruTag { Name = "weird:name/with_chars", Type = DanbooruTagType.Copyright },
            });
            var format = new DanbooruFilenameFormat { FilenameFormat = "%copyright%" };

            var result = Helper.MakeFilename(format, post);

            Assert.DoesNotContain(":", result);
            Assert.DoesNotContain("/", result);
        }

        [Fact]
        public void BaseFolder_is_prefixed()
        {
            var format = new DanbooruFilenameFormat { FilenameFormat = "%id%", BaseFolder = "/tmp/out" };
            var post = MakePost("42", "s", "md5", "https://x/y.jpg");

            var result = Helper.MakeFilename(format, post);

            Assert.Equal("/tmp/out/42", result);
        }

        [Fact]
        public void Result_is_truncated_to_limit()
        {
            var format = new DanbooruFilenameFormat { FilenameFormat = "%id%", Limit = 3 };
            var post = MakePost("1234567", "s", "md5", "https://x/y.jpg");

            var result = Helper.MakeFilename(format, post);

            Assert.Equal(3, result.Length);
        }
    }
}
