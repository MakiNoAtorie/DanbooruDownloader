using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;

namespace DanbooruDownloader.Cli.Entity
{
    [XmlType("tags")]
    public class DanbooruTagCollection
    {
        [XmlElementAttribute("tag", Form = System.Xml.Schema.XmlSchemaForm.Unqualified)]
        public DanbooruTag[] Tag { get; set; } = Array.Empty<DanbooruTag>();

        public override string ToString()
        {
            if (Tag == null || Tag.Length == 0) return "";
            return String.Join(" ", Tag.OrderBy(x => x.Name));
        }
    }

    [XmlTypeAttribute(AnonymousType = true)]
    public class DanbooruTag : IComparable<DanbooruTag>, IEqualityComparer<DanbooruTag>
    {
        [XmlAttribute("type")]
        public DanbooruTagType Type { get; set; }

        [XmlAttribute("ambiguous")]
        public bool Ambiguous { get; set; }

        [XmlAttribute("count")]
        public int Count { get; set; }

        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlAttribute("id")]
        public string Id { get; set; }

        public int CompareTo(DanbooruTag obj)
        {
            return string.Compare(this.Name, obj.Name);
        }

        public bool Equals(DanbooruTag x, DanbooruTag y)
        {
            return String.Equals(x.Name, y.Name);
        }

        public int GetHashCode(DanbooruTag obj)
        {
            return obj.Name.GetHashCode();
        }
    }

    public enum DanbooruTagType
    {
        [XmlEnum(Name = "0")]
        General = 0,

        [XmlEnum(Name = "1")]
        Artist = 1,

        [XmlEnum(Name = "3")]
        Copyright = 3,

        [XmlEnum(Name = "4")]
        Character = 4,

        [XmlEnum(Name = "5")]
        Circle = 5,

        [XmlEnum(Name = "6")]
        Faults = 6,

        [XmlEnum(Name = "-1")]
        Unknown = -1
    }
}
