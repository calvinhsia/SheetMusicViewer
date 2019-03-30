using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public string curFullPathFile;
        [XmlIgnore]
        public List<Favorite> lstFavorites;
        /// <summary>
        /// for continued PDF: e.g. file1.pdf, file2.pdf
        /// </summary>
        [XmlIgnore]
        public PdfMetaData PriorPdfMetaData;
        /// <summary>
        /// for continued PDF: e.g. file1.pdf, file2.pdf
        /// </summary>
        [XmlIgnore]
        public PdfMetaData SucceedingPdfMetaData;


        /// <summary>
        /// the page no when this PDF was last opened
        /// </summary>
        public int LastPageNo;
        /// <summary>
        /// Could be duplicate: a PDF might be part of an assembled volume
        /// </summary>
        public bool HideThisPDFFile;

        /*Normal = 0,Rotate90 = 1,Rotate180 = 2,Rotate270 = 3*/
        public int Rotation;
        public BookMark[] BookMarks;
        public Favorite[] Favorites;
        public string Notes;


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
                        pdfFileData.curFullPathFile = FullPathFile;
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
                    curFullPathFile = FullPathFile
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
        }

        public static void SavePdfFileData(PdfMetaData pdfFileData)
        {
            var bm = new BookMark()
            {
                SongName = "SongName",
                PageNo = 23
            };
            var lstBms = new List<BookMark>
            {
                bm
            };
            pdfFileData.BookMarks = lstBms.ToArray();
            pdfFileData.Favorites = pdfFileData.lstFavorites.OrderBy(f => f.Pageno).ToArray();
            var serializer = new XmlSerializer(typeof(PdfMetaData));
            var bmkFile = Path.ChangeExtension(pdfFileData.curFullPathFile, "bmk");
            using (var sw = new StreamWriter(bmkFile))
            {
                serializer.Serialize(sw, pdfFileData);
            }
        }

        internal bool IsFavorite(int PageNo)
        {
            var isFav = false;
            if (lstFavorites.Where(f=>f.Pageno == PageNo).Any())
            {
                isFav = true;
            }
            return isFav;
        }

        internal void ToggleFavorite(int PageNo, bool IsFavorite)
        {
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
            return $"{Path.GetFileName(curFullPathFile)}";
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

    [Serializable]
    public class BookMark
    {
        public string SongName;
        public string Composer;
        public string Notes;
        public string Date;
        public int PageNo;
        public override string ToString()
        {
            return $"{SongName} {PageNo}";
        }
    }
}