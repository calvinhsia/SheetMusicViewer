using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Windows.Data.Pdf;
using Windows.Storage.Streams;
using WpfPdfViewer;
using static WpfPdfViewer.PdfViewerWindow;

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
        readonly string root1 = @"c:\Sheetmusic";
        readonly string root2 = @"f:\Sheetmusic";
        string Rootfolder { get { if (Directory.Exists(root1)) { return root1; } return root2; } }
        //string testbmk = @"C:\Users\calvinh\OneDrive\Documents\SheetMusic\FakeBooks\The Ultimate Pop Rock Fake Book.bmk";
        readonly string testPdf = @"C:\SheetMusic\FakeBooks\The Ultimate Pop Rock Fake Book.pdf";

        [TestMethod]
        public async Task TestStress()
        {
            var w = new WpfPdfViewer.PdfViewerWindow
            {
                _RootMusicFolder = Rootfolder
            };
            var lstMetaData = await PdfMetaData.LoadAllPdfMetaDataFromDiskAsync(w._RootMusicFolder);
            var currentPdfMetaData = lstMetaData.Where(m => m.GetFullPathFile(volNo: 0).Contains("Fake")).First();
            w.currentPdfMetaData = currentPdfMetaData;
            w.currentPdfMetaData.InitializeListPdfDocuments();
            //var cacheEntry = PdfViewerWindow.CacheEntry.TryAddCacheEntry(mpdf.PageNumberOffset);
            //await cacheEntry.task;
            //// calling thread must be STA, UIThread
            //var res = cacheEntry.task.Result;
            for (var iter = 0; iter < 12; iter++)
            {
                var pageNo = 0;
                for (pageNo = currentPdfMetaData.PageNumberOffset; pageNo < currentPdfMetaData.NumPagesInSet + currentPdfMetaData.PageNumberOffset - 1; pageNo++)

                {
                    var (pdfDoc, pdfPgno) = await currentPdfMetaData.GetPdfDocumentForPageno(pageNo);
                    //                    var pdfPgNo = currentPdfMetaData.GetPdfVolPageNo(pageNo + i);
                    if (pdfDoc != null && pdfPgno >= 0 && pdfPgno < pdfDoc.PageCount)
                    {
                        using (var pdfPage = pdfDoc.GetPage((uint)(pdfPgno)))
                        {
                            using (var strm = new InMemoryRandomAccessStream())
                            {
                                var rect = pdfPage.Dimensions.ArtBox;
                                var renderOpts = new PdfPageRenderOptions()
                                {
                                    DestinationWidth = (uint)rect.Width,
                                    DestinationHeight = (uint)rect.Height,
                                };
                                if (pdfPage.Rotation != PdfPageRotation.Normal)
                                {
                                    renderOpts.DestinationHeight = (uint)rect.Width;
                                    renderOpts.DestinationWidth = (uint)rect.Height;
                                }

                                await pdfPage.RenderToStreamAsync(strm, renderOpts);
                                var bmi = new BitmapImage();
                                bmi.BeginInit();
                                bmi.StreamSource = strm.AsStream();
                                //                                bmi.Rotation = (Rotation)currentPdfMetaData.GetRotation(cacheEntry.pageNo);
                                bmi.CacheOption = BitmapCacheOption.OnLoad;
                                bmi.EndInit();

                                TestContext.WriteLine($"got page {pageNo,5}   strms={strm.Size,10:n0} {currentPdfMetaData} ");
                                //                                break;
                            }
                        }
                    }
                }
            }

        }
        [TestMethod]
        public async Task TestCache()
        {
            var ev = new ManualResetEventSlim();
            //Thread dispThread = new Thread((p) =>
            //{
            //    var c = CreateExecutionContext();
            //    SynchronizationContext.SetSynchronizationContext(c.DispatcherSynchronizationContext);
            //    Dispatcher.Run();
            //    ev.Set();
            //});
            //dispThread.SetApartmentState(ApartmentState.STA);
            //dispThread.Start();
            var c = CreateExecutionContext();
            await c.Dispatcher.InvokeAsync(async () =>
            {
                var w = new WpfPdfViewer.PdfViewerWindow
                {
                    _RootMusicFolder = Rootfolder
                };
                var testw = new Window();
                testw.Show();
                var lstMetaData = await PdfMetaData.LoadAllPdfMetaDataFromDiskAsync(w._RootMusicFolder);
                foreach (var currentPdfMetaData in lstMetaData)
                {
                    var sw = Stopwatch.StartNew();
//                    var currentPdfMetaData = lstMetaData.Where(m => m.GetFullPathFile(volNo: 0).Contains("Fake")).First();
                    w.currentPdfMetaData = currentPdfMetaData;
                    w.currentPdfMetaData.InitializeListPdfDocuments();
                    //var cacheEntry = PdfViewerWindow.CacheEntry.TryAddCacheEntry(mpdf.PageNumberOffset);
                    //await cacheEntry.task;
                    //// calling thread must be STA, UIThread
                    //var res = cacheEntry.task.Result;
                    for (var iter = 0; iter < 1; iter++)
                    {
                        var pageNo = 0;
                        for (pageNo = currentPdfMetaData.PageNumberOffset; pageNo < currentPdfMetaData.NumPagesInSet + currentPdfMetaData.PageNumberOffset - 1; pageNo++)
                        {
                            var cacheEntry = w._pageCache.TryAddCacheEntry(pageNo);
                            await cacheEntry.task;
                            var bmi = cacheEntry.task.Result;
                            var image = new Image() { Source = bmi };
                            testw.Content = image;
                            TestContext.WriteLine($"got page {pageNo,8}   bmi={bmi.Width:n0}, {bmi.Height:n0}  {sw.Elapsed.TotalSeconds,8:n4} {currentPdfMetaData} ");
                            //break;
                        }
                    }
                }
                ev.Set();
            });
            ev.Wait();
        }

        private ExecutionContext CreateExecutionContext()
        {
            const string Threadname = "MyMockUIThread";
            var tcs = new TaskCompletionSource<ExecutionContext>();

            var mockUIThread = new Thread(() =>
            {
                // Create the context, and install it:
                var dispatcher = Dispatcher.CurrentDispatcher;
                var syncContext = new DispatcherSynchronizationContext(dispatcher);

                SynchronizationContext.SetSynchronizationContext(syncContext);

                tcs.SetResult(new ExecutionContext
                {
                    DispatcherSynchronizationContext = syncContext,
                    Dispatcher = dispatcher
                });

                // Start the Dispatcher Processing
                TestContext.WriteLine($"{Threadname}  dispatcher run");
                Dispatcher.Run();
                TestContext.WriteLine($"{Threadname} done");
                "".ToString();
            });

            mockUIThread.SetApartmentState(ApartmentState.STA);
            mockUIThread.Name = Threadname;
            TestContext.WriteLine($"{Threadname} start");
            mockUIThread.Start();

            return tcs.Task.Result;
        }

        internal class ExecutionContext
        {
            public DispatcherSynchronizationContext DispatcherSynchronizationContext { get; set; }
            public Dispatcher Dispatcher { get; set; }
        }

        [TestMethod]
        [Ignore]
        public async Task TestReadBmkData()
        {
            var //rootfolder = @"C:\Bak\SheetMusic\Poptest";
            rootfolder = @"C:\Users\calvinh\OneDrive\Documents\SheetMusic";
            var w = new WpfPdfViewer.PdfViewerWindow
            {
                _RootMusicFolder = rootfolder
            };
            var lstMetaData = await PdfMetaData.LoadAllPdfMetaDataFromDiskAsync(w._RootMusicFolder);
            foreach (var pm in lstMetaData)
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
            var pdfData = PdfMetaData.ReadPdfMetaDataAsync(testPdf);
            TestContext.WriteLine($"pdfdata = {pdfData}");
        }

        [TestMethod]
        public async Task TestCreateBmpCache()
        {
            var w = new WpfPdfViewer.PdfViewerWindow
            {
                _RootMusicFolder = Rootfolder
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
            //var classicalPdf = w.lstPdfMetaFileData[0];
            // with no renderoptions,wh=(794,1122), pixelHeight= (1589, 2245 )
            // with renderops = 150,225 wh= (225,150), pixelhw = (300, 450), dpix = dpiy = 192
            //            var bmi = classicalPdf.GetBitmapImageThumbnail();

        }
    }
}
