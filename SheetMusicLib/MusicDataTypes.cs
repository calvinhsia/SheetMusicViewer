using System.Xml.Serialization;

namespace SheetMusicLib
{
    /// <summary>
    /// Base class for page-based entries
    /// </summary>
    public class PageNoBaseClass
    {
        public int Pageno { get; set; }
    }

    /// <summary>
    /// Table of Contents Entry
    /// </summary>
    [Serializable]
    public class TOCEntry : ICloneable
    {
        public string SongName { get; set; }
        public string Composer { get; set; }
        public string Notes { get; set; }
        /// <summary>
        /// Composition Date
        /// </summary>
        public string Date { get; set; }
        public int PageNo { get; set; }

        public object Clone()
        {
            return new TOCEntry()
            {
                SongName = this.SongName,
                Composer = this.Composer,
                Notes = this.Notes,
                Date = this.Date,
                PageNo = this.PageNo
            };
        }

        public override string ToString()
        {
            return $"{PageNo} {SongName} {Composer} {Date} {Notes}".Trim();
        }
    }

    /// <summary>
    /// Favorite page marker
    /// </summary>
    [Serializable]
    public class Favorite : PageNoBaseClass
    {
        public string FavoriteName { get; set; }

        public override string ToString()
        {
            return $"{FavoriteName} {Pageno}".Trim();
        }
    }

    /// <summary>
    /// Ink stroke data for a page
    /// </summary>
    [Serializable]
    public class InkStrokeClass : PageNoBaseClass
    {
        /// <summary>
        /// Canvas dimensions when ink was captured (Width=X, Height=Y)
        /// </summary>
        public PortablePoint InkStrokeDimension { get; set; }

        /// <summary>
        /// Stroke data (either ISF binary or JSON UTF-8 bytes)
        /// </summary>
        public byte[] StrokeData { get; set; }
    }

    /// <summary>
    /// PDF volume information (portable version without platform-specific Task dependency)
    /// </summary>
    [Serializable]
    public class PdfVolumeInfoBase
    {
        /// <summary>
        /// The number of PDF pages in this PDF file
        /// </summary>
        [XmlElement("NPages")]
        public int NPagesInThisVolume { get; set; }

        /// <summary>
        /// Rotation: Normal = 0, Rotate90 = 1, Rotate180 = 2, Rotate270 = 3
        /// </summary>
        public int Rotation { get; set; }

        /// <summary>
        /// The filename (with extension) for the PDF document.
        /// Can't be relative to rootfolder: user could change rootfolder to folder inside,
        /// so must be relative to fullpath: needs to be portable from machine to machine.
        /// </summary>
        [XmlElement("FileName")]
        public string FileNameVolume { get; set; }

        public override string ToString()
        {
            return $"{FileNameVolume} #Pgs={NPagesInThisVolume,4} Rotation={(PortableRotation)Rotation}";
        }
    }

    /// <summary>
    /// Comparer for TOC entries by song name
    /// </summary>
    public class TocEntryComparer : IComparer<TOCEntry>
    {
        public int Compare(TOCEntry x, TOCEntry y)
        {
            return string.Compare(x.SongName, y.SongName);
        }
    }

    /// <summary>
    /// Comparer for page-based entries
    /// </summary>
    public class PageNoBaseClassComparer : IComparer<PageNoBaseClass>
    {
        public int Compare(PageNoBaseClass x, PageNoBaseClass y)
        {
            return x.Pageno == y.Pageno ? 0 : (x.Pageno < y.Pageno ? -1 : 1);
        }
    }

    /// <summary>
    /// Comparer for PDF volume info by filename
    /// </summary>
    public class PdfVolumeInfoBaseComparer : IComparer<PdfVolumeInfoBase>
    {
        public int Compare(PdfVolumeInfoBase x, PdfVolumeInfoBase y)
        {
            return string.Compare(x.FileNameVolume, y.FileNameVolume);
        }
    }
}
