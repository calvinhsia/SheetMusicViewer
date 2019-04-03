using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Serialization;

namespace WpfPdfViewer
{
    /// <summary>
    /// The serialized info for a PDF is in a file with the exact same name as the PDF with the extension changed to ".bmk"
    /// </summary>
    [Serializable]
    public class PdfMetaData
    {
        [XmlIgnore]
        public string FullPathFile;
        [XmlIgnore]
        public string RelativeFileName => FullPathFile.Substring(PdfViewerWindow.s_pdfViewerWindow._RootMusicFolder.Length + 1);
        [XmlIgnore]
        public List<Favorite> lstFavorites;

        [XmlIgnore]
        public List<TOCEntry> lstTocEntries;
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
        /// </summary>
        public int LastPageNo;

        bool IsDirty = false;
        int initialLastPageNo;

        internal string GetDescription(int currentPageNumber)
        {
            var str = string.Empty;
            foreach (var ent in lstTocEntries.Where(e => e.PageNo == currentPageNumber))
            {
                str += ent + " ";
            }
            return str.Trim();
        }


        /// <summary>
        /// The Table of contents of a songbook shows the scanned page numbers, which may not match the actual PDF page numbers (there could be a cover page scanned
        /// or could be a multivolume set)
        /// We want to keep the scabbed ORC TOC editing, cleanup, true and minimize required editing, so keep the original page numbers.
        /// The scanned imported OCR TOC will show the physical page no, but not the actual PDF page no.
        /// This value will map between the 2 so that the imported scanned TOC saved as XML will not need to be adjusted.
        /// For 1st, 2nd, 3rd volumes, the offset from the actual scanned page number (as visible on the page) to the PDF page number
        /// e.g. the very 1st volume might have a cover page, which is page 0. Viewing song Foobar might show page 44, but it's really PdfPage=45, 
        /// so we set PageNumberOffset to 1
        /// For vol 4 (PriorPdfMetaData != null), the 1st song "Please Mr Postman" might show page 403, but it's really PdfPage 0. So PageNumberOffset = 403
        /// So the XML for the song will say 403 (same as scanned TOC), but the actual PDFpage no in vol 4 = (403 - PageNumberOffset == 0)
        /// The next song "Poor Side Of Town" on page 404 ins on PdfPage 1. Toc = 404. diff == PageNumberOffset== 403
        /// </summary>
        public int PageNumberOffset;
        /// <summary>
        /// Hide it so it doesn't show anywhere in the UI
        /// </summary>
        public bool HideThisPDFFile;

        /*Normal = 0,Rotate90 = 1,Rotate180 = 2,Rotate270 = 3*/
        public int Rotation;
        public TOCEntry[] TableOfContents;
        public Favorite[] Favorites;
        public string Notes;
        internal BitmapImage bitmapImageCacheThumbnail;

        public static PdfMetaData ReadPdfMetaData(string FullPathFile)
        {
            PdfMetaData pdfFileData = null;
            var bmkFile = Path.ChangeExtension(FullPathFile, "bmk");
            if (File.Exists(bmkFile))
            {
                try
                {
                    var serializer = new XmlSerializer(typeof(PdfMetaData));
                    using (var sr = new StreamReader(bmkFile))
                    {
                        pdfFileData = (PdfMetaData)serializer.Deserialize(sr);
                        pdfFileData.FullPathFile = FullPathFile;
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
                    FullPathFile = FullPathFile
                };
            }
            pdfFileData?.Initialize();
            return pdfFileData;
        }
        public PdfMetaData() { }

        private void Initialize()
        {
            lstFavorites = new List<Favorite>();
            if (Favorites != null)
            {
                lstFavorites.AddRange(Favorites);
            }
            lstTocEntries = new List<TOCEntry>();
            if (TableOfContents != null)
            {
                lstTocEntries.AddRange(TableOfContents);
            }
        }

        public static void SavePdfFileData(PdfMetaData pdfFileData)
        {
            //var fTsv = @"C:\Users\calvinh\Documents\Book1.txt";
            //var lines = File.ReadAllLines(fTsv);
            //var lstTocEntries = new List<TOCEntry>();
            //foreach (var line in lines)
            //{
            //    var parts = line.Split("\t".ToArray());
            //    var tocEntry = new TOCEntry()
            //    {
            //        PageNo =int.Parse( parts[0]),
            //        SongName=parts[1],
            //        Composer=parts[2],
            //        Date=parts[3]
            //    };
            //    lstTocEntries.Add(tocEntry);
            //}
            //pdfFileData.TableOfContents = lstTocEntries.ToArray();

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
            if (pdfFileData.IsDirty || pdfFileData.initialLastPageNo != pdfFileData.LastPageNo)
            {
                pdfFileData.Favorites = pdfFileData.lstFavorites.OrderBy(f => f.Pageno).ToArray();
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
                var strm = File.Create(bmkFile);
                using (var w = XmlWriter.Create(strm, settings))
                {
                    serializer.Serialize(w, pdfFileData);
                }
            }
        }

        internal bool IsFavorite(int PageNo)
        {
            var isFav = false;
            if (lstFavorites.Where(f => f.Pageno == PageNo).Any())
            {
                isFav = true;
            }
            return isFav;
        }

        internal void ToggleFavorite(int PageNo, bool IsFavorite)
        {
            this.IsDirty = true;
            for (int i = 0; i < lstFavorites.Count; i++)
            {
                if (lstFavorites[i].Pageno == PageNo) // already in list of favs
                {
                    if (IsFavorite) // already set as favorite, do nothing
                    {

                    }
                    else
                    {
                        // remove it
                        lstFavorites.RemoveAt(i);
                    }
                    return;
                }
            }
            if (IsFavorite)
            {
                lstFavorites.Add(new Favorite()
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
    public class Favorite
    {
        public string FavoriteName;
        public int Pageno;
        public override string ToString()
        {
            return $"{FavoriteName} {Pageno}".Trim();
        }
    }


    /// <summary>
    /// Not really bookmark: Table of Contents Entry
    /// </summary>
    [Serializable]
    public class TOCEntry
    {
        public string SongName;
        public string Composer;
        public string Notes;
        public string Date;
        public int PageNo;
        public override string ToString()
        {
            return $"{PageNo} {SongName} {Composer} {Notes} {Date}";
        }
    }
}