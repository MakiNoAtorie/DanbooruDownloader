using System.Text.RegularExpressions;
using DanbooruDownloader.Cli.Entity;

namespace DanbooruDownloader.Cli.Tests
{
    public class BlacklistTests
    {
        private static DanbooruPost PostWithTags(params string[] tagNames)
        {
            return new DanbooruPost
            {
                Id = "1",
                TagsEntity = tagNames.Select(n => new DanbooruTag { Name = n }).ToList(),
            };
        }

        [Fact]
        public void No_option_means_not_blacklisted()
        {
            var post = PostWithTags("loli");
            Assert.False(Helper.CheckBlacklistedTag(post, null!));
        }

        [Fact]
        public void Exact_name_match_is_blacklisted()
        {
            var post = PostWithTags("safe_tag", "banned_tag");
            var option = new DanbooruPostDaoOption
            {
                BlacklistedTags = new List<DanbooruTag> { new DanbooruTag { Name = "banned_tag" } },
            };

            Assert.True(Helper.CheckBlacklistedTag(post, option));
        }

        [Fact]
        public void Match_is_case_insensitive()
        {
            var post = PostWithTags("BANNED_TAG");
            var option = new DanbooruPostDaoOption
            {
                BlacklistedTags = new List<DanbooruTag> { new DanbooruTag { Name = "banned_tag" } },
            };

            Assert.True(Helper.CheckBlacklistedTag(post, option));
        }

        [Fact]
        public void No_match_means_not_blacklisted()
        {
            var post = PostWithTags("safe_tag_a", "safe_tag_b");
            var option = new DanbooruPostDaoOption
            {
                BlacklistedTags = new List<DanbooruTag> { new DanbooruTag { Name = "banned_tag" } },
            };

            Assert.False(Helper.CheckBlacklistedTag(post, option));
        }

        [Fact]
        public void Regex_mode_matches_pattern()
        {
            var post = PostWithTags("some_gore_tag");
            var option = new DanbooruPostDaoOption
            {
                BlacklistedTagsUseRegex = true,
                BlacklistedTagsRegex = new Regex("gore", RegexOptions.IgnoreCase),
            };

            Assert.True(Helper.CheckBlacklistedTag(post, option));
        }

        [Fact]
        public void OnlyForGeneral_ignores_non_general_matches()
        {
            var post = new DanbooruPost
            {
                Id = "1",
                TagsEntity = new List<DanbooruTag>
                {
                    new DanbooruTag { Name = "banned_artist", Type = DanbooruTagType.Artist },
                },
            };
            var option = new DanbooruPostDaoOption
            {
                IsBlacklistOnlyForGeneral = true,
                BlacklistedTags = new List<DanbooruTag> { new DanbooruTag { Name = "banned_artist" } },
            };

            // The matching tag is type Artist, not General, so with IsBlacklistOnlyForGeneral
            // it should NOT be treated as blacklisted.
            Assert.False(Helper.CheckBlacklistedTag(post, option));
        }
    }
}
