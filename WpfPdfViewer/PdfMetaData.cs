using System;
using System.Collections.Generic;
using System.ComponentModel;
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
        [XmlIgnore]
        public string FullPathFile;
        [XmlIgnore]
        public string RelativeFileName => FullPathFile.Substring(PdfViewerWindow.s_pdfViewerWindow._RootMusicFolder.Length + 1);
        /// <summary>
        /// stored in root
        /// </summary>
        public List<Favorite> Favorites = new List<Favorite>();

        /// <summary>
        /// stored in root
        /// </summary>
        public List<TOCEntry> lstTocEntries = new List<TOCEntry>();

        /// <summary>
        /// stored in root
        /// </summary>
        [XmlIgnore]
        public Dictionary<int, List<TOCEntry>> dictToc = new Dictionary<int, List<TOCEntry>>();
        /// <summary>
        /// for continued PDF: e.g. file1.pdf, file2.pdf. Forms a linked list
        /// </summary>
        [XmlIgnore]
        public PdfMetaData PriorPdfMetaData;
        /// <summary>
        /// for continued PDF: e.g. file1.pdf, file2.pdf. Forms a linked list
        /// </summary>
        [XmlIgnore]
        public PdfMetaData SucceedingPdfMetaData;

        /// <summary>
        /// the page no when this PDF was last opened
        /// stored in root
        /// </summary>
        public int LastPageNo;
        /// <summary>
        /// The num PDF pages in this PDF file
        /// </summary>
        public int NumPages;

        bool IsDirty = false;
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

        /*Normal = 0,Rotate90 = 1,Rotate180 = 2,Rotate270 = 3*/
        public int Rotation;
        public string Notes;


        [XmlIgnore]
        internal BitmapImage bitmapImageCache;
        internal string GetDescription(int currentPageNumber)
        {
            var str = string.Empty;
            var tocPgNm = currentPageNumber + PageNumberOffset;
            if (dictToc.TryGetValue(tocPgNm, out var lstTocs))
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


        public static PdfMetaData ReadPdfMetaData(string FullPathPdfFile)
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
                        pdfFileData.FullPathFile = FullPathPdfFile;
                        pdfFileData.initialLastPageNo = pdfFileData.LastPageNo;
                        if (pdfFileData.HideThisPDFFile)
                        {
                            pdfFileData = null;
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
                    FullPathFile = FullPathPdfFile
                };
            }
            pdfFileData?.Initialize();
            return pdfFileData;
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
            //            lstTocEntries = null; // save memory
        }

        public static void SavePdfFileData(PdfMetaData pdfFileData)
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


            if (pdfFileData.IsDirty || pdfFileData.initialLastPageNo != pdfFileData.LastPageNo)
            {
                // we saved memory
                //                pdfFileData.lstTocEntries = pdfFileData.dictToc.Values.ToList();

                var serializer = new XmlSerializer(typeof(PdfMetaData));
                var settings = new XmlWriterSettings()
                {
                    Indent = true,
                    IndentChars = " "
                };
                var bmkFile = Path.ChangeExtension(pdfFileData.FullPathFile, "bmk");
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

        public PdfMetaData GetRootMetaData()
        {
            var pmetadataFile = this;
            while (pmetadataFile.PriorPdfMetaData != null)
            {
                pmetadataFile = pmetadataFile.PriorPdfMetaData;
            }
            return pmetadataFile;
        }
        /// <summary>
        /// Total page count across volume
        /// </summary>
        /// <returns></returns>
        public int GetTotalPageCount()
        {
            var pmetadataFile = this.GetRootMetaData();
            int nCnt = 0;
            while (pmetadataFile != null)
            {
                nCnt += pmetadataFile.NumPages;
                pmetadataFile = pmetadataFile.SucceedingPdfMetaData;
            }
            return nCnt;
        }

        /// <summary>
        /// Total song count across volume
        /// </summary>
        /// <returns></returns>
        public int GetSongCount()
        {
            var pmetadataFile = this.GetRootMetaData();
            int nCnt = 0;
            while (pmetadataFile != null)
            {
                nCnt += pmetadataFile.lstTocEntries.Count;
                pmetadataFile = pmetadataFile.SucceedingPdfMetaData;
            }
            return nCnt;
        }
        public BitmapImage GetBitmapImageThumbnail()
        {
            var bmi = bitmapImageCache;
            if (bmi == null)
            {
                bmi = this.GetRootMetaData().bitmapImageCache;
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
                var metadataFileHead = this.GetRootMetaData();// now see if root one has one
                bmi = metadataFileHead.bitmapImageCache;
                if (bmi == null)
                {
                    var f = await StorageFile.GetFileFromPathAsync(metadataFileHead.FullPathFile);
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
                                DestinationWidth = (uint)150,
                                DestinationHeight = (uint)225
                            };

                            await page.RenderToStreamAsync(strm, renderOpts);
                            //var enc = new PngBitmapEncoder();
                            //enc.Frames.Add(BitmapFrame.Create)
                            bmi.BeginInit();
                            bmi.StreamSource = strm.AsStream();
                            bmi.CacheOption = BitmapCacheOption.OnLoad;
                            bmi.Rotation = (Rotation)metadataFileHead.Rotation;
                            bmi.EndInit();
                        }
                    }
                    metadataFileHead.bitmapImageCache = bmi;
                }
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
        public override string ToString()
        {
            return $"{Path.GetFileName(FullPathFile)} {LastPageNo}";
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