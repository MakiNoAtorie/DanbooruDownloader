using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace DanbooruDownloader.Cli.Entity
{
    public class DanbooruPost
    {
        public DanbooruProvider Provider { get; set; }

        public string Query { get; set; }

        public string SearchTags { get; set; }

        public string Referer { get; set; }

        public bool Completed { get; set; }

        public string Progress { get; set; }

        public string Id { get; set; }

        public string Tags { get; set; }

        public string Source { get; set; }

        public string Score { get; set; }

        public string Rating { get; set; }

        public string Status { get; set; } = "-";

        public bool HasChildren { get; set; }

        public string ParentId { get; set; }

        public string Change { get; set; }

        public string CreatorId { get; set; }

        public string CreatedAt { get; set; }

        public DateTime CreatedAtDateTime { get; set; }

        public string MD5 { get; set; }

        private List<DanbooruTag> tagsEntity;

        public List<DanbooruTag> TagsEntity
        {
            get
            {
                if (tagsEntity == null)
                {
                    tagsEntity = Helper.ParseTags(Tags, Provider);
                }
                return tagsEntity;
            }
            set { this.tagsEntity = value; }
        }

        public int Filesize { get; set; }

        public string Filename { get; set; }

        public bool Hidden { get; set; }

        /* if jpeg_url not exist, then the url will be taken from sample_url
         * if sample_url not exist, then the url will be taken from file_url
         */
        public string FileUrl { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }

        public string PreviewUrl { get; set; }

        public int PreviewWidth { get; set; }

        public int PreviewHeight { get; set; }

        private string _sample_url;

        public string SampleUrl
        {
            get
            {
                if (String.IsNullOrWhiteSpace(_sample_url))
                {
                    _sample_url = FileUrl;
                }
                return _sample_url;
            }
            set { this._sample_url = value; }
        }

        private int _sample_width;

        public int SampleWidth
        {
            get
            {
                if (_sample_width == 0)
                {
                    _sample_width = Width;
                }
                return _sample_width;
            }
            set { this._sample_width = value; }
        }

        private int _sample_height;

        public int SampleHeight
        {
            get
            {
                if (_sample_height == 0)
                {
                    _sample_height = Height;
                }
                return _sample_height;
            }
            set { this._sample_height = value; }
        }

        private string _jpeg_url;

        public string JpegUrl
        {
            get
            {
                if (String.IsNullOrWhiteSpace(_jpeg_url))
                {
                    _jpeg_url = SampleUrl;
                }
                return _jpeg_url;
            }
            set { this._jpeg_url = value; }
        }

        private int _jpeg_width;

        public int JpegWidth
        {
            get
            {
                if (_jpeg_width == 0)
                {
                    _jpeg_width = Width;
                }
                return _jpeg_width;
            }
            set { this._jpeg_width = value; }
        }

        private int _jpeg_height;

        public int JpegHeight
        {
            get
            {
                if (_jpeg_height == 0)
                {
                    _jpeg_height = Height;
                }
                return _jpeg_height;
            }
            set { this._jpeg_height = value; }
        }
    }
}
