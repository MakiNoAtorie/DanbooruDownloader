using DanbooruDownloader.Cli.Config;
using DanbooruDownloader.Cli.Entity;

namespace DanbooruDownloader.Cli.Tests
{
    public class EnvFileTests
    {
        [Fact]
        public void Parse_handles_comments_quotes_and_export()
        {
            var values = EnvFile.Parse(new[] { "# c", "", "A=1", "export B = \"two words\"", "C='x=y'", "D=", "junk" });

            Assert.Equal("1", values["A"]);
            Assert.Equal("two words", values["B"]);
            Assert.Equal("x=y", values["C"]);
            Assert.Equal("", values["D"]);
            Assert.False(values.ContainsKey("junk"));
        }

        [Fact]
        public void Apply_sets_api_key_and_cookie_logins_and_skips_empty_values()
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllLines(path, new[]
                {
                    "TEST_BOORU_COM_API_KEY=k", "TEST_BOORU_COM_USER=12",
                    "TEST_COOKIE_HTTPS_COOKIE=a=b; c=d",
                    "TEST_EMPTY_API_KEY=",
                });
                var gel = new DanbooruProvider { Name = "test-booru.com", LoginType = LoginType.Cookie };
                var cookie = new DanbooruProvider { Name = "Test Cookie (HTTPS)" };
                var empty = new DanbooruProvider { Name = "test empty", LoginType = LoginType.Anonymous };

                EnvFile.Apply(new List<DanbooruProvider> { gel, cookie, empty }, path);

                Assert.Equal(LoginType.ApiKey, gel.LoginType);
                Assert.Equal("k", gel.ApiKey);
                Assert.Equal("12", gel.UserName);
                Assert.Equal(LoginType.Cookie, cookie.LoginType);
                Assert.Equal("a=b; c=d", cookie.UserName);
                Assert.Equal(LoginType.Anonymous, empty.LoginType);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
