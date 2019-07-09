using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Serialization;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace WpfPdfViewer
{
    /// <summary>
    /// The serialized info for a PDF is in a file with the same name as the PDF with the extension changed to ".bmk"
    /// Some PDFs are a series of scanned docs, numbered, e.g. 0,1,2,3...
    /// There will be a single .bmk for all volumes of a multi volume set series.
    /// </summary>
    [Serializable]
    public class PdfMetaData
    {
        /// <summary>
        /// full path to 1st Pdf file in volume (vol0)
        /// </summary>
        [XmlIgnore]
        public string _FullPathFile;

        /// <summary>
        /// num pages in all volumes of set
        /// </summary>
        [XmlIgnore]
        public int NumPagesInSet => lstVolInfo.Sum(p => p.NPagesInThisVolume);


        public List<PdfVolumeInfo> lstVolInfo = new List<PdfVolumeInfo>();
        /// <summary>
        /// the page no when this PDF was last opened
        /// </summary>
        public int LastPageNo;

        internal bool IsDirty = false;
        int initialLastPageNo;

        /// <summary>
        /// A Singles Folder is treated differently: there is only one BMK file next to the Singles directory "Singles.bmk" next to dir "Singles" (not in dir "Singles")
        /// Each PDF is treated as a single song (perhaps with a few pages). SongName is Name of file
        /// The Table Of Contents is read in from the BMK if it exists
        /// Then the dir is enumerated in alpha order and the TOC is checked against the contents. 
        /// If user added more items, then they are inserted and the TOC is updated so that the page numbers are adjusted, so that Inking and Favorites work
        /// The VolInfo structure contains the file name (no path)
        /// </summary>
        [XmlIgnore]
        public bool IsSinglesFolder;

        [XmlIgnore]
        public int MaxPageNum => PageNumberOffset + NumPagesInSet;

        /// <summary>
        /// to prevent overwriting of data written externally 
        /// </summary>
        [XmlIgnore]
        DateTime dtLastWrite;

        /// <summary>
        /// The Table of contents of a songbook shows the physical page numbers, which may not match the actual PDF page numbers (there could be a cover page scanned or could be a multivolume set, or 30 pages of intro, and then page 1 has the 1st song)
        /// Also, each scanned page might have the physical page # printed on it. 
        /// We want to keep the scanned OCR TOC true and minimize required editing. This means the page no displayed in the UI is the same as the page # on the scanned page
        /// PageNumberOffset will map between each so that the imported scanned TOC saved as XML will not need to be adjusted.
        /// For 1st, 2nd, 3rd volumes, the offset from the actual scanned page number (as visible on the page) to the PDF page number
        /// e.g. the very 1st volume might have a cover page, which is page 0 and 44 pages of intro. Viewing song "The Crush Collision March" might show page 4, but it's really PdfPage=49, 
        /// so we set PageNumberOffset to -45
        /// Another way to think about it: find a page with a printed page no on it, e.g. page 3. Count back to the 1st PDF page (probably the book cover) PageNumberOffset is the resulting count
        /// </summary>
        public int PageNumberOffset;

        public string Notes;

        public List<InkStrokeClass> LstInkStrokes = new List<InkStrokeClass>();

        public List<Favorite> Favorites = new List<Favorite>();

        public List<TOCEntry> lstTocEntries = new List<TOCEntry>();

        [XmlIgnore] // can't serialize dictionaries.Could be multiple TOCEntries for a single page Page #=> List<TOCEntry>
        public SortedList<int, List<TOCEntry>> dictToc = new SortedList<int, List<TOCEntry>>();
        [XmlIgnore]
        public SortedList<int, Favorite> dictFav = new SortedList<int, Favorite>();

        [XmlIgnore]
        public SortedList<int, InkStrokeClass> dictInkStrokes = new SortedList<int, InkStrokeClass>();

        [XmlIgnore]
        internal BitmapImage bitmapImageCache;
        /// <summary>
        /// Get a description of the page. If the page isn't in the TOC, it might be the 2nd page of a multi-page song
        /// so find the nearest TOC entry before it.
        /// </summary>
        /// <param name="pageNo"></param>
        /// <returns></returns>
        public string GetDescription(int pageNo)
        {
            var str = string.Empty;
            var pMetaDataItem = this;

            var tocPgNm = pageNo;
            if (!pMetaDataItem.dictToc.TryGetValue(tocPgNm, out var lstTocs))
            {
                // find the 1st one beyond, then go back 1
                var ndxclosest = pMetaDataItem.dictToc.Keys.FindIndexOfFirstGTorEQTo(tocPgNm);
                if (ndxclosest > 0 && ndxclosest <= pMetaDataItem.dictToc.Count)
                {
                    var key = pMetaDataItem.dictToc.Keys[ndxclosest - 1];
                    lstTocs = pMetaDataItem.dictToc[key];
                }
            }
            if (lstTocs != null)
            {
                int cnt = 0;
                foreach (var toce in lstTocs)
                {
                    var val = $"{toce.SongName} {toce.Composer} {toce.Date} {toce.Notes}".Trim(); //without page num
                    if (cnt++ > 0)
                    {
                        str += " | ";
                    }
                    str += val;

                }
            }
            else
            {
                str = $"{this}";
            }
            return str.Trim();
        }

        string GetFullPathFilePrivate(int volNo)
        {
            var retval = Path.Combine(
                (IsSinglesFolder ? this._FullPathFile : Path.GetDirectoryName(this._FullPathFile)),
                lstVolInfo[volNo].FileNameVolume);
            //string FormatVolno(int v)
            //{
            //    string str;
            //    if (lstVolInfo.Count < 10)
            //    {
            //        str = $"{v}";
            //    }
            //    else
            //    {
            //        var d = v / 10;
            //        str = $"{d}{v - d * 10}";
            //    }
            //    return str;
            //}
            //if (retval.EndsWith("0.pdf"))
            //{
            //    if (retval.EndsWith("00.pdf"))
            //    {
            //        retval = retval.Replace("00.pdf", string.Empty) + FormatVolno(volNo) + ".pdf";
            //    }
            //    else
            //    {
            //        retval = retval.Replace("0.pdf", string.Empty) + FormatVolno(volNo) + ".pdf";
            //    }
            //}
            //else if (_FullPathFile.EndsWith("1.pdf"))
            //{
            //    // no page 0, so 1 based
            //    if (retval.EndsWith("01.pdf"))
            //    {
            //        retval = retval.Replace("01.pdf", string.Empty) + FormatVolno(volNo + 1) + ".pdf";
            //    }
            //    else
            //    {
            //        retval = retval.Replace("1.pdf", string.Empty) + FormatVolno(volNo + 1) + ".pdf";
            //    }
            //}
            //else if (this.lstVolInfo.Count > 0) //there's more than 1 entry
            //{
            //    if (volNo == 0)
            //    {
            //    }
            //    else
            //    {
            //        retval = retval.Replace(".pdf", string.Empty) + FormatVolno(volNo) + ".pdf";
            //    }
            //}
            Debug.Assert(File.Exists(retval));
            return retval;
        }
        public string GetFullPathFileFromVolno(int volNo, bool MakeRelative = false)
        {
            var retval = GetFullPathFilePrivate(volNo);
            if (MakeRelative)
            {
                if (retval != null)
                {
                    retval = retval.Substring(PdfViewerWindow.s_pdfViewerWindow._RootMusicFolder.Length + 1).Replace(".pdf", string.Empty);
                    var lastcharVol0 = retval.Last();
                    if ("01".Contains(lastcharVol0)) // if the last char is 0 or 1
                    {
                        retval = retval.Substring(0, retval.Length - 1);
                    }
                }
            }
            return retval;
        }
        public string GetFullPathFileFromPageNo(int pageNo)
        {
            var volNo = GetVolNumFromPageNum(pageNo);
            var retval = GetFullPathFileFromVolno(volNo);
            return retval;
        }

        public int GetVolNumFromPageNum(int pageNo)
        {
            var volno = 0;
            var pSum = PageNumberOffset;
            foreach (var vol in lstVolInfo)
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
        public int GetPagenoOfVolume(int volno)
        {
            var pgno = PageNumberOffset;
            Debug.Assert(volno < lstVolInfo.Count);
            for (int i = 0; i < volno; i++)
            {
                pgno += lstVolInfo[i].NPagesInThisVolume;
            }
            return pgno;
        }

        internal static async Task<(List<PdfMetaData>, List<string>)> LoadAllPdfMetaDataFromDiskAsync(string rootMusicFolder)
        {
            var lstPdfMetaFileData = new List<PdfMetaData>();
            var lstFolders = new List<string>(); // folder names modulo root
            if (!string.IsNullOrEmpty(rootMusicFolder) && Directory.Exists(rootMusicFolder))
            {
                var pathCurrentMusicFolder = rootMusicFolder;
                lstPdfMetaFileData.Clear();
                await Task.Run(async () =>
                {
                    PdfMetaData curmetadata = null;
                    int nContinuations = 0;
                    await recurDirsAsync(pathCurrentMusicFolder);
                    async Task<bool> TryAddFileAsync(string curFullPathFile)
                    {
                        var retval = false;
                        try
                        {
                            curmetadata = await PdfMetaData.ReadPdfMetaDataAsync(curFullPathFile);
                            if (curmetadata != null)
                            {
                                lstPdfMetaFileData.Add(curmetadata);
                            }
                            retval = true;
                        }
                        catch (Exception ex)
                        {
                            PdfViewerWindow.s_pdfViewerWindow.OnException($"Reading {curFullPathFile}", ex);
                        }
                        return retval;
                    }
                    void SaveMetaData() // if we created a new one or modified it (added volumes), save it
                    {
                        if (curmetadata != null)
                        {
                            // for single songs not in SinglesFolder: if no TOC and # pages < 11, create a new TOC entry with 1 song: same as file name
                            if (!curmetadata.IsSinglesFolder && curmetadata.lstTocEntries.Count == 0 && curmetadata.NumPagesInSet < 11)
                            {
                                curmetadata.lstTocEntries.Add(new TOCEntry()
                                {
                                    SongName = Path.GetFileNameWithoutExtension(curmetadata._FullPathFile)
                                });
                                curmetadata.IsDirty = true;
                            }
                        }
                        if (curmetadata?.IsDirty == true)
                        {
                            if (curmetadata.lstVolInfo.Count != nContinuations + 1) // +1 for root
                            {
                                "adf".ToString();
                            }
                            curmetadata.SaveIfDirty();
                        }
                        nContinuations = 0;
                    }
                    async Task<int> recurDirsAsync(string curPath)
                    {
                        var lastFile = string.Empty;
                        nContinuations = 0;
                        var cntItems = 0;
                        int curVolNo = 0; // 
                        try
                        {
                            if (curPath.EndsWith("singles", StringComparison.InvariantCultureIgnoreCase))
                            {// we treat Singles as a book with multiple songs.
                                lstPdfMetaFileData.Add(await HandleSinglesFolderAsync(curPath));
                            }
                            else
                            {
                                var curVolIsOneBased = false;// zero based by default
                                foreach (var file in Directory.EnumerateFiles(curPath, "*.pdf").OrderBy(f => f.ToLower()))//.Where(f=>f.Contains("Miser"))) // "file" is fullpath
                                {
                                    if (file.Contains("Treem"))
                                    {
                                        "".ToString();
                                    }
                                    var isContinuation = false;
                                    curVolIsOneBased = false;
                                    if (!string.IsNullOrEmpty(lastFile) &&
                                            curmetadata != null &&
                                            System.IO.Path.GetDirectoryName(lastFile) == System.IO.Path.GetDirectoryName(file)) // same dir
                                    {
                                        var justFnameVol0 = System.IO.Path.GetFileNameWithoutExtension(curmetadata._FullPathFile).Trim().ToLower();
                                        var lastcharVol0 = justFnameVol0.Last();
                                        if ("01".Contains(lastcharVol0)) // if the last char is 0 or 1
                                        {
                                            curVolIsOneBased = lastcharVol0 == '1';
                                            justFnameVol0 = justFnameVol0.Substring(0, justFnameVol0.Length - 1);
                                        }
                                        var justfnameCurrent = System.IO.Path.GetFileNameWithoutExtension(file).Trim().ToLower();
                                        if (justFnameVol0.Length < justfnameCurrent.Length &&
                                            justFnameVol0 == justfnameCurrent.Substring(0, justFnameVol0.Length))
                                        {
                                            if (char.IsDigit(justfnameCurrent.Substring(justFnameVol0.Length)[0]))
                                            {
                                                /// all continuations must have file length > base name (without trailing minus "0" or "1")
                                                /// and must be extended with at least one digit.
                                                /// Thus, book1, book1a, book2 are all treated together as one
                                                /// but not bookI, bookI1, bookII: this is 2 books: "bookI" and "bookI1" are the 1st part, and "bookII" is the second.
                                                isContinuation = true;
                                            }
                                            //if (int.TryParse(justfnameCurrent.Substring(justFnameVol0.Length), out var volNo))
                                            //{
                                            //    if (volNo != curVolNo + 1 + (curVolIsOneBased ? 1 : 0))
                                            //    {
                                            //        throw new InvalidOperationException($"Vol mismatch Expected: {curVolNo} Actual: {volNo}  for {file}");
                                            //    }
                                            //}
                                            //if (curVolNo > 0 && volNo == 0)
                                            //{
                                            //}
                                            //else
                                            //{
                                            //}
                                        }
                                    }
                                    if (isContinuation)
                                    {
                                        curVolNo++;
                                        nContinuations++;
                                        // add to current
                                        if (curmetadata != null && curmetadata.IsDirty) // dirty: we're creating a new one
                                        {
                                            if (curmetadata.lstVolInfo.Count <= curVolNo)
                                            {
                                                var newvolInfo = new PdfVolumeInfo()
                                                {
                                                    FileNameVolume = Path.GetFileName(file),
                                                    NPagesInThisVolume = (int)(await GetPdfDocumentForFileAsync(file)).PageCount
                                                };
                                                if (newvolInfo.NPagesInThisVolume != 1)
                                                {
                                                    newvolInfo.Rotation = (int)Rotation.Rotate180;
                                                }
                                                else
                                                {
                                                    newvolInfo.Rotation = (int)Rotation.Rotate0;  // assume if single page, it's upside up
                                                }
                                                curmetadata.lstVolInfo.Add(newvolInfo);
                                            }
                                        }
                                        if (curmetadata != null && string.IsNullOrEmpty(curmetadata.lstVolInfo[curVolNo].FileNameVolume))
                                        {
                                            curmetadata.lstVolInfo[curVolNo].FileNameVolume = Path.GetFileName(file); // temptemp
                                        }
                                    }
                                    else
                                    {
                                        SaveMetaData(); // save prior metadata
                                        if (await TryAddFileAsync(file))
                                        {
                                            curVolNo = 0;
                                            curVolIsOneBased = false;
                                            cntItems++;
                                        }
                                    }
                                    lastFile = file;
                                }
                                if (cntItems > 0)
                                {
                                    if (rootMusicFolder.Length < curPath.Length)
                                    {
                                        var justdir = curPath.Substring(rootMusicFolder.Length + 1);
                                        lstFolders.Add(justdir);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            if (ex.Data.Contains("Filename"))
                            {
                                lastFile += " "+ ex.Data["Filename"];
                            }
                            PdfViewerWindow.s_pdfViewerWindow.OnException($"Exception reading files {curPath} near {lastFile}", ex);
                        }
                        SaveMetaData(); // last one in dir
                        foreach (var dir in Directory.EnumerateDirectories(curPath))
                        {
                            if (!dir.EndsWith("hidden", StringComparison.InvariantCultureIgnoreCase))
                            {
                                if (await recurDirsAsync(dir) > 0)
                                {
                                }
                            }
                        }
                        return cntItems;
                    }
                });
            }
            return (lstPdfMetaFileData, lstFolders);
        }

        internal static async Task<PdfMetaData> HandleSinglesFolderAsync(string curPath)
        {
            PdfMetaData curmetadata = null;
            var bmkFile = Path.ChangeExtension(curPath, "bmk");
            var sortedListSingles = new SortedSet<string>(); //tolower justfname
            var sortedListDeletedSingles = new SortedSet<string>();//tolower justfname
            if (File.Exists(bmkFile))
            {
                curmetadata = await PdfMetaData.ReadPdfMetaDataAsync(curPath, IsSingles: true);
                foreach (var vol in curmetadata.lstVolInfo)
                {
                    if (!File.Exists(Path.Combine(curPath, vol.FileNameVolume)))
                    {
                        sortedListDeletedSingles.Add(vol.FileNameVolume.ToLower());
                    }
                    else
                    {
                        sortedListSingles.Add(vol.FileNameVolume.ToLower());
                    }
                }
            }
            else
            {
                curmetadata = new PdfMetaData()
                {
                    _FullPathFile = curPath,
                    IsSinglesFolder = true,
                    IsDirty = true
                };
            }
            var lstNewFiles = new List<string>(); //fullpath
                                                  // find the new files that haven't been added yet. If we insert, then we need to renumber all subsequent entries to keep fav/ink/TOC data sync'd
            foreach (var single in Directory.EnumerateFiles(curPath, "*.pdf").OrderBy(f => f))
            {
                var JustFileName = Path.GetFileName(single).ToLower();
                if (!sortedListSingles.Contains(JustFileName))
                //                                    if (!curPdfFileData.lstVolInfo.Where(v => v.FileNameVolume.ToLower() == JustFileName.ToLower()).Any()) // optimize o(n^2)
                {
                    lstNewFiles.Add(single);
                    sortedListSingles.Add(JustFileName); // if it's already in the list, will return false and not add it. that's ok.
                }
            }
            if (lstNewFiles.Count > 0 || sortedListDeletedSingles.Count > 0) // there is least one new or deleted file. Need to recreate LstVol in alpha order, preserve TOC, adjust ink/fav/toc pagenos
            {// VolInfo has no user editable data. TOC has user info (user can edit TOC) so we need to preserve the edited content. Ink/Fav only need to adjust pagenos.
                curmetadata.IsDirty = true;
                // we deal with the fav/ink first by storing per vol, then rebasing pagenos
                var dictVolToLstFav = new Dictionary<string, List<Favorite>>(); // volname =>lstFav (a vol (which is a single song), can have multiple favs)
                var dictVolToLstInk = new Dictionary<string, List<InkStrokeClass>>();
                foreach (var fav in curmetadata.dictFav.Values)
                {
                    var volNo = curmetadata.GetVolNumFromPageNum(fav.Pageno);
                    var vol = curmetadata.lstVolInfo[volNo];
                    if (!dictVolToLstFav.TryGetValue(vol.FileNameVolume, out var lstFavPerVol))
                    {
                        lstFavPerVol = new List<Favorite>();
                        dictVolToLstFav[vol.FileNameVolume] = lstFavPerVol;
                    }
                    fav.Pageno -= curmetadata.GetPagenoOfVolume(volNo);
                    lstFavPerVol.Add(fav);
                }
                foreach (var ink in curmetadata.dictInkStrokes.Values)
                {
                    var volNo = curmetadata.GetVolNumFromPageNum(ink.Pageno);
                    var vol = curmetadata.lstVolInfo[volNo];
                    if (!dictVolToLstInk.TryGetValue(vol.FileNameVolume, out var lstInkPerVol))
                    {
                        lstInkPerVol = new List<InkStrokeClass>();
                        dictVolToLstInk[vol.FileNameVolume] = lstInkPerVol;
                    }
                    ink.Pageno -= curmetadata.GetPagenoOfVolume(volNo);
                    lstInkPerVol.Add(ink);
                }
                var sortedSetVolInfo = new SortedSet<PdfVolumeInfo>(curmetadata.lstVolInfo, new PdfVolumeInfoComparer());
                var sortedSetToc = new SortedSet<TOCEntry>(curmetadata.lstTocEntries, new TocEntryComparer());
                foreach (var delfile in sortedListDeletedSingles)
                {
                    var delvol = sortedSetVolInfo.Where(v => string.Compare(v.FileNameVolume, delfile, StringComparison.InvariantCultureIgnoreCase) == 0).FirstOrDefault();
                    if (delvol != null)
                    {
                        sortedSetVolInfo.Remove(delvol);
                    }
                    var delToc = sortedSetToc.Where(t => string.Compare(t.SongName, delfile, StringComparison.InvariantCultureIgnoreCase) == 0).FirstOrDefault();
                    if (delToc != null)
                    {
                        sortedSetToc.Remove(delToc);
                    }
                }
                foreach (var newfile in lstNewFiles)
                {
                    try
                    {
                        var newVolInfo = new PdfVolumeInfo()
                        {
                            FileNameVolume = Path.GetFileName(newfile),
                            NPagesInThisVolume = (int)(await GetPdfDocumentForFileAsync(newfile)).PageCount
                        };
                        sortedSetVolInfo.Add(newVolInfo);
                    }
                    catch (Exception ex)
                    {
                        ex.Data["Filename"] = newfile;
                        throw ex;

                    }
                }
                // update VolInfo.
                curmetadata.lstVolInfo = sortedSetVolInfo.ToList();

                // now fix TOC
                var singlesPageNo = 0;
                curmetadata.lstTocEntries.Clear();
                foreach (var vol in sortedSetVolInfo)
                {
                    var toc = sortedSetToc.Where(t => vol.FileNameVolume.ToLower().Contains(t.SongName.ToLower())).FirstOrDefault();
                    if (toc == null)
                    {
                        toc = new TOCEntry()
                        {
                            SongName = Path.GetFileNameWithoutExtension(vol.FileNameVolume),
                            PageNo = singlesPageNo
                        };
                    }
                    else
                    {
                        toc.PageNo = singlesPageNo;
                    }
                    sortedSetToc.Remove(toc);
                    curmetadata.lstTocEntries.Add(toc);
                    singlesPageNo += vol.NPagesInThisVolume;
                }
                // update toc
                curmetadata.InitializeDictToc(curmetadata.lstTocEntries);
                // now fix favorites.
                singlesPageNo = 0;
                curmetadata.Favorites.Clear();
                for (int volno = 0; volno < curmetadata.lstVolInfo.Count; volno++)
                {
                    var vol = curmetadata.lstVolInfo[volno];
                    if (dictVolToLstFav.TryGetValue(vol.FileNameVolume, out var lstFavPerVol))
                    {
                        foreach (var fav in lstFavPerVol)
                        {
                            fav.Pageno += singlesPageNo;
                            curmetadata.Favorites.Add(fav);
                        }
                    }
                    singlesPageNo += vol.NPagesInThisVolume;
                }
                curmetadata.InitializeFavList();
                // now handle ink
                singlesPageNo = 0;
                curmetadata.LstInkStrokes.Clear();
                for (int volno = 0; volno < curmetadata.lstVolInfo.Count; volno++)
                {
                    var vol = curmetadata.lstVolInfo[volno];
                    if (dictVolToLstInk.TryGetValue(vol.FileNameVolume, out var lstInkPerVol))
                    {
                        foreach (var ink in lstInkPerVol)
                        {
                            ink.Pageno += singlesPageNo;
                            curmetadata.LstInkStrokes.Add(ink);
                        }
                    }
                    singlesPageNo += vol.NPagesInThisVolume;
                }
                curmetadata.InitializeInkStrokes();
            }
            return curmetadata;
        }

        public string PdfBmkMetadataFileName => Path.ChangeExtension(_FullPathFile, "bmk");
        public static async Task<PdfMetaData> ReadPdfMetaDataAsync(string FullPathPdfFileOrSinglesFolder, bool IsSingles = false)
        {
            PdfMetaData pdfFileData = null;
            var bmkFile = Path.ChangeExtension(FullPathPdfFileOrSinglesFolder, "bmk");
            if (File.Exists(bmkFile))
            {
                try
                {
                    var serializer = new XmlSerializer(typeof(PdfMetaData));
                    var dtLastWriteTime = (new FileInfo(bmkFile)).LastWriteTime;
                    using (var sr = new StreamReader(bmkFile))
                    {
                        pdfFileData = (PdfMetaData)serializer.Deserialize(sr);
                        pdfFileData._FullPathFile = FullPathPdfFileOrSinglesFolder;
                        pdfFileData.dtLastWrite = dtLastWriteTime;
                        pdfFileData.initialLastPageNo = pdfFileData.LastPageNo;
                        pdfFileData.IsSinglesFolder = IsSingles;
                        if (pdfFileData.lstVolInfo.Count == 0) // There should be at least one for each PDF in a series. If no series, there should be 1 for itself.
                        {
                            var doc = await GetPdfDocumentForFileAsync(FullPathPdfFileOrSinglesFolder);
                            pdfFileData.lstVolInfo.Add(new PdfVolumeInfo()
                            {
                                FileNameVolume = Path.GetFileName(FullPathPdfFileOrSinglesFolder),
                                NPagesInThisVolume = (int)doc.PageCount,
                                Rotation = 0
                            });
                            pdfFileData.IsDirty = true;
                        }
                        if (string.IsNullOrEmpty(pdfFileData.lstVolInfo[0].FileNameVolume))
                        {
                            pdfFileData.lstVolInfo[0].FileNameVolume = Path.GetFileName(FullPathPdfFileOrSinglesFolder); //temptemp
                            pdfFileData.IsDirty = true;
                        }
                        if (pdfFileData.LastPageNo < pdfFileData.PageNumberOffset || pdfFileData.LastPageNo >= pdfFileData.MaxPageNum) // make sure lastpageno is in range
                        {
                            pdfFileData.LastPageNo = pdfFileData.PageNumberOffset; // go to first page
                        }
                    }
                }
                catch (Exception ex)
                {
                    PdfViewerWindow.s_pdfViewerWindow.OnException($"Reading {bmkFile}", ex);
                    // we don't want to delete the file because the user might have valuable bookmarks/favorites.
                    // let the user have an opportunity to fix it.
                }
            }
            else
            {
                pdfFileData = new PdfMetaData()
                {
                    _FullPathFile = FullPathPdfFileOrSinglesFolder,
                    IsSinglesFolder = IsSingles,
                    IsDirty = true
                };
                var doc = await GetPdfDocumentForFileAsync(FullPathPdfFileOrSinglesFolder);
                pdfFileData.lstVolInfo.Add(new PdfVolumeInfo()
                {
                    FileNameVolume = Path.GetFileName(FullPathPdfFileOrSinglesFolder),
                    NPagesInThisVolume = (int)doc.PageCount,
                    Rotation = 0
                });
            }
            pdfFileData.InitializeDictToc(pdfFileData.lstTocEntries);
            pdfFileData.InitializeFavList();
            pdfFileData.InitializeInkStrokes();
            return pdfFileData;
        }

        public void InitializeDictToc(List<TOCEntry> lstTocEntries)
        {
            this.lstTocEntries = lstTocEntries; //could be coming from MetadataImport
            dictToc.Clear();
            foreach (var toc in lstTocEntries) // a page can have multiple songs
            {
                toc.SongName = RemoveQuotes(toc.SongName);
                toc.Date = RemoveQuotes(toc.Date);
                toc.Composer = RemoveQuotes(toc.Composer);
                if (!dictToc.TryGetValue(toc.PageNo, out var tocLst))
                {
                    tocLst = new List<TOCEntry>();
                    dictToc[toc.PageNo] = tocLst;
                }
                tocLst.Add(toc);
            }
        }
        public void InitializeInkStrokes()
        {
            dictInkStrokes.Clear();
            foreach (var ink in LstInkStrokes)
            {
                dictInkStrokes[ink.Pageno] = ink;
            }
            LstInkStrokes.Clear();
        }

        public void InitializeFavList()
        {
            dictFav.Clear();
            foreach (var fav in Favorites)
            {
                dictFav[fav.Pageno] = fav;
            }
            Favorites.Clear(); // save memory
        }
        public void InitializeListPdfDocuments()
        {
            var pageNo = PageNumberOffset;
            for (int volNo = 0; volNo < lstVolInfo.Count; volNo++)
            {
                var task = new Task<PdfDocument>((pg) =>// can't be async
                {
                    var pathPdfFileVol = GetFullPathFileFromPageNo((int)pg);
                    var pdfDoc = GetPdfDocumentForFileAsync(pathPdfFileVol).GetAwaiter().GetResult();
                    //PdfDocument pdfDoc = null;
                    //pdfDoc = GetPdfDocumentAsync((int)pg).GetAwaiter().GetResult();
                    return pdfDoc;
                }, pageNo);
                pageNo += lstVolInfo[volNo].NPagesInThisVolume;
                lstVolInfo[volNo].TaskPdfDocument = task;
            }
        }

        //private static int GetNumPagesInPdf(string FullPathFile)
        //{
        //    var tkf = StorageFile.GetFileFromPathAsync(FullPathFile);
        //    tkf.AsTask().Wait();
        //    var tkpdfDoc = PdfDocument.LoadFromFileAsync(tkf.AsTask().Result).AsTask();
        //    tkpdfDoc.Wait();
        //    var pdfDoc = tkpdfDoc.Result;
        //    return (int)pdfDoc.PageCount;
        //}

        string RemoveQuotes(string str)
        {
            if (!string.IsNullOrEmpty(str))
            {
                if (str.StartsWith("\"") && str.EndsWith("\""))
                {
                    str = str.Replace("\"", string.Empty);
                }
            }
            return str;
        }

        public async Task<(PdfDocument pdfDoc, int pdfPgno)> GetPdfDocumentForPageno(int pageNo)
        {
            PdfDocument pdfDoc = null;
            var pdfPgNo = 0;
            if (pageNo < MaxPageNum)
            {
                var volno = GetVolNumFromPageNum(pageNo);
                var pdfDocTask = lstVolInfo[volno].TaskPdfDocument;
                if (pdfDocTask.Status == TaskStatus.Created)
                {
                    pdfDocTask.Start();
                }
                if (pdfDocTask.IsCompleted)
                {
                    pdfDoc = pdfDocTask.Result;
                }
                else
                {
                    pdfDoc = await pdfDocTask;
                }
                pdfPgNo = GetPdfVolPageNo(pageNo);
                int GetPdfVolPageNo(int Pgno)
                {
                    var res = Pgno - PageNumberOffset;
                    for (int i = 0; i < volno; i++)
                    {
                        res -= lstVolInfo[i].NPagesInThisVolume;
                    }
                    Debug.Assert(res >= 0, "page must be >=0");
                    return res;
                }
            }
            return (pdfDoc, pdfPgNo);
        }

        public static async Task<PdfDocument> GetPdfDocumentForFileAsync(string pathPdfFileVol)
        {
            //    StorageFile f = await StorageFile.GetFileFromPathAsync(pathPdfFileVol);
            //var pdfDoc = await PdfDocument.LoadFromFileAsync(f);
            using (var fstrm = await FileRandomAccessStream.OpenAsync(pathPdfFileVol, FileAccessMode.Read))
            {
                var pdfDoc = await PdfDocument.LoadFromStreamAsync(fstrm);
                //if (pdfDoc.IsPasswordProtected)
                //{
                //    //    this.dpPage.Children.Add(new TextBlock() { Text = $"Password Protected {pathPdfFileVol}" });
                //}
                return pdfDoc;
            }
        }

        public static async Task<PdfDocument> GetPdfDocumentForFileAsyncOrig(string pathPdfFileVol)
        {
            StorageFile f = await StorageFile.GetFileFromPathAsync(pathPdfFileVol);
            var pdfDoc = await PdfDocument.LoadFromFileAsync(f);
            if (pdfDoc.IsPasswordProtected)
            {
                //    this.dpPage.Children.Add(new TextBlock() { Text = $"Password Protected {pathPdfFileVol}" });
            }
            return pdfDoc;
        }

        public void SaveIfDirty(bool ForceDirty = false)
        {
            InitializeListPdfDocuments(); // reinit list to clear out results to save mem
            if (!PdfViewerWindow.s_pdfViewerWindow.IsTesting)
            {
                if (IsDirty || ForceDirty || initialLastPageNo != LastPageNo)
                {
                    try
                    {
                        if (_FullPathFile.Contains("Everybody"))
                        {
                            "adf".ToString();
                        }
                        var bmkFile = PdfBmkMetadataFileName;
                        if (File.Exists(bmkFile))
                        {
                            var dt = (new FileInfo(bmkFile)).LastWriteTime;
                            if (dt != dtLastWrite)
                            {
                                if (MessageBox.Show(
                                    $"{bmkFile} \nOriginal {dtLastWrite} \nCurrent {dt}",
                                    $"File already exists. Replace original?",
                                    MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                                {
                                    return;
                                }
                            }
                            File.Delete(bmkFile);
                        }
                        var serializer = new XmlSerializer(typeof(PdfMetaData));
                        var settings = new XmlWriterSettings()
                        {
                            Indent = true,
                            IndentChars = " "
                        };
                        Favorites = dictFav.Values.ToList();
                        if (dictInkStrokes.Count > 0)
                        {
                            LstInkStrokes = dictInkStrokes.Values.ToList();
                        }
                        using (var strm = File.Create(bmkFile))
                        {
                            using (var w = XmlWriter.Create(strm, settings))
                            {
                                serializer.Serialize(w, this);
                            }
                        }
                        var newdt = (new FileInfo(bmkFile)).LastWriteTime;
                        dtLastWrite = newdt;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Exception saving file " + ex.ToString());
                    }
                }
            }
        }

        /// <summary>
        /// Total page count across volumes
        /// </summary>
        /// <returns></returns>
        public int GetTotalPageCount()
        {
            return lstVolInfo.Sum(p => p.NPagesInThisVolume);
        }

        /// <summary>
        /// Total song count across volumes
        /// </summary>
        /// <returns></returns>
        public int GetSongCount()
        {
            return lstTocEntries.Count;
        }
        //public BitmapImage GetBitmapImageThumbnail()
        //{
        //    var bmi = bitmapImageCache;
        //    if (bmi == null)
        //    {
        //        bmi = this.bitmapImageCache;
        //    }
        //    return bmi;
        //}

        internal async Task<BitmapImage> CalculateBitMapImageForPageAsync(int PageNo, CancellationTokenSource cts, Size? SizeDesired)
        {
            BitmapImage bmi = null;
            cts?.Token.Register(() =>
            {
                PageNo.ToString();
                //Debug.WriteLine($"Cancel {PageNo}");
            });
            cts?.Token.ThrowIfCancellationRequested();
            var (pdfDoc, pdfPgno) = await GetPdfDocumentForPageno(PageNo);
            if (pdfDoc != null && pdfPgno >= 0 && pdfPgno < pdfDoc.PageCount)
            {
                using (var pdfPage = pdfDoc.GetPage((uint)(pdfPgno)))
                {
                    var rect = pdfPage.Dimensions.ArtBox;
                    var renderOpts = new PdfPageRenderOptions();
                    if (SizeDesired.HasValue)
                    {
                        renderOpts.DestinationWidth = (uint)SizeDesired.Value.Width;
                        renderOpts.DestinationHeight = (uint)SizeDesired.Value.Height;
                    }
                    else
                    {
                        renderOpts.DestinationWidth = (uint)rect.Width;
                        renderOpts.DestinationHeight = (uint)rect.Height;
                    }
                    if (pdfPage.Rotation != PdfPageRotation.Normal)
                    {
                        renderOpts.DestinationWidth = (uint)rect.Height;
                        renderOpts.DestinationHeight = (uint)rect.Width;
                    }
                    //                    renderOpts.BackgroundColor = Windows.UI.Color.FromArgb(0xf, 0, 0xff, 0);
                    bmi = await GetBitMapImageFromPdfPage(pdfPage, GetRotation(PageNo), renderOpts, cts);
                }
            }
            if (bmi == null)
            {
                throw new InvalidDataException($"No bitmapimage for Pg={PageNo} PdfPg={pdfPgno} PdfPgCnt={pdfDoc?.PageCount} {this} ");
            }
            return bmi;
        }

        private async Task<BitmapImage> GetBitMapImageFromPdfPage(PdfPage pdfPage, Rotation rotation, PdfPageRenderOptions renderOpts, CancellationTokenSource cts)
        {
            var bmi = new BitmapImage();
            using (var strm = new InMemoryRandomAccessStream())
            {
                await pdfPage.RenderToStreamAsync(strm, renderOpts);
                cts?.Token.ThrowIfCancellationRequested();
                //                bmi.CreateOptions = BitmapCreateOptions.DelayCreation;
                bmi.BeginInit();
                bmi.StreamSource = strm.AsStream();
                bmi.Rotation = rotation;
                bmi.CacheOption = BitmapCacheOption.OnLoad;
                bmi.EndInit();
                //                        bmi.Freeze();
                //                        bmi.StreamSource = null;
            }
            return bmi;
        }

        /// <summary>
        /// gets a thumbnail for the 1st page of a sequence of PDFs.
        /// </summary>
        /// <param name="size"></param>
        /// <returns></returns>
        public async Task<BitmapImage> GetBitmapImageThumbnailAsync()
        {
            var bmi = bitmapImageCache; // first see if we have one 
            if (bmi == null)
            {
                var pdfDoc = await GetPdfDocumentForFileAsync(GetFullPathFileFromVolno(volNo: 0));
                using (var pdfPage = pdfDoc.GetPage(0))
                {
                    var rect = pdfPage.Dimensions.ArtBox;
                    var renderOpts = new PdfPageRenderOptions
                    {
                        DestinationWidth = (uint)150, // match these with choose.xaml
                        DestinationHeight = (uint)225
                    };
                    bmi = await GetBitMapImageFromPdfPage(pdfPage, GetRotation(PageNumberOffset), renderOpts, cts: null);
                }
                bitmapImageCache = bmi;
                if (PdfViewerWindow.s_pdfViewerWindow.currentPdfMetaData?._FullPathFile == _FullPathFile)
                {
                    if (PdfViewerWindow.s_pdfViewerWindow.ImgThumbImage != null)
                    {
                        PdfViewerWindow.s_pdfViewerWindow.OnMyPropertyChanged(nameof(PdfViewerWindow.ImgThumbImage));
                    }
                }
            }
            return bmi;
        }

        internal bool IsFavorite(int PageNo)
        {
            var isFav = false;
            if (dictFav.ContainsKey(PageNo))
            {
                isFav = true;
            }
            return isFav;
        }

        internal void ToggleFavorite(int PageNo, bool IsFavorite, string FavoriteName = null)
        {
            this.IsDirty = true;
            if (IsFavorite)
            {
                if (!dictFav.ContainsKey(PageNo))
                {
                    var fav = new Favorite()
                    {
                        Pageno = PageNo
                    };
                    if (!string.IsNullOrEmpty(FavoriteName))
                    {
                        fav.FavoriteName = FavoriteName;
                    }
                    dictFav[PageNo] = fav;
                    IsDirty = true;
                }
            }
            else
            {
                if (dictFav.ContainsKey(PageNo))
                {
                    dictFav.Remove(PageNo);
                    IsDirty = true;
                }
            }
        }

        internal Rotation GetRotation(int pgNo)
        {
            var vol = GetVolNumFromPageNum(pgNo);
            return (Rotation)lstVolInfo[vol].Rotation;
        }

        internal void Rotate(int pgNo)
        {
            var vol = GetVolNumFromPageNum(pgNo);
            this.IsDirty = true;
            lstVolInfo[vol].Rotation = (lstVolInfo[vol].Rotation + 1) % 4;
        }
        public override string ToString()
        {
            return $"{Path.GetFileNameWithoutExtension(_FullPathFile)} Vol={lstVolInfo.Count} Toc={lstTocEntries.Count} Fav={dictFav.Count}";
        }
    }

    /// <summary>
    /// comparer used for singles 
    /// </summary>
    public class PdfVolumeInfoComparer : IComparer<PdfVolumeInfo>
    {
        public int Compare(PdfVolumeInfo x, PdfVolumeInfo y)
        {
            return string.Compare(x.FileNameVolume, y.FileNameVolume);
        }
    }
    public class TocEntryComparer : IComparer<TOCEntry>
    {
        public int Compare(TOCEntry x, TOCEntry y)
        {
            return string.Compare(x.SongName, y.SongName);
        }
    }

    public class PageNoBaseClassComparer : IComparer<PageNoBaseClass>
    {
        public int Compare(PageNoBaseClass x, PageNoBaseClass y)
        {
            return x.Pageno == y.Pageno ? 0 : (x.Pageno < y.Pageno ? -1 : 1);
        }
    }

    [Serializable]
    public class PdfVolumeInfo
    {
        /// <summary>
        /// The num PDF pages in this PDF file
        /// </summary>
        [XmlElement("NPages")]
        public int NPagesInThisVolume;
        /*Normal = 0,Rotate90 = 1,Rotate180 = 2,Rotate270 = 3*/
        public int Rotation;
        [XmlIgnore]
        public Task<PdfDocument> TaskPdfDocument;

        [XmlElement("FileName")]
        /// <summary>
        /// the JustFilename (with extension) Can't be rel to rootfolder: user could change rootfolder to folder inside, so must be rel to fullpath: needs to be portable from machine to machine) path filename for the PDF document
        /// </summary>
        public string FileNameVolume;

        public override string ToString()
        {
            return $"{FileNameVolume} #Pgs={NPagesInThisVolume,4} Rotation={(Rotation)Rotation}";
        }
    }

    public class PageNoBaseClass
    {
        public int Pageno { get; set; }
    }

    [Serializable]
    public class Favorite : PageNoBaseClass //: ICloneable
    {
        public string FavoriteName { get; set; }
        [XmlIgnore]
        public object Tag;

        //public object Clone()
        //{
        //    return new Favorite()
        //    {
        //        FavoriteName = this.FavoriteName,
        //        Pageno = this.Pageno
        //    };
        //}

        public override string ToString()
        {
            return $"{FavoriteName} {Pageno}".Trim();
        }
    }

    [Serializable]
    public class InkStrokeClass : PageNoBaseClass
    {
        public Point InkStrokeDimension;
        public byte[] StrokeData { get; set; }
    }

    /// <summary>
    /// Not really bookmark: Table of Contents Entry
    /// </summary>
    [Serializable]
    public class TOCEntry : ICloneable
    {
        public string SongName { get; set; }
        public string Composer { get; set; }
        public string Notes { get; set; }
        public string Date { get; set; }
        public int PageNo { get; set; }
        [XmlIgnore]
        public object Tag;

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
}