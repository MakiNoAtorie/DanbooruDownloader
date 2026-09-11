using System.Collections.Generic;

namespace DanbooruDownloader.Cli.Entity
{
    public class DanbooruFilenameFormat
    {
        // WinForms app referenced FormMain.MAX_FILENAME_LENGTH (= 255); inlined here since
        // there's no FormMain in the CLI.
        public const int MAX_FILENAME_LENGTH = 255;

        private string _FilenameFormat;

        public string FilenameFormat
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_FilenameFormat))
                {
                    _FilenameFormat = @"%provider%/%rating% - %id% - %tags%";
                }
                return _FilenameFormat;
            }
            set { _FilenameFormat = value; }
        }

        private int _Limit;

        public int Limit
        {
            get
            {
                if (_Limit <= 0 || _Limit > MAX_FILENAME_LENGTH)
                {
                    _Limit = 150;
                }
                return _Limit;
            }
            set { _Limit = value; }
        }

        private string _BaseFolder;

        public string BaseFolder
        {
            get { return _BaseFolder ??= ""; }
            set { _BaseFolder = value; }
        }

        private string _MissingTagReplacement;

        public string MissingTagReplacement
        {
            get { return _MissingTagReplacement ??= ""; }
            set { _MissingTagReplacement = value; }
        }

        private string _artistGroupReplacement;
        private string _copyrightGroupReplacement;
        private string _characterGroupReplacement;
        private string _circleGroupReplacement;
        private string _faultsGroupReplacement;

        public int ArtistGroupLimit { get; set; }

        public string ArtistGroupReplacement
        {
            get { return string.IsNullOrWhiteSpace(_artistGroupReplacement) ? (_artistGroupReplacement = "multiple_artists") : _artistGroupReplacement; }
            set { _artistGroupReplacement = value; }
        }

        public int CopyrightGroupLimit { get; set; }

        public string CopyrightGroupReplacement
        {
            get { return string.IsNullOrWhiteSpace(_copyrightGroupReplacement) ? (_copyrightGroupReplacement = "multiple_copyrights") : _copyrightGroupReplacement; }
            set { _copyrightGroupReplacement = value; }
        }

        public int CharacterGroupLimit { get; set; }

        public string CharacterGroupReplacement
        {
            get { return string.IsNullOrWhiteSpace(_characterGroupReplacement) ? (_characterGroupReplacement = "multiple_characters") : _characterGroupReplacement; }
            set { _characterGroupReplacement = value; }
        }

        public int CircleGroupLimit { get; set; }

        public string CircleGroupReplacement
        {
            get { return string.IsNullOrWhiteSpace(_circleGroupReplacement) ? (_circleGroupReplacement = "multiple_circles") : _circleGroupReplacement; }
            set { _circleGroupReplacement = value; }
        }

        public int FaultsGroupLimit { get; set; }

        public string FaultsGroupReplacement
        {
            get { return string.IsNullOrWhiteSpace(_faultsGroupReplacement) ? (_faultsGroupReplacement = "multiple_faults") : _faultsGroupReplacement; }
            set { _faultsGroupReplacement = value; }
        }

        public List<DanbooruTag> IgnoredTags { get; set; } = new List<DanbooruTag>();

        public string IgnoredTagsRegex { get; set; }

        public bool IgnoreTagsUseRegex { get; set; }

        public bool IsReplaceMode { get; set; }

        public bool IgnoredTagsOnlyForGeneral { get; set; }

        public bool TagReplaceUnderscoreToSpace { get; set; }
    }
}
