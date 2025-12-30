using System.Xml.Serialization;
using System.Text;
using System.Text.Json;

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

    /// <summary>
    /// Entry in a playlist, referencing a song from a PDF book
    /// </summary>
    [Serializable]
    public class PlaylistEntry
    {
        /// <summary>
        /// The song name from TOC
        /// </summary>
        public string SongName { get; set; } = string.Empty;
        
        /// <summary>
        /// The composer from TOC
        /// </summary>
        public string Composer { get; set; } = string.Empty;
        
        /// <summary>
        /// The page number in the book
        /// </summary>
        public int PageNo { get; set; }
        
        /// <summary>
        /// The book name (relative path from root folder)
        /// </summary>
        public string BookName { get; set; } = string.Empty;
        
        /// <summary>
        /// Optional notes
        /// </summary>
        public string Notes { get; set; } = string.Empty;

        public override string ToString()
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(SongName)) parts.Add(SongName);
            if (!string.IsNullOrEmpty(Composer)) parts.Add(Composer);
            parts.Add($"p.{PageNo}");
            return string.Join(" - ", parts);
        }
    }

    /// <summary>
    /// A named playlist containing multiple song entries
    /// </summary>
    [Serializable]
    public class Playlist
    {
        /// <summary>
        /// Name of the playlist
        /// </summary>
        public string Name { get; set; } = "New Playlist";
        
        /// <summary>
        /// When the playlist was created
        /// </summary>
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        
        /// <summary>
        /// When the playlist was last modified
        /// </summary>
        public DateTime ModifiedDate { get; set; } = DateTime.Now;
        
        /// <summary>
        /// The songs in this playlist
        /// </summary>
        public List<PlaylistEntry> Entries { get; set; } = new();

        public override string ToString()
        {
            return $"{Name} ({Entries.Count} songs)";
        }
    }
}
