using DanbooruDownloader.Cli.Entity;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace DanbooruDownloader.Cli.DAO
{
    public class DanbooruTagsDao
    {
        public static XmlSerializer _ser = new XmlSerializer(typeof(DanbooruTagCollection));
        public DanbooruTagCollection Tags { get; set; }

        // Unlike the WinForms app (which requires tags.xml and prompts the user to
        // download it on startup), the CLI treats a missing tags.xml as "no tag
        // categorization available yet" rather than a fatal error, so `search`/`download`
        // work without it. Tag category grouping (artist/character/etc.) is a later
        // feature (tags.xml download command) - until then, tags just resolve as Unknown.
        public DanbooruTagsDao(string xmlTagFile)
        {
            if (!File.Exists(xmlTagFile))
            {
                Program.Logger.Warn($"{xmlTagFile} not found - tag categorization disabled until it's downloaded.");
                this.Tags = new DanbooruTagCollection();
                return;
            }
            using (StreamReader s = File.OpenText(xmlTagFile))
            {
                try
                {
                    this.Tags = (DanbooruTagCollection)_ser.Deserialize(s);
                }
                catch (Exception ex)
                {
                    Program.Logger.Error("Failed to parse: " + xmlTagFile, ex);
                    this.Tags = new DanbooruTagCollection();
                }
            }
        }

        private Dictionary<string, DanbooruTag> _byName;

        public DanbooruTag GetTag(string tag)
        {
            // tags.xml can hold many thousands of tags and this runs for every tag of every post.
            _byName ??= (Tags.Tag ?? Array.Empty<DanbooruTag>())
                .Where(x => x.Name != null)
                .GroupBy(x => x.Name)
                .ToDictionary(g => g.Key, g => g.First());
            if (_byName.TryGetValue(tag, out var result)) return result;

            return new DanbooruTag
            {
                Name = tag,
                Type = DanbooruTagType.Unknown,
                Count = -1,
                Id = "-1"
            };
        }

        public List<DanbooruTag> ParseTagsString(string tagsStr)
        {
            List<DanbooruTag> tags = new List<DanbooruTag>();
            if (!String.IsNullOrWhiteSpace(tagsStr))
            {
                var tokens = tagsStr.Split(' ');
                foreach (var item in tokens)
                {
                    if (String.IsNullOrWhiteSpace(item)) continue;
                    tags.Add(GetTag(item.Trim()));
                }
            }
            return tags;
        }

        public static void Save(string xmlTagFile, List<DanbooruTag> tags)
        {
            using StreamWriter s = File.CreateText(xmlTagFile);
            var col = new DanbooruTagCollection { Tag = tags.ToArray() };
            _ser.Serialize(s, col);
        }

        private static DanbooruTagsDao _DefaultTagsDao;

        public static DanbooruTagsDao Instance
        {
            get
            {
                if (_DefaultTagsDao == null)
                {
                    _DefaultTagsDao = new DanbooruTagsDao("tags.xml");
                }
                return _DefaultTagsDao;
            }
            set { _DefaultTagsDao = value; }
        }
    }
}
