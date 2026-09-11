using DanbooruDownloader.Cli.Entity;
using System.Collections.Generic;

namespace DanbooruDownloader.Cli.Engine
{
    public interface IEngine
    {
        int? TotalPost { get; set; }
        int? Offset { get; set; }
        string RawData { get; set; }
        string ResponseMessage { get; set; }
        bool Success { get; set; }
        DanbooruSearchParam SearchParam { get; set; }

        List<DanbooruPost> Parse(string data, DanbooruSearchParam query, ref string errorMessage);

        string GenerateQueryString(DanbooruSearchParam query);

        int GetNextPage();

        int GetPrevPage();
    }
}
