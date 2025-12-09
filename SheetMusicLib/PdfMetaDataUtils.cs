namespace SheetMusicLib
{
    /// <summary>
    /// Platform-independent PDF metadata operations and utilities
    /// </summary>
    public static class PdfMetaDataUtils
    {
        /// <summary>
        /// Get the BMK metadata filename for a PDF file or singles folder
        /// </summary>
        public static string GetBmkFileName(string fullPathFile)
        {
            return Path.ChangeExtension(fullPathFile, "bmk");
        }

        /// <summary>
        /// Calculate volume number from page number
        /// </summary>
        public static int GetVolNumFromPageNum(int pageNo, int pageNumberOffset, IList<PdfVolumeInfoBase> volumes)
        {
            var volno = 0;
            var pSum = pageNumberOffset;
            foreach (var vol in volumes)
            {
                pSum += vol.NPagesInThisVolume;
                if (pageNo < pSum)
                {
                    break;
                }
                volno++;
            }
            return volno;
        }

        /// <summary>
        /// Calculate page number at start of volume
        /// </summary>
        public static int GetPagenoOfVolume(int volno, int pageNumberOffset, IList<PdfVolumeInfoBase> volumes)
        {
            var pgno = pageNumberOffset;
            for (int i = 0; i < volno && i < volumes.Count; i++)
            {
                pgno += volumes[i].NPagesInThisVolume;
            }
            return pgno;
        }

        /// <summary>
        /// Calculate total page count across all volumes
        /// </summary>
        public static int GetTotalPageCount(IList<PdfVolumeInfoBase> volumes)
        {
            return volumes.Sum(v => v.NPagesInThisVolume);
        }

        /// <summary>
        /// Initialize TOC dictionary from list, cleaning up quotes
        /// </summary>
        public static SortedList<int, List<TOCEntry>> InitializeDictToc(List<TOCEntry> lstTocEntries)
        {
            var dictToc = new SortedList<int, List<TOCEntry>>();
            foreach (var toc in lstTocEntries)
            {
                toc.SongName = StringUtilities.RemoveQuotes(toc.SongName);
                toc.Date = StringUtilities.RemoveQuotes(toc.Date);
                toc.Composer = StringUtilities.RemoveQuotes(toc.Composer);
                if (!dictToc.TryGetValue(toc.PageNo, out var tocLst))
                {
                    tocLst = new List<TOCEntry>();
                    dictToc[toc.PageNo] = tocLst;
                }
                tocLst.Add(toc);
            }
            return dictToc;
        }

        /// <summary>
        /// Initialize favorites dictionary from list
        /// </summary>
        public static SortedList<int, Favorite> InitializeDictFav(List<Favorite> favorites)
        {
            var dictFav = new SortedList<int, Favorite>();
            foreach (var fav in favorites)
            {
                dictFav[fav.Pageno] = fav;
            }
            return dictFav;
        }

        /// <summary>
        /// Initialize ink strokes dictionary from list
        /// </summary>
        public static SortedList<int, InkStrokeClass> InitializeDictInkStrokes(List<InkStrokeClass> lstInkStrokes)
        {
            var dictInkStrokes = new SortedList<int, InkStrokeClass>();
            foreach (var ink in lstInkStrokes)
            {
                dictInkStrokes[ink.Pageno] = ink;
            }
            return dictInkStrokes;
        }

        /// <summary>
        /// Get description for a page from TOC
        /// </summary>
        public static string GetDescription(int pageNo, SortedList<int, List<TOCEntry>> dictToc, string fallbackDescription)
        {
            var str = string.Empty;

            if (!dictToc.TryGetValue(pageNo, out var lstTocs))
            {
                // find the first one beyond, then go back 1
                var ndxclosest = dictToc.Keys.FindIndexOfFirstGTorEQTo(pageNo);
                if (ndxclosest > 0 && ndxclosest <= dictToc.Count)
                {
                    var key = dictToc.Keys[ndxclosest - 1];
                    lstTocs = dictToc[key];
                }
            }

            if (lstTocs != null)
            {
                int cnt = 0;
                foreach (var toce in lstTocs)
                {
                    var val = $"{toce.SongName} {toce.Composer} {toce.Date} {toce.Notes}".Trim();
                    if (cnt++ > 0)
                    {
                        str += " | ";
                    }
                    str += val;
                }
            }
            else
            {
                str = fallbackDescription;
            }
            return str.Trim();
        }
    }
}
