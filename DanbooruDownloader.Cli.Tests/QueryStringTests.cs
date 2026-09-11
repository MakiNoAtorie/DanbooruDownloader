using DanbooruDownloader.Cli.Engine;
using DanbooruDownloader.Cli.Entity;

namespace DanbooruDownloader.Cli.Tests
{
    public class QueryStringTests
    {
        private static DanbooruProvider MakeProvider(BoardType boardType) => new DanbooruProvider
        {
            Name = "test",
            BoardType = boardType,
        };

        [Fact]
        public void Gelbooru_uses_pid_param()
        {
            var provider = MakeProvider(BoardType.Gelbooru);
            var query = new DanbooruSearchParam { Tag = "1girl", Page = 2, Provider = provider };

            var result = DanbooruXmlEngine.GetQueryString(provider, query);

            Assert.Contains("pid=2", result);
            Assert.DoesNotContain("page=2", result);
        }

        [Fact]
        public void Danbooru_uses_page_param()
        {
            var provider = MakeProvider(BoardType.Danbooru);
            var query = new DanbooruSearchParam { Tag = "1girl", Page = 2, Provider = provider };

            var result = DanbooruXmlEngine.GetQueryString(provider, query);

            Assert.Contains("page=2", result);
            Assert.DoesNotContain("pid=", result);
        }

        [Fact]
        public void Tags_are_included_and_space_becomes_plus()
        {
            var provider = MakeProvider(BoardType.Danbooru);
            var query = new DanbooruSearchParam { Tag = "long hair blue_eyes", Provider = provider };

            var result = DanbooruXmlEngine.GetQueryString(provider, query);

            Assert.Contains("tags=long+hair+blue_eyes", result);
        }

        [Fact]
        public void Rating_negation_prefixes_with_minus()
        {
            var provider = MakeProvider(BoardType.Danbooru);
            var query = new DanbooruSearchParam { Tag = "", Rating = "rating:safe", IsNotRating = true, Provider = provider };

            var result = DanbooruXmlEngine.GetQueryString(provider, query);

            Assert.Contains("-rating:safe", result);
        }

        [Fact]
        public void Limit_is_included_when_set()
        {
            var provider = MakeProvider(BoardType.Danbooru);
            var query = new DanbooruSearchParam { Tag = "", Limit = 50, Provider = provider };

            var result = DanbooruXmlEngine.GetQueryString(provider, query);

            Assert.Contains("limit=50", result);
        }
    }
}
