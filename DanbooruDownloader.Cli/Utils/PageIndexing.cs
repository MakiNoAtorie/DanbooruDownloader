using DanbooruDownloader.Cli.Entity;

namespace DanbooruDownloader.Cli.Utils
{
    /// <summary>
    /// Extracted as its own testable unit after a live bug: Gelbooru's DAPI (pid=) pages
    /// from 0, while Danbooru/Shimmie2's page= parameter pages from 1. Getting this wrong
    /// silently skips a provider's real first page (see DanbooruXmlEngine.GetQueryString's
    /// "page="/"pid=" branch, which this must stay consistent with).
    /// </summary>
    public static class PageIndexing
    {
        public static int FirstPage(BoardType boardType)
        {
            return boardType == BoardType.Gelbooru ? 0 : 1;
        }

        // Users always count pages from 1; convert to the board's own numbering.
        public static int ToApiPage(BoardType boardType, int userPage) => userPage - 1 + FirstPage(boardType);
    }
}
