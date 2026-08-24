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
        /// <summary>
        /// Optional URL link (e.g., YouTube video, purchase link)
        /// </summary>
        public string Link { get; set; }

        public object Clone()
        {
            return new TOCEntry()
            {
                SongName = this.SongName,
                Composer = this.Composer,
                Notes = this.Notes,
                Date = this.Date,
                PageNo = this.PageNo,
                Link = this.Link
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
    /// Entry in a playlist, referencing a song from a PDF book.
    /// Only stores the identity fields needed to locate the song;
    /// display information (Composer, Notes) is resolved at runtime
    /// from the corresponding PdfMetaData.
    ///
    /// For regular multi-song PDFs: <see cref="BookName"/> + <see cref="PageNo"/> is the key.
    /// For singles folders: <see cref="BookName"/> + <see cref="SongName"/> is the key
    /// (<see cref="SongName"/> is the PDF basename without extension).  PageNo is unstable
    /// for singles because adding a new file shifts all subsequent page offsets.
    /// </summary>
    [Serializable]
    public class PlaylistEntry
    {
        /// <summary>
        /// The book name (relative path from root folder) — uniquely identifies the PDF book
        /// or singles folder.
        /// </summary>
        public string BookName { get; set; } = string.Empty;

        /// <summary>
        /// The page number in the book — used as the key for regular (non-singles) PDFs.
        /// For singles-folder entries this holds the last-known page offset and is updated
        /// on load, but <see cref="SongName"/> is the authoritative key.
        /// </summary>
        public int PageNo { get; set; }

        /// <summary>
        /// For singles-folder entries: the PDF basename without extension (e.g. "Maple Leaf Rag").
        /// This is stable even when new singles are added to the folder.
        /// Empty for regular multi-song PDF entries.
        /// </summary>
        public string SongName { get; set; } = string.Empty;

        /// <summary>True when this entry was added from a singles folder.</summary>
        public bool IsSinglesEntry => !string.IsNullOrEmpty(SongName);

        public override string ToString()
        {
            return IsSinglesEntry ? $"{BookName} / {SongName}" : $"{BookName} p.{PageNo}";
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

    /// <summary>
    /// A single entry in a piano-roll auto-play playlist.
    /// Stores the path to the cached .mxl file and a display name shown in the UI.
    /// </summary>
    [Serializable]
    public class PianoRollPlaylistEntry
    {
        /// <summary>Full path to the cached .mxl (or plain .xml / .musicxml) file.</summary>
        public string MxlPath { get; set; } = string.Empty;

        /// <summary>Human-readable song title shown in the playlist and the piano-roll overlay.</summary>
        public string DisplayName { get; set; } = string.Empty;

        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// A named ordered list of songs for the piano-roll "player piano" mode.
    /// Songs are played sequentially; the piano roll auto-advances on completion.
    /// </summary>
    [Serializable]
    public class PianoRollPlaylist
    {
        /// <summary>Name of this piano-roll playlist.</summary>
        public string Name { get; set; } = "New PianoRoll Playlist";

        /// <summary>When the playlist was created.</summary>
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        /// <summary>When the playlist was last modified.</summary>
        public DateTime ModifiedDate { get; set; } = DateTime.Now;

        /// <summary>Ordered song entries.</summary>
        public List<PianoRollPlaylistEntry> Entries { get; set; } = new();

        public override string ToString() => $"{Name} ({Entries.Count} songs)";
    }
}
