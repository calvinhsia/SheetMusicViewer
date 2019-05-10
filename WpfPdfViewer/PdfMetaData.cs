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

        [XmlIgnore]
        public int MaxPageNum => PageNumberOffset + NumPagesInSet;

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
        /// <summary>
        /// Hide it so it doesn't show anywhere in the UI. Could be a dupe for testing
        /// </summary>
        public bool HideThisPDFFile;

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
                foreach (var toce in lstTocs)
                {
                    var val = $"{toce.SongName} {toce.Composer} {toce.Date} {toce.Notes}".Trim();
                    str += val + " ";
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
            var retval = _FullPathFile;
            string FormatVolno(int v)
            {
                string str;
                if (lstVolInfo.Count < 10)
                {
                    str = $"{v}";
                }
                else
                {
                    var d = v / 10;
                    str = $"{d}{v - d * 10}";
                }
                return str;
            }
            if (retval.EndsWith("0.pdf"))
            {
                if (retval.EndsWith("00.pdf"))
                {
                    retval = retval.Replace("00.pdf", string.Empty) + FormatVolno(volNo) + ".pdf";
                }
                else
                {
                    retval = retval.Replace("0.pdf", string.Empty) + FormatVolno(volNo) + ".pdf";
                }
            }
            else if (_FullPathFile.EndsWith("1.pdf"))
            {
                // no page 0, so 1 based
                if (retval.EndsWith("01.pdf"))
                {
                    retval = retval.Replace("01.pdf", string.Empty) + FormatVolno(volNo + 1) + ".pdf";
                }
                else
                {
                    retval = retval.Replace("1.pdf", string.Empty) + FormatVolno(volNo + 1) + ".pdf";
                }
            }
            else if (this.lstVolInfo.Count > 0) //there's more than 1 entry
            {
                if (volNo == 0)
                {
                }
                else
                {
                    retval = retval.Replace(".pdf", string.Empty) + FormatVolno(volNo) + ".pdf";
                }
            }
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
                    while (retval.EndsWith("0") || retval.EndsWith("1"))
                    {
                        retval = retval.Substring(0, retval.Length - 2);
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

        private int GetVolNumFromPageNum(int pageNo)
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
                    PdfMetaData curPdfFileData = null;
                    int nContinuations = 0;
                    await recurDirsAsync(pathCurrentMusicFolder);
                    async Task<bool> TryAddFileAsync(string curFullPathFile)
                    {
                        var retval = false;
                        try
                        {
                            curPdfFileData = await PdfMetaData.ReadPdfMetaDataAsync(curFullPathFile);
                            if (curPdfFileData != null)
                            {
                                lstPdfMetaFileData.Add(curPdfFileData);
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
                        if (curPdfFileData != null)
                        {
                            // for single songs: if no TOC and # pages < 7, create a new TOC entry with 1 song: same as file name
                            if (curPdfFileData.lstTocEntries.Count == 0 && curPdfFileData.NumPagesInSet < 7)
                            {
                                curPdfFileData.lstTocEntries.Add(new TOCEntry()
                                {
                                    SongName = Path.GetFileNameWithoutExtension(curPdfFileData._FullPathFile)
                                });
                                curPdfFileData.IsDirty = true;
                            }
                        }
                        if (curPdfFileData?.IsDirty == true)
                        {
                            if (curPdfFileData.lstVolInfo.Count != nContinuations + 1) // +1 for root
                            {
                                "adf".ToString();
                            }
                            SavePdfMetaFileData(curPdfFileData);
                        }
                        nContinuations = 0;
                    }
                    async Task<int> recurDirsAsync(string curPath)
                    {
                        var lastFile = string.Empty;
                        var pgOffset = 0;
                        nContinuations = 0;
                        var cntItems = 0;
                        int curVolNo = 0; // 
                        try
                        {
                            foreach (var file in Directory.EnumerateFiles(curPath, "*.pdf").OrderBy(f => f.ToLower()))//.Where(f=>f.Contains("Miser"))) // "file" is fullpath
                            {
                                if (file.Contains("Scott Joplin C"))
                                {
                                    "".ToString();
                                }
                                var isContinuation = false;
                                if (!string.IsNullOrEmpty(lastFile) &&
                                        curPdfFileData != null &&
                                        System.IO.Path.GetDirectoryName(lastFile) == System.IO.Path.GetDirectoryName(file)) // same dir
                                {
                                    var justFnameVol0 = System.IO.Path.GetFileNameWithoutExtension(curPdfFileData._FullPathFile).Trim().ToLower();
                                    var lastcharVol0 = justFnameVol0.Last();
                                    if ("01".Contains(lastcharVol0)) // if the last char is 0 or 1
                                    {
                                        if (lastcharVol0 == '1' && curVolNo == 0)
                                        {
                                            curVolNo++;
                                        }
                                        justFnameVol0 = justFnameVol0.Substring(0, justFnameVol0.Length - 1);
                                    }
                                    var justfnameCurrent = System.IO.Path.GetFileNameWithoutExtension(file).Trim().ToLower();
                                    if (justFnameVol0.Length < justfnameCurrent.Length &&
                                        justFnameVol0 == justfnameCurrent.Substring(0, justFnameVol0.Length))
                                    {
                                        if (int.TryParse(justfnameCurrent.Substring(justFnameVol0.Length), out var volNo))
                                        {
                                            if (volNo != curVolNo + 1)
                                            {
                                                throw new InvalidOperationException($"Vol mismatch Expected: {curVolNo} Actual: {volNo}  for {file}");
                                            }
                                        }
                                        isContinuation = true;
                                    }
                                }
                                if (isContinuation)
                                {
                                    curVolNo++;
                                    nContinuations++;
                                    // add to current
                                    if (curPdfFileData != null && curPdfFileData.IsDirty) // dirty: we're creating a new one
                                    {
                                        var newvolInfo = new PdfVolumeInfo()
                                        {
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
                                        curPdfFileData.lstVolInfo.Add(newvolInfo);
                                    }
                                }
                                else
                                {
                                    SaveMetaData(); // save prior metadata
                                    if (await TryAddFileAsync(file))
                                    {
                                        curVolNo = 0;
                                        cntItems++;
                                    }
                                    if (curPdfFileData != null)
                                    {
                                        pgOffset = curPdfFileData.lstVolInfo[0].NPagesInThisVolume;
                                    }
                                }
                                lastFile = file;
                            }

                        }
                        catch (Exception ex)
                        {
                            PdfViewerWindow.s_pdfViewerWindow.OnException($"Exception reading files ", ex);
                        }
                        SaveMetaData(); // last one in dir
                        foreach (var dir in Directory.EnumerateDirectories(curPath))
                        {
                            if (await recurDirsAsync(dir) > 0)
                            {
                                var justdir = dir.Substring(rootMusicFolder.Length + 1);
                                lstFolders.Add(justdir);
                            }
                        }
                        return cntItems;
                    }
                });
            }
            return (lstPdfMetaFileData, lstFolders);
        }
        public string PdfMetadataFileName => Path.ChangeExtension(_FullPathFile,"bmk");
        public static async Task<PdfMetaData> ReadPdfMetaDataAsync(string FullPathPdfFile)
        {
            PdfMetaData pdfFileData = null;
            var bmkFile = Path.ChangeExtension(FullPathPdfFile, "bmk");
            if (File.Exists(bmkFile))
            {
                try
                {
                    var serializer = new XmlSerializer(typeof(PdfMetaData));
                    using (var sr = new StreamReader(bmkFile))
                    {
                        pdfFileData = (PdfMetaData)serializer.Deserialize(sr);
                        pdfFileData._FullPathFile = FullPathPdfFile;
                        pdfFileData.initialLastPageNo = pdfFileData.LastPageNo;
                        if (pdfFileData.HideThisPDFFile)
                        {
                            pdfFileData = null;
                        }
                        else
                        {
                            if (pdfFileData.lstVolInfo.Count == 0) // There should be at least one for each PDF in a series. If no series, there should be 1 for itself.
                            {
                                var doc = await GetPdfDocumentForFileAsync(FullPathPdfFile);
                                pdfFileData.lstVolInfo.Add(new PdfVolumeInfo()
                                {
                                    NPagesInThisVolume = (int)doc.PageCount,
                                    Rotation = 0
                                });
                                pdfFileData.IsDirty = true;
                            }
                            if (pdfFileData.LastPageNo < pdfFileData.PageNumberOffset || pdfFileData.LastPageNo >= pdfFileData.MaxPageNum) // make sure lastpageno is in range
                            {
                                pdfFileData.LastPageNo = pdfFileData.PageNumberOffset; // go to first page
                            }
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
                    _FullPathFile = FullPathPdfFile,
                    IsDirty = true
                };
                var doc = await GetPdfDocumentForFileAsync(FullPathPdfFile);
                pdfFileData.lstVolInfo.Add(new PdfVolumeInfo()
                {
                    NPagesInThisVolume = (int)doc.PageCount,
                    Rotation = 0
                });
            }
            pdfFileData?.InitializeDictToc(pdfFileData?.lstTocEntries);
            pdfFileData?.InitializeFavList();
            pdfFileData?.InitializeInkStrokes();
            return pdfFileData;
        }

        public void InitializeDictToc(List<TOCEntry> lstTocEntries)
        {
            this.lstTocEntries = lstTocEntries;
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
                dictInkStrokes[ink.PageNo] = ink;
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
            StorageFile f = await StorageFile.GetFileFromPathAsync(pathPdfFileVol);
            var pdfDoc = await PdfDocument.LoadFromFileAsync(f);
            if (pdfDoc.IsPasswordProtected)
            {
                //    this.dpPage.Children.Add(new TextBlock() { Text = $"Password Protected {pathPdfFileVol}" });
            }
            return pdfDoc;
        }

        public static void SavePdfMetaFileData(PdfMetaData pdfFileData, bool ForceSave = false)
        {
            pdfFileData.InitializeListPdfDocuments(); // reinit list to clear out results to save mem
            if (!PdfViewerWindow.s_pdfViewerWindow.IsTesting)
            {
                if (pdfFileData.IsDirty || ForceSave || pdfFileData.initialLastPageNo != pdfFileData.LastPageNo)
                {
                    try
                    {
                        var serializer = new XmlSerializer(typeof(PdfMetaData));
                        var settings = new XmlWriterSettings()
                        {
                            Indent = true,
                            IndentChars = " "
                        };
                        pdfFileData.Favorites = pdfFileData.dictFav.Values.ToList();
                        if (pdfFileData.dictInkStrokes.Count > 0)
                        {
                            pdfFileData.LstInkStrokes = pdfFileData.dictInkStrokes.Values.ToList();
                        }
                        var bmkFile = Path.ChangeExtension(pdfFileData._FullPathFile, "bmk");
                        if (File.Exists(bmkFile))
                        {
                            File.Delete(bmkFile);
                        }
                        using (var strm = File.Create(bmkFile))
                        {
                            using (var w = XmlWriter.Create(strm, settings))
                            {
                                serializer.Serialize(w, pdfFileData);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Exception saving file " + ex.ToString());
                    }
                }
            }
        }

        internal int GetVolumeFromPageNo(int pageNo)
        {
            var volno = 0;
            var sumpg = 0;
            foreach (var vol in lstVolInfo)
            {
                if (pageNo < vol.NPagesInThisVolume)
                {
                    break;
                }
                sumpg += vol.NPagesInThisVolume;
                volno++;
            }
            return volno;
        }

        /// <summary>
        /// Total page count across volume
        /// </summary>
        /// <returns></returns>
        public int GetTotalPageCount()
        {
            return lstVolInfo.Sum(p => p.NPagesInThisVolume);
        }

        /// <summary>
        /// Total song count across volume
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
                        renderOpts.DestinationHeight = (uint)rect.Width;
                        renderOpts.DestinationWidth = (uint)rect.Height;
                    }
                    bmi = await GetBitMapImageFromPdfPage(pdfPage, GetRotation(PageNo), renderOpts, cts);
                }
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

                    //using (var strm = new InMemoryRandomAccessStream())
                    //{

                    //    await pdfPage.RenderToStreamAsync(strm, renderOpts);
                    //    //var enc = new PngBitmapEncoder();
                    //    //enc.Frames.Add(BitmapFrame.Create)
                    //    bmi.BeginInit();
                    //    bmi.StreamSource = strm.AsStream();
                    //    bmi.CacheOption = BitmapCacheOption.OnLoad;
                    //    bmi.Rotation = (Rotation)GetRotation(pgNo: PageNumberOffset);
                    //    bmi.EndInit();
                    //}
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

        internal void ToggleFavorite(int PageNo, bool IsFavorite)
        {
            this.IsDirty = true;
            if (IsFavorite)
            {
                if (!dictFav.ContainsKey(PageNo))
                {
                    dictFav[PageNo] = new Favorite() { Pageno = PageNo };
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

        //public int GetPdfVolPageNo(int Pgno)
        //{
        //    var res = Pgno;
        //    var volno = GetVolNumFromPageNum(Pgno);
        //    for (int i = 0; i < volno; i++)
        //    {
        //        res -= lstVolInfo[i].NPagesInThisVolume;
        //    }
        //    Debug.Assert(res >= 0, "page must be >=0");
        //    return res;
        //}


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

        public override string ToString()
        {
            return $"#Pgs={NPagesInThisVolume,4} Rotation={(Rotation)Rotation}";
        }
    }

    [Serializable]
    public class Favorite : ICloneable
    {
        public string FavoriteName { get; set; }
        public int Pageno { get; set; }
        [XmlIgnore]
        public object Tag;

        public object Clone()
        {
            return new Favorite()
            {
                FavoriteName = this.FavoriteName,
                Pageno = this.Pageno
            };
        }

        public override string ToString()
        {
            return $"{FavoriteName} {Pageno}".Trim();
        }
    }

    [Serializable]
    public class InkStrokeClass
    {
        public int PageNo { get; set; }

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