using DanbooruDownloader.Cli.Entity;
using DanbooruDownloader.Cli.Tags;

namespace DanbooruDownloader.Cli.Tests
{
    public class TagParsingTests
    {
        [Fact]
        public void Gelbooru_element_style_tags_parse_with_gelbooru_type_numbers()
        {
            var xml = """<?xml version="1.0"?><tags type="array" count="3"><tag><id>855</id><name>hakurei_reimu</name><count>87054</count><type>4</type><ambiguous>0</ambiguous></tag><tag><id>9</id><name>highres</name><count>5</count><type>5</type><ambiguous>0</ambiguous></tag><tag><id>1</id><name/><count>1</count><type>4</type><ambiguous>0</ambiguous></tag></tags>""";

            var tags = TagDownloader.ParseTags(xml, BoardType.Gelbooru);

            Assert.Equal(3, tags.Count);
            Assert.Equal("hakurei_reimu", tags[0].Name);
            Assert.Equal(DanbooruTagType.Character, tags[0].Type);
            Assert.Equal(87054, tags[0].Count);
            Assert.Equal(DanbooruTagType.Faults, tags[1].Type);
            Assert.True(string.IsNullOrEmpty(tags[2].Name));
        }

        [Fact]
        public void Moebooru_attribute_style_tags_parse()
        {
            var xml = """<?xml version="1.0" encoding="UTF-8"?><tags type="array"><tag id="1" name="some_circle" count="3" type="5" ambiguous="false"/><tag id="2" name="odd" count="1" type="2" ambiguous="true"/></tags>""";

            var tags = TagDownloader.ParseTags(xml, BoardType.Danbooru);

            Assert.Equal(DanbooruTagType.Circle, tags[0].Type);
            Assert.Equal(DanbooruTagType.Unknown, tags[1].Type);
            Assert.True(tags[1].Ambiguous);
        }
    }
}
