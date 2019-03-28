using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace WpfPdfViewer
{
    /// <summary>
    /// The serialized info for a PDF is in a file with the exact same name as the PDF with the extension changed to ".bmk"
    /// </summary>
    [Serializable]
    public class PdfFileData
    {
        [XmlIgnore]
        public string curFullPathFile;

        public static PdfFileData CreatePdfFileData(string FullPathFile)
        {
            PdfFileData pdfFileData = null;
            var bmkFile = Path.ChangeExtension(FullPathFile, "bmk");
            if (File.Exists(bmkFile))
            {
                var serializer = new XmlSerializer(typeof(PdfFileData));
                using (var sr = new StreamReader(bmkFile))
                {
                    pdfFileData = (PdfFileData)serializer.Deserialize(sr);
                    pdfFileData.curFullPathFile = FullPathFile;
                    if (pdfFileData.HideThisPDFFile)
                    {
                        pdfFileData = null;
                    }
                }
            }
            else
            {
                pdfFileData = new PdfFileData(FullPathFile);
            }
            return pdfFileData;
        }
        public PdfFileData() { }

        public static void SavePdfFileData(PdfFileData pdfFileData)
        {
            var bm = new BookMark()
            {
                BookMarkName = "bmname",
                BookMarkPageNo = 23
            };
            var lstBms = new List<BookMark>();
            lstBms.Add(bm);
            pdfFileData.BookMarks = lstBms.ToArray();
            var serializer = new XmlSerializer(typeof(PdfFileData));
            var bmkFile = Path.ChangeExtension( pdfFileData.curFullPathFile, "bmk");
            using (var sw = new StreamWriter(bmkFile))
            {
                serializer.Serialize(sw, pdfFileData);
            }
        }

        public PdfFileData(string curFullPathFile)
        {
            this.curFullPathFile = curFullPathFile;
        }
        /// <summary>
        /// Could be duplicate: a PDF might be part of an assembled volume
        /// </summary>
        public bool HideThisPDFFile;
        public BookMark[] BookMarks;
        public override string ToString()
        {
            return $"{Path.GetFileName(curFullPathFile)}";
        }
    }

    [Serializable]
    public class BookMark
    {
        public string BookMarkName;
        public uint BookMarkPageNo;
        public override string ToString()
        {
            return $"{BookMarkName} {BookMarkPageNo}";
        }
    }
}