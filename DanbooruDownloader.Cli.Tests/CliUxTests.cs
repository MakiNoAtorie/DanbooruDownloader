using DanbooruDownloader.Cli.Entity;
using DanbooruDownloader.Cli.Utils;

namespace DanbooruDownloader.Cli.Tests
{
    public class CliUxTests
    {
        private const string Spec = "provider= tags= limit= force";

        [Fact]
        public void Unknown_flag_is_an_error_with_a_suggestion()
        {
            var ex = Assert.Throws<UsageException>(() => Flags.Parse(new[] { "--provider", "x", "--tag", "touhou" }, Spec));
            Assert.Contains("--tags", ex.Message);
        }

        [Fact]
        public void Switch_does_not_swallow_the_next_argument()
        {
            var positionals = new List<string>();
            var flags = Flags.Parse(new[] { "--force", "danbooru", "--tags=a b" }, Spec, positionals);
            Assert.Equal("true", flags["force"]);
            Assert.Equal("a b", flags["tags"]);
            Assert.Equal(new[] { "danbooru" }, positionals);
        }

        [Fact]
        public void Stray_argument_and_missing_value_are_errors()
        {
            Assert.Throws<UsageException>(() => Flags.Parse(new[] { "--provider", "x", "touhou" }, Spec));
            Assert.Throws<UsageException>(() => Flags.Parse(new[] { "--provider" }, Spec));
            // A negated tag is a value, not a flag.
            Assert.Equal("-solo", Flags.Parse(new[] { "--tags", "-solo" }, Spec)["tags"]);
        }

        [Fact]
        public void Numbers_are_validated()
        {
            var flags = Flags.Parse(new[] { "--limit", "abc" }, Spec);
            Assert.Throws<UsageException>(() => Flags.Int(flags, "limit"));
            Assert.Throws<UsageException>(() => Flags.Int(Flags.Parse(new[] { "--limit", "0" }, Spec), "limit", min: 1));
            Assert.Equal(7, Flags.Int(new Dictionary<string, string>(), "limit", 7, 1));
        }

        [Fact]
        public void Providers_match_by_unique_part_of_name()
        {
            var providers = new[] { "danbooru", "e621 (Furry)", "yande.re (oreno.imouto.org)", "Sankaku Complex (HTTPS)", "Sankaku Complex (Idol)", "gelbooru.com", "safebooru.org" }
                .Select(n => new DanbooruProvider { Name = n }).ToList();

            Assert.Equal("e621 (Furry)", Program.FindProvider(providers, "e621")?.Name);
            Assert.Equal("yande.re (oreno.imouto.org)", Program.FindProvider(providers, "yandere")?.Name);
            Assert.Equal("gelbooru.com", Program.FindProvider(providers, "gelbooru")?.Name);
            Assert.Equal("danbooru", Program.FindProvider(providers, "DANBOORU")?.Name);
            Assert.Null(Program.FindProvider(providers, "sankaku"));
            Assert.Equal(2, Program.MatchProviders(providers, "sankaku").Count);
            Assert.Null(Program.FindProvider(providers, "nope"));
        }

        [Theory]
        [InlineData(BoardType.Gelbooru, 1, 0)]
        [InlineData(BoardType.Gelbooru, 3, 2)]
        [InlineData(BoardType.Danbooru, 1, 1)]
        [InlineData(BoardType.Shimmie2, 3, 3)]
        public void User_pages_count_from_one_on_every_board(BoardType board, int userPage, int apiPage)
        {
            Assert.Equal(apiPage, PageIndexing.ToApiPage(board, userPage));
        }

        [Theory]
        [InlineData("s", "https://danbooru.donmai.us", "sensitive")]
        [InlineData("s", "https://konachan.com", "safe")]
        [InlineData("g", "https://danbooru.donmai.us", "general")]
        [InlineData("rating:e", "https://konachan.com", "explicit")]
        public void Rating_letters_become_words(string rating, string url, string expected)
        {
            Assert.Equal(expected, Program.RatingName(rating, new DanbooruProvider { Url = url }));
        }

        [Fact]
        public void Cut_names_end_at_a_tag_boundary()
        {
            Assert.Equal("g - 1 - long_hair blue_eyes", Helper.CutAtTagBoundary("g - 1 - long_hair blue_eyes elysia_(herr"));
            // Keeps the cut when the last space is too early to be worth it.
            Assert.Equal("g - 1 - tttttttttttttttttttt", Helper.CutAtTagBoundary("g - 1 - tttttttttttttttttttt"));
        }

        [Fact]
        public void Command_line_quotes_values_with_spaces()
        {
            Assert.Equal("danboo download --provider \"e621 (Furry)\" --tags \"a b\" --all-pages",
                Flags.ToCommandLine(new[] { "download", "--provider", "e621 (Furry)", "--tags", "a b", "--all-pages" }));
        }
    }
}
