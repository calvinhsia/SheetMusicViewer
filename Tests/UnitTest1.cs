using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WpfPdfViewer;

namespace Tests
{
    public class TestBase
    {
        public TestContext TestContext { get; set; }
        [TestInitialize]
        public void TestInitialize()
        {
            TestContext.WriteLine($"{DateTime.Now.ToString()} Starting test {TestContext.TestName}");
        }
    }

    [TestClass]
    public class UnitTest1 : TestBase
    {
        readonly string rootfolder = @"C:\Users\calvinh\OneDrive\Documents\SheetMusic";
        //string testbmk = @"C:\Users\calvinh\OneDrive\Documents\SheetMusic\FakeBooks\The Ultimate Pop Rock Fake Book.bmk";
        readonly string testPdf = @"C:\Users\calvinh\OneDrive\Documents\SheetMusic\FakeBooks\The Ultimate Pop Rock Fake Book.pdf";

        [TestMethod]
        [Ignore]
        public async Task TestReadBmkData()
        {
            var rootfolder = @"C:\Bak\SheetMusic\Poptest";
            rootfolder = @"C:\Users\calvinh\OneDrive\Documents\SheetMusic";
            var w = new WpfPdfViewer.PdfViewerWindow
            {
                _RootMusicFolder = rootfolder
            };
            var lstMetaData= await PdfMetaData.LoadAllPdfMetaDataFromDiskAsync(w._RootMusicFolder);
            foreach(var pm in lstMetaData)
            {
                TestContext.WriteLine($"{pm.GetFullPathFile(volNo: 0)}");
                foreach (var vol in pm.lstVolInfo)
                {
                    TestContext.WriteLine($"   {vol.ToString()}");
                }
            }
        }

        [TestMethod]
        public void TestCreatePdfMetaData()
        {
            var pdfData = PdfMetaData.ReadPdfMetaData(testPdf);
            TestContext.WriteLine($"pdfdata = {pdfData}");
        }

        [TestMethod]
        public async Task TestCreateBmpCache()
        {
            var w = new WpfPdfViewer.PdfViewerWindow
            {
                _RootMusicFolder = rootfolder
            };
            await PdfMetaData.LoadAllPdfMetaDataFromDiskAsync(w._RootMusicFolder);
            for (int i = 0; i < 11; i++)
            {
                GetBMPs(w);
            }
        }
        void GetBMPs(WpfPdfViewer.PdfViewerWindow w)
        {
            TestContext.WriteLine($"Got PDFMetaDataCnt={w.lstPdfMetaFileData.Count}");
            foreach (var pdfMetaData in w.lstPdfMetaFileData)
            {
                var bmi = pdfMetaData.GetBitmapImageThumbnailAsync();
                pdfMetaData.bitmapImageCache = null;
//                TestContext.WriteLine($" {pdfMetaData} {bmi.PixelWidth} {bmi.PixelHeight}");
            }
            var classicalPdf = w.lstPdfMetaFileData[0];
            // with no renderoptions,wh=(794,1122), pixelHeight= (1589, 2245 )
            // with renderops = 150,225 wh= (225,150), pixelhw = (300, 450), dpix = dpiy = 192
            //            var bmi = classicalPdf.GetBitmapImageThumbnail();

        }
    }
}
