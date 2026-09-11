using DanbooruDownloader.Cli.Entity;
using DanbooruDownloader.Cli.Utils;

namespace DanbooruDownloader.Cli.Tests
{
    // Regression test for a real live bug: Gelbooru's DAPI (pid=) pages from 0, while
    // Danbooru/Shimmie2's page= pages from 1. Getting this wrong silently skips a
    // provider's real first page (found by testing "artist add" against safebooru.org,
    // which returned 0 posts for an artist who actually had several).
    public class PageIndexingTests
    {
        [Fact]
        public void Gelbooru_starts_at_zero()
        {
            Assert.Equal(0, PageIndexing.FirstPage(BoardType.Gelbooru));
        }

        [Theory]
        [InlineData(BoardType.Danbooru)]
        [InlineData(BoardType.Shimmie2)]
        public void NonGelbooru_starts_at_one(BoardType boardType)
        {
            Assert.Equal(1, PageIndexing.FirstPage(boardType));
        }
    }
}
