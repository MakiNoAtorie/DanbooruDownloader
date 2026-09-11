using DanbooruDownloader.Cli.Entity;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Xml;

namespace DanbooruDownloader.Cli.DAO
{
    public class DanbooruProviderDao
    {
        public const string DATETIME_FORMAT_UNIX = "unix-timestamp";
        public const string DATETIME_FORMAT_NA = "not-available";

        public List<DanbooruProvider> Read(string filename = @"DanbooruProviderList.xml")
        {
            List<DanbooruProvider> list = new List<DanbooruProvider>();

            using (XmlTextReader reader = new XmlTextReader(filename))
            {
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element) continue;
                    if (reader.Name.Equals("DanbooruProviderList")) continue;
                    if (!reader.Name.Equals("DanbooruProvider")) continue;

                    var cookieAlwaysAskValue = "";
                    DanbooruProvider newProvider = new DanbooruProvider();
                    do
                    {
                        reader.Read();
                        if (reader.NodeType.Equals(XmlNodeType.Element))
                        {
                            switch (reader.Name)
                            {
                                case "Name": newProvider.Name = reader.ReadElementContentAsString(); break;
                                case "Url": newProvider.Url = reader.ReadElementContentAsString(); break;
                                case "QueryStringJson": newProvider.QueryStringJson = reader.ReadElementContentAsString(); break;
                                case "QueryStringHtml": newProvider.QueryStringHtml = reader.ReadElementContentAsString(); break;
                                case "QueryStringXml": newProvider.QueryStringXml = reader.ReadElementContentAsString(); break;
                                case "Preferred":
                                    string preferred = reader.ReadElementContentAsString().ToLowerInvariant();
                                    switch (preferred)
                                    {
                                        case "json": newProvider.Preferred = PreferredMethod.Json; break;
                                        case "xml": newProvider.Preferred = PreferredMethod.Xml; break;
                                        case "html": newProvider.Preferred = PreferredMethod.Html; break;
                                        default: throw new Exception("Invalid Provider Type in DanbooruProviderList.xml: " + preferred);
                                    }
                                    break;

                                case "DefaultLimit": newProvider.DefaultLimit = reader.ReadElementContentAsInt(); break;
                                case "HardLimit": newProvider.HardLimit = reader.ReadElementContentAsInt(); break;
                                case "UserName":
                                    if (String.IsNullOrWhiteSpace(cookieAlwaysAskValue))
                                        newProvider.UserName = reader.ReadElementContentAsString();
                                    else
                                        newProvider.UserName = cookieAlwaysAskValue;
                                    break;

                                case "Password": newProvider.Password = reader.ReadElementContentAsString(); break;
                                case "UseAuth":
                                    // compat
                                    var isAuth = reader.ReadElementContentAsBoolean();
                                    if (isAuth) newProvider.LoginType = LoginType.UserPass;
                                    break;

                                case "LoginType":
                                    string ltype = reader.ReadElementContentAsString();
                                    newProvider.LoginType = (LoginType)Enum.Parse(typeof(LoginType), ltype);
                                    // Unlike the WinForms app (which eagerly popped an InputBox for
                                    // every CookieAlwaysAsk provider while loading the whole list),
                                    // the CLI defers this prompt to DanbooruProvider.GetQueryUrl(),
                                    // so only the provider actually being used ever blocks for input -
                                    // otherwise every command would hang on unrelated providers.
                                    break;

                                case "PasswordSalt": newProvider.PasswordSalt = reader.ReadElementContentAsString(); break;
                                case "PasswordHash": newProvider.PasswordHash = reader.ReadElementContentAsString(); break;
                                case "ApiKey": newProvider.ApiKey = reader.ReadElementContentAsString(); break;
                                case "BoardType":
                                    string type = reader.ReadElementContentAsString();
                                    newProvider.BoardType = (BoardType)Enum.Parse(typeof(BoardType), type);
                                    break;

                                case "TagDownloadUseLoop": newProvider.TagDownloadUseLoop = reader.ReadElementContentAsBoolean(); break;
                                case "DateTimeFormat": newProvider.DateTimeFormat = reader.ReadElementContentAsString(); break;
                            }
                        }
                    } while (!reader.Name.Equals("DanbooruProvider"));
                    list.Add(newProvider);
                }
            }
            return list;
        }

        public void Save(List<DanbooruProvider> list, string filename = @"DanbooruProviderList.xml")
        {
            XmlWriterSettings setting = new XmlWriterSettings();
            setting.Indent = true;

            using (XmlWriter writer = XmlWriter.Create(filename, setting))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("DanbooruProviderList");
                foreach (DanbooruProvider p in list)
                {
                    writer.WriteStartElement("DanbooruProvider");
                    PropertyInfo[] propertyInfos = typeof(DanbooruProvider).GetProperties();
                    foreach (PropertyInfo info in propertyInfos)
                    {
                        if (!info.CanWrite || info.GetSetMethod() == null) continue;

                        var value = info.GetValue(p, null);
                        if (value == null) value = "";
                        if (info.Name == "Password") value = ""; // never persist the plaintext password

                        if (info.PropertyType.Name == "Boolean")
                            writer.WriteElementString(info.Name, value.ToString().ToLowerInvariant());
                        else
                            writer.WriteElementString(info.Name, value.ToString());
                    }
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
                writer.WriteEndDocument();
                writer.Flush();
            }
        }
    }
}
