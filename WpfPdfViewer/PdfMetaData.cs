using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Serialization;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace WpfPdfViewer
{
    ///// <summary>
    ///// Represents a currently open set of PDFs (all of the same title)
    ///// A set consists of multiple PDFs, all with names ending in "0.pdf", "1.pdf", etc.
    ///// </summary>
    //public class PdfOpenStatus
    //{
    //    PdfMetaData pdfMetaDataRoot;
    //    int volumeNo;
    //}

    /// <summary>
    /// The serialized info for a PDF is in a file with the exact same name as the PDF with the extension changed to ".bmk"
    /// Some PDFs are a series of scanned docs, numbered, e.g. 0,1,2,3...
    /// Some of these members will be stored in the "root" volume, while others will be per each file.
    /// only those members indicated, are stored in the "root"
    /// When reading the data in, any fav, toc in non-root will be moved to root.
    /// </summary>
    [Serializable]
    public class PdfMetaData
    {
        private string _FullPathRootFile;
        public string GetFullPathFile(int volNo, bool MakeRelative = false)
        {
            var retval = _FullPathRootFile;
            if (_FullPathRootFile.EndsWith("0.pdf"))
            {
                retval = retval.Replace("0.pdf", string.Empty) + volNo.ToString() + ".pdf";
            }
            if (MakeRelative)
            {
                retval = retval?.Substring(PdfViewerWindow.s_pdfViewerWindow._RootMusicFolder.Length + 1);
            }
            return retval;
        }
        public string GetFullPathFileFromPageNo(int pageNo)
        {
            var retval = _FullPathRootFile;
            if (_FullPathRootFile.EndsWith("0.pdf"))
            {
                var volNo = GetVolNumFromPageNum(pageNo);
                retval = retval.Replace("0.pdf", string.Empty) + volNo.ToString() + ".pdf";
            }
            else if (_FullPathRootFile.EndsWith("1.pdf"))
            {
                var volNo = GetVolNumFromPageNum(pageNo) + 1;
                retval = retval.Replace("1.pdf", string.Empty) + volNo.ToString() + ".pdf";
            }
            return retval;
        }

        private int GetVolNumFromPageNum(int pageNo)
        {
            var volno = 0;
            var pSum = 0;
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

        /// <summary>
        /// num pages in all volumes of set
        /// </summary>
        [XmlIgnore]
        public int NumPagesInSet => lstVolInfo.Sum(p => p.NPagesInThisVolume);

        public List<PdfVolumeInfo> lstVolInfo = new List<PdfVolumeInfo>();
        /// <summary>
        /// the page no when this PDF was last opened
        /// stored in root
        /// </summary>
        public int LastPageNo;

        internal bool IsDirty = false;
        int initialLastPageNo;

        /// <summary>
        /// The Table of contents of a songbook shows the scanned page numbers, which may not match the actual PDF page numbers (there could be a cover page scanned
        /// or could be a multivolume set)
        /// We want to keep the scanned ORC TOC editing, cleanup, true and minimize required editing, so keep the original page numbers.
        /// The scanned imported OCR TOC will show the physical page no, but not the actual PDF page no.
        /// This value will map between the 2 so that the imported scanned TOC saved as XML will not need to be adjusted.
        /// For 1st, 2nd, 3rd volumes, the offset from the actual scanned page number (as visible on the page) to the PDF page number
        /// e.g. the very 1st volume might have a cover page, which is page 0. Viewing song Foobar might show page 44, but it's really PdfPage=45, 
        /// so we set PageNumberOffset to 1
        /// For vol 4 (PriorPdfMetaData != null), the 1st song "Please Mr Postman" might show page 403, but it's really PdfPage 0. So PageNumberOffset = 403
        /// So the XML for the song will say 403 (same as scanned TOC), but the actual PDFpage no in vol 4 = (403 - PageNumberOffset == 0)
        /// The next song "Poor Side Of Town" on page 404 ins on PdfPage 1. Toc = 404. diff == PageNumberOffset== 403
        /// 
        /// So there are 2 kinds of page numbers PDF PgNo and TOC PgNo, and PdfPgNo = TOCPgNo + PageNumberOffset
        /// So what do we show in the Toolbar? probably best to show TOC pgNo. It matches the page numbers in the scan and is familiar to user.
        /// However, that means the displayed Toolbar pageno couuld be negative: the 1st song might be on PDF page 1, with lots of intro material on prior pages.
        /// The PDF page numbers are always 0->maxno -1. For a continued volume, max = Sum(max of all volumes)
        /// so, the pgnos for all TOC entries and the dictToc are of type TocPgno.
        /// </summary>
        public int PageNumberOffset;
        /// <summary>
        /// Hide it so it doesn't show anywhere in the UI. Could be a dupe for testing
        /// </summary>
        public bool HideThisPDFFile;

        public string Notes;

        public List<Favorite> Favorites = new List<Favorite>();

        public List<TOCEntry> lstTocEntries = new List<TOCEntry>();

        /// <summary>
        /// the current volume being displayed
        /// </summary>
        [XmlIgnore]
        public int VolumeCurrent;

        [XmlIgnore]
        public List<Task<PdfDocument>> lstPdfDocuments = new List<Task<PdfDocument>>();

        [XmlIgnore] // can't serialize dictionaries
        public Dictionary<int, List<TOCEntry>> dictToc = new Dictionary<int, List<TOCEntry>>();

        [XmlIgnore]
        internal BitmapImage bitmapImageCache;
        internal string GetDescription(int currentPageNumber)
        {
            var str = string.Empty;
            var pMetaDataItem = this;

            var tocPgNm = currentPageNumber + PageNumberOffset;
            if (pMetaDataItem.dictToc.TryGetValue(tocPgNm, out var lstTocs))
            {
                foreach (var toce in lstTocs)
                {
                    str += toce + " ";
                }
            }
            else
            {
                str = $"{tocPgNm} {this}";
            }
            return str.Trim();
        }

        internal static async Task<List<PdfMetaData>> LoadAllPdfMetaDataFromDiskAsync(string _RootMusicFolder)
        {
            var lstPdfMetaFileData = new List<PdfMetaData>();
            if (!string.IsNullOrEmpty(_RootMusicFolder) && Directory.Exists(_RootMusicFolder))
            {
                var pathCurrentMusicFolder = _RootMusicFolder;
                lstPdfMetaFileData.Clear();
                await Task.Run(() =>
                {
                    PdfMetaData curPdfFileData = null;
                    recurDirs(pathCurrentMusicFolder);
                    bool TryAddFile(string curFullPathFile)
                    {
                        if (curFullPathFile.Contains("James Sco"))
                        {
                            "asdf".ToString();
                        }
                        curPdfFileData = PdfMetaData.ReadPdfMetaData(curFullPathFile);
                        if (curPdfFileData != null)
                        {
                            //curPdfFileData.lstVolInfo.Clear();
                            //curPdfFileData.lstVolInfo.Add(new PdfVolumeInfo()
                            //{
                            //    NPagesInThisVolume = getNumPages(curFullPathFile),
                            //    Rotation = (int)Rotation.Rotate180
                            //});
                            lstPdfMetaFileData.Add(curPdfFileData);
                        }
                        return true;
                    }
                    void recurDirs(string curPath)
                    {
                        var lastFile = string.Empty;
                        var pgOffset = 0;
                        foreach (var file in Directory.EnumerateFiles(curPath, "*.pdf").OrderBy(f => f))//.Where(f=>f.Contains("Miser"))) // "file" is fullpath
                        {
                            var isContinuation = false;
                            if (!string.IsNullOrEmpty(lastFile) &&
                                System.IO.Path.GetDirectoryName(lastFile) == System.IO.Path.GetDirectoryName(file))
                            {
                                // if the prior added file and this file differ by ony a single char, treat as continuation. E.g. file1.pdf, file2.pdf
                                var justFnamelast = System.IO.Path.GetFileNameWithoutExtension(lastFile).Trim();
                                var justfnameCurrent = System.IO.Path.GetFileNameWithoutExtension(file).Trim();
                                if (justFnamelast.Length == justfnameCurrent.Length) // file1, file2
                                {
                                    if (justFnamelast.Substring(0, justFnamelast.Length - 1) ==
                                       justfnameCurrent.Substring(0, justfnameCurrent.Length - 1))
                                    {
                                        isContinuation = true;
                                    }
                                }
                                else
                                {  // file, file2
                                    if (justFnamelast == justfnameCurrent.Substring(0, justfnameCurrent.Length - 1))
                                    {
                                        isContinuation = true;
                                    }
                                }
                            }
                            if (isContinuation)
                            {
                                //// add to current
                                //if (curPdfFileData != null)
                                //{
                                //    curPdfFileData.lstVolInfo.Add(new PdfVolumeInfo()
                                //    {
                                //        NPagesInThisVolume = getNumPages(file),
                                //        Rotation = (int)Rotation.Rotate180
                                //    });
                                //}
                                //var bmkToDel = Path.ChangeExtension(file, "bmk");
                                //if (File.Exists(bmkToDel))
                                //{
                                //    //var subVolMetaData = ReadPdfMetaData(file);
                                //    //if (subVolMetaData.lstTocEntries.Count > 0)
                                //    //{
                                //    //    "hadtoc?".ToString();
                                //    //    curPdfFileData.lstTocEntries.AddRange(subVolMetaData.lstTocEntries);
                                //    //    subVolMetaData.lstTocEntries.Clear();
                                //    //}
                                //    //if (subVolMetaData.Favorites.Count > 0)
                                //    //{
                                //    //    "had fav?".ToString();
                                //    //    foreach (var fav in subVolMetaData.Favorites)
                                //    //    {
                                //    //        curPdfFileData.Favorites.Add(new Favorite()
                                //    //        {
                                //    //            Pageno = fav.Pageno + pgOffset
                                //    //        });
                                //    //    }
                                //    //    subVolMetaData.Favorites.Clear();
                                //    //}
                                //    File.Delete(bmkToDel);
                                //}
                            }
                            else
                            {
                                //if (curPdfFileData != null)
                                //{
                                //    PdfMetaData.SavePdfFileData(curPdfFileData, ForceSave: true);
                                //    curPdfFileData = null;
                                //}
                                TryAddFile(file);
                                if (curPdfFileData != null)
                                {
                                    pgOffset = curPdfFileData.lstVolInfo[0].NPagesInThisVolume;
                                }
                            }
                            lastFile = file;
                        }
                        //if (curPdfFileData != null)
                        //{
                        //    PdfMetaData.SavePdfFileData(curPdfFileData, ForceSave: true);
                        //}
                        foreach (var dir in Directory.EnumerateDirectories(curPath))
                        {
                            recurDirs(dir);
                        }
                    }
                });
            }
            return lstPdfMetaFileData;
        }

        public static PdfMetaData ReadPdfMetaData(string FullPathPdfFile)
        {
            PdfMetaData pdfFileData = null;
            var bmkFile = Path.ChangeExtension(FullPathPdfFile, "bmk");
            if (File.Exists(bmkFile))
            {
                if (FullPathPdfFile.Contains("James Sco"))
                {
                    "asdf".ToArray();
                }
                try
                {
                    var serializer = new XmlSerializer(typeof(PdfMetaData));
                    using (var sr = new StreamReader(bmkFile))
                    {
                        pdfFileData = (PdfMetaData)serializer.Deserialize(sr);
                        pdfFileData._FullPathRootFile = FullPathPdfFile;
                        pdfFileData.initialLastPageNo = pdfFileData.LastPageNo;
                        if (pdfFileData.HideThisPDFFile)
                        {
                            pdfFileData = null;
                        }
                        else
                        {
                            if (pdfFileData.lstVolInfo.Count == 0) // import old data
                            {
                                pdfFileData.lstVolInfo.Add(new PdfVolumeInfo()
                                {
                                    NPagesInThisVolume = getNumPages(FullPathPdfFile),
                                    Rotation = 0
                                });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.Forms.MessageBox.Show($"{bmkFile}\r\n {ex.ToString()}", "Exception parsing Xml");
                    // we don't want to delete the file because the user might have valuable bookmarks/favorites.
                    // let the user have an opportunity to fix it.
                }
            }
            else
            {
                pdfFileData = new PdfMetaData()
                {
                    _FullPathRootFile = FullPathPdfFile
                };
                pdfFileData.lstVolInfo.Add(new PdfVolumeInfo()
                {
                    NPagesInThisVolume = getNumPages(FullPathPdfFile),
                    Rotation = 0
                });
            }
            pdfFileData?.Initialize();
            return pdfFileData;
        }

        private static int getNumPages(string FullPathFile)
        {
            var tkf = StorageFile.GetFileFromPathAsync(FullPathFile);
            tkf.AsTask().Wait();
            var tkpdfDoc = PdfDocument.LoadFromFileAsync(tkf.AsTask().Result).AsTask();
            tkpdfDoc.Wait();
            var pdfDoc = tkpdfDoc.Result;
            return (int)pdfDoc.PageCount;
        }

        public PdfMetaData() { }

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
        private void Initialize()
        {
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
            lstPdfDocuments.Clear();
            var pageNo = 0;
            for (int volNo = 0; volNo < lstVolInfo.Count; volNo++)
            {

                //                var t = Task<PdfDocument>.Factory.cr
                var task = new Task<PdfDocument>((pg) =>
                {
                    PdfDocument pdfDoc = null;
                    //var pathPdfFileVol = GetFullPathFileFromPageNo(pageNo);
                    //StorageFile f = await StorageFile.GetFileFromPathAsync(pathPdfFileVol);
                    //pdfDoc = await PdfDocument.LoadFromFileAsync(f);
                    //if (pdfDoc.IsPasswordProtected)
                    //{
                    //    //    this.dpPage.Children.Add(new TextBlock() { Text = $"Password Protected {pathPdfFileVol}" });
                    //}
                    pdfDoc = GetPdfDocumentAsync((int)pg).GetAwaiter().GetResult();
                    return pdfDoc;
                }, pageNo);
                pageNo += lstVolInfo[volNo].NPagesInThisVolume;
                this.lstPdfDocuments.Add(task);
            }
        }
        public async Task<PdfDocument> GetPdfDocumentForPageno(int pageNo)
        {
            var volno = GetVolNumFromPageNum(pageNo);
            var res = lstPdfDocuments[volno];
            if (res.Status == TaskStatus.Created)
            {
                res.Start();
            }
            PdfDocument pdfDoc = null;
            if (res.IsCompleted)
            {
                pdfDoc = res.Result;
            }
            else
            {
                pdfDoc = await res;
            }
            return pdfDoc;
        }
        internal async Task<PdfDocument> GetPdfDocumentAsync(int pageNo)
        {
            PdfDocument pdfDoc = null;
            var pathPdfFileVol = GetFullPathFileFromPageNo(pageNo);
            StorageFile f = await StorageFile.GetFileFromPathAsync(pathPdfFileVol);
            pdfDoc = await PdfDocument.LoadFromFileAsync(f);
            if (pdfDoc.IsPasswordProtected)
            {
                //    this.dpPage.Children.Add(new TextBlock() { Text = $"Password Protected {pathPdfFileVol}" });
            }
            return pdfDoc;
        }
        public static void SavePdfFileData(PdfMetaData pdfFileData, bool ForceSave = false)
        {
            //var fTsv = @"C:\Users\calvinh\Documents\t.txt";
            //var lines = File.ReadAllLines(fTsv);
            //var lstTocEntries = new List<TOCEntry>();
            //foreach (var line in lines.Where(l => !string.IsNullOrEmpty(l.Trim())))
            //{
            //    var parts = line.Split("\t".ToArray());
            //    var tocEntry = new TOCEntry()
            //    {
            //        PageNo = int.Parse( parts[1].Trim()),
            //        SongName = parts[0].Trim(),
            //        Composer = parts[2].Trim(),
            //        Notes=parts[3].Trim(),
            //        Date = parts[4].Trim().Replace(".",string.Empty)
            //    };
            //    lstTocEntries.Add(tocEntry);
            //}
            //pdfFileData.lstTocEntries = lstTocEntries;

            /*
 <TableOfContents>
  <TOCEntry>
   <SongName>sample</SongName>
   <PageNo>23</PageNo>
  </TOCEntry>
 </TableOfContents>
             
             */
            //var bm = new TOCEntry()
            //{
            //    SongName = "sample",
            //    PageNo = 23
            //};
            //var lstBms = new List<TOCEntry>
            //{
            //    bm
            //};
            //pdfFileData.TableOfContents = lstBms.ToArray();

            //var ftxt = @"C:\Users\calvinh\Documents\t.txt";
            //var lines = File.ReadAllLines(ftxt);
            //var lstTocEntries = new List<TOCEntry>();
            //foreach (var line in lines.Where(l => !string.IsNullOrEmpty(l)))
            //{

            //    var space = line.IndexOf(" ");

            //    var pageno = int.Parse(line.Substring(0,space));
            //    var title = line.Substring(space+1).Trim();
            //    //var yearRawStart = title.LastIndexOf('(');
            //    //var yearStr = title.Substring(yearRawStart).Replace("(", string.Empty).Replace(")", string.Empty);
            //    //                title = title.Substring(0, yearRawStart).Trim();
            //    var tocEntry = new TOCEntry()
            //    {
            //        PageNo = pageno,
            //        SongName = title,
            //    };
            //    lstTocEntries.Add(tocEntry);
            //}
            //pdfFileData.lstTocEntries = lstTocEntries;


            if (pdfFileData.IsDirty || ForceSave || pdfFileData.initialLastPageNo != pdfFileData.LastPageNo)
            {
                // we saved memory
                //                pdfFileData.lstTocEntries = pdfFileData.dictToc.Values.ToList();
                //// got dup favs: remove dupes
                //var dictFav = new HashSet<int>();
                //foreach (var fav in pdfFileData.Favorites.OrderBy(p => p.Pageno))
                //{
                //    dictFav.Add(fav.Pageno);
                //}
                //pdfFileData.Favorites.Clear();
                //foreach (var f in dictFav)
                //{
                //    pdfFileData.Favorites.Add(new Favorite() { Pageno = f });
                //}
                //                pdfFileData.Favorites = pdfFileData.Favorites.Distinct().OrderBy(p=>p.Pageno).ToList();

                var serializer = new XmlSerializer(typeof(PdfMetaData));
                var settings = new XmlWriterSettings()
                {
                    Indent = true,
                    IndentChars = " "
                };
                var bmkFile = Path.ChangeExtension(pdfFileData._FullPathRootFile, "bmk");
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
        public BitmapImage GetBitmapImageThumbnail()
        {
            var bmi = bitmapImageCache;
            if (bmi == null)
            {
                bmi = this.bitmapImageCache;
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
                var f = await StorageFile.GetFileFromPathAsync(GetFullPathFile(volNo: 0));
                var pdfDoc = await PdfDocument.LoadFromFileAsync(f);

                //StorageFile f = await StorageFile.GetFileFromPathAsync(pdfMetaDataItem.FullPathFile);
                //var pdfDoc = await PdfDocument.LoadFromFileAsync(f);
                bmi = new BitmapImage();
                using (var page = pdfDoc.GetPage(0))
                {
                    using (var strm = new InMemoryRandomAccessStream())
                    {
                        var rect = page.Dimensions.ArtBox;
                        var renderOpts = new PdfPageRenderOptions
                        {
                            DestinationWidth = (uint)150, // match these with choose.xaml
                            DestinationHeight = (uint)225
                        };

                        await page.RenderToStreamAsync(strm, renderOpts);
                        //var enc = new PngBitmapEncoder();
                        //enc.Frames.Add(BitmapFrame.Create)
                        bmi.BeginInit();
                        bmi.StreamSource = strm.AsStream();
                        bmi.CacheOption = BitmapCacheOption.OnLoad;
                        bmi.Rotation = (Rotation)GetRotation(pgNo: 0);
                        bmi.EndInit();
                    }
                }
                bitmapImageCache = bmi;
            }
            return bmi;
        }

        internal bool IsFavorite(int PageNo)
        {
            var isFav = false;
            if (Favorites.Where(f => f.Pageno == PageNo).Any())
            {
                isFav = true;
            }
            return isFav;
        }

        internal void ToggleFavorite(int PageNo, bool IsFavorite)
        {
            this.IsDirty = true;
            for (int i = 0; i < Favorites.Count; i++)
            {
                if (Favorites[i].Pageno == PageNo) // already in list of favs
                {
                    if (IsFavorite) // already set as favorite, do nothing
                    {

                    }
                    else
                    {
                        // remove it
                        Favorites.RemoveAt(i);
                    }
                    return;
                }
            }
            if (IsFavorite)
            {
                Favorites.Add(new Favorite()
                {
                    Pageno = PageNo
                });
            }
        }

        public int GetPdfVolPageNo(int Pgno)
        {
            var res = Pgno;
            var volno = GetVolNumFromPageNum(Pgno);
            for (int i = 0; i < volno; i++)
            {
                res -= lstVolInfo[i].NPagesInThisVolume;
            }
            Debug.Assert(res >= 0, "page must be >=0");
            return res;
        }

        public override string ToString()
        {
            return $"{Path.GetFileNameWithoutExtension(_FullPathRootFile)}";
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
        public override string ToString()
        {
            return $"{NPagesInThisVolume} {Rotation}";
        }
    }

    [Serializable]
    public class Favorite : ICloneable
    {
        public string FavoriteName;
        public int Pageno;
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


    /// <summary>
    /// Not really bookmark: Table of Contents Entry
    /// </summary>
    [Serializable]
    public class TOCEntry : ICloneable
    {
        public string SongName;
        public string Composer;
        public string Notes;
        public string Date;
        public int PageNo;
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
            return $"{PageNo} {SongName} {Composer} {Notes} {Date}";
        }
    }
}