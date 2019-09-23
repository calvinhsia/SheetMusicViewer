using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
        public void AddLogEntry(string msg)
        {
            var str = DateTime.Now.ToString("hh:mm:ss:fff") + " " + msg;
            TestContext.WriteLine(str);
            if (Debugger.IsAttached)
            {
                Debug.WriteLine(str);
            }
        }
    }

    [TestClass]
    public class UnitTest1 : TestBase
    {
        readonly string root1 = @"c:\Sheetmusic";
        readonly string root2 = @"f:\Sheetmusic";
        string Rootfolder { get { if (Directory.Exists(root1)) { return root1; } return root2; } }
        //string testbmk = @"C:\Users\calvinh\OneDrive\Documents\SheetMusic\FakeBooks\The Ultimate Pop Rock Fake Book.bmk";
        //        readonly string testPdf = @"C:\SheetMusic\FakeBooks\The Ultimate Pop Rock Fake Book.pdf";

        [TestMethod]
        //        [Ignore]
        public async Task TestStress()
        {
            var ev = new ManualResetEventSlim();
            var c = CreateExecutionContext();
            await c.Dispatcher.InvokeAsync(async () =>
            {
                var w = new WpfPdfViewer.PdfViewerWindow
                {
                    _RootMusicFolder = Rootfolder
                };
                (var lstMetaData, var _) = await PdfMetaData.LoadAllPdfMetaDataFromDiskAsync(w._RootMusicFolder);
                var testw = new Window();
                testw.Show();
                foreach (var currentPdfMetaData in lstMetaData)
                {
                    AddLogEntry($"Starting {currentPdfMetaData}");

                    w.currentPdfMetaData = currentPdfMetaData;
                    w.currentPdfMetaData.InitializeListPdfDocuments();
                    //var cacheEntry = PdfViewerWindow.CacheEntry.TryAddCacheEntry(mpdf.PageNumberOffset);
                    //await cacheEntry.task;
                    //// calling thread must be STA, UIThread
                    //var res = cacheEntry.task.Result;
                    for (var iter = 0; iter < 1; iter++)
                    {
                        for (var pageNo = currentPdfMetaData.PageNumberOffset; pageNo < currentPdfMetaData.MaxPageNum; pageNo++)
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
                                        bmi.Rotation = (Rotation)currentPdfMetaData.GetRotation(pageNo);
                                        bmi.CacheOption = BitmapCacheOption.OnLoad;
                                        bmi.EndInit();
                                        //testw.Content = new Image()
                                        //{
                                        //    Source = bmi
                                        //};
                                        //if (pdfPage.Rotation != PdfPageRotation.Rotate270 && pdfPage.Rotation != PdfPageRotation.Rotate90)
                                        //{
                                        //    AddLogEntry($"got page {pageNo,5}   strms={strm.Size,10:n0} {pdfPage.Rotation,10} {rect}  {currentPdfMetaData} ");
                                        //}
                                    }
                                }
                            }
                        }
                    }
                }
                AddLogEntry("Done all");
                ev.Set();
            });
            ev.Wait();
        }

        [TestMethod]
        // [Ignore]
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
                var w = new WpfPdfViewer.PdfViewerWindow(Rootfolder)
                {
                    //                    _RootMusicFolder = Path.Combine(Rootfolder, "FakeBooks")
                    IsTesting = true
                };
                w.Show();
                (var lstMetaData, var _) = await PdfMetaData.LoadAllPdfMetaDataFromDiskAsync(w._RootMusicFolder);
                foreach (var currentPdfMetaData in lstMetaData.Where(d => d._FullPathFile.Contains(@"Ragtime\Singles")))
                {
                    //                    var currentPdfMetaData = lstMetaData.Where(m => m.GetFullPathFile(volNo: 0).Contains("Fake")).First();
                    w.currentPdfMetaData = currentPdfMetaData;
                    w.currentPdfMetaData.InitializeListPdfDocuments();
                    //var cacheEntry = PdfViewerWindow.CacheEntry.TryAddCacheEntry(mpdf.PageNumberOffset);
                    //await cacheEntry.task;
                    //// calling thread must be STA, UIThread
                    //var res = cacheEntry.task.Result;
                    AddLogEntry($"Starting book {w.currentPdfMetaData}");
                    w._fShow2Pages = false;
                    for (var iter = 0; iter < 1; iter++)
                    {
                        var pageNo = 0;
                        for (pageNo = currentPdfMetaData.PageNumberOffset; pageNo < currentPdfMetaData.NumPagesInSet + currentPdfMetaData.PageNumberOffset - 1; pageNo++)
                        {
                            await w.ShowPageAsync(pageNo, ClearCache: false);
                            //                            var bmi = await currentPdfMetaData.CalculateBitMapImageForPageAsync(pageNo, cts: null, SizeDesired: null);
                            //                            AddLogEntry(testw.Title);
                            //break;
                        }
                    }
                    w.CloseCurrentPdfFile();
                    //for (int i = 0; i < 5; i++)
                    //{
                    //    GC.Collect(4, GCCollectionMode.Forced);
                    //    GC.WaitForPendingFinalizers();
                    //    Marshal.CleanupUnusedObjectsInCurrentContext();
                    //}
                }
                AddLogEntry($"Done with all");
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
                AddLogEntry($"{Threadname}  dispatcher run");
                Dispatcher.Run();
                AddLogEntry($"{Threadname} done");
                "".ToString();
            });

            mockUIThread.SetApartmentState(ApartmentState.STA);
            mockUIThread.Name = Threadname;
            AddLogEntry($"{Threadname} start");
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
            //rootfolder = @"C:\SheetMusic\Classical";
            rootfolder = @"C:\SheetMusic";
            //rootfolder = @"c:\temp";
            //for (int i = 0;i <10000; i++)
            //{
            //    TestContext.WriteLine($"adfadf {i}");
            //}
            var w = new WpfPdfViewer.PdfViewerWindow
            {
                _RootMusicFolder = rootfolder
            };
            (var lstMetaData, var _) = await PdfMetaData.LoadAllPdfMetaDataFromDiskAsync(w._RootMusicFolder);
            foreach (var pm in lstMetaData)
            {
                if (pm.dictInkStrokes.Count > 0)
                {
                    //foreach (var kvp in pm.dictInkStrokes)
                    //{
                    //    kvp.Value.Pageno = kvp.Value.Pageno;
                    //}
                    //pm.SaveIfDirty(ForceDirty: true);
                    //pm.IsDirty = true;
                    //PdfMetaData.SavePdfMetaFileData(pm);
                }
            }
        }

        [TestMethod]
        public async Task TestSingles()
        {
            var testdir = Path.Combine(Environment.CurrentDirectory, "testdir");
            var singlesFolder = Path.Combine(testdir, "singles");
            for (int i = 0; i < 20; i++)
            {
                AddLogEntry($"Iter {i}");
                if (!Directory.Exists(testdir))
                {
                    Directory.CreateDirectory(testdir);
                }
                foreach (var file in Directory.EnumerateFiles(testdir))
                {
                    AddLogEntry($"Del {file}");
                    File.Delete(file);
                }
                if (Directory.Exists(singlesFolder))
                {
                    foreach (var file in Directory.EnumerateFiles(singlesFolder))
                    {
                        AddLogEntry($"Del {file}");
                        File.Delete(file);
                    }
                    Directory.Delete(singlesFolder, recursive: true);
                }
                Directory.CreateDirectory(singlesFolder);
                var sourceFiles = Directory.GetFiles(Path.Combine(root1, @"ragtime\singles"), "*.pdf").OrderBy(p => p);
                foreach (var file in sourceFiles.Take(2))
                {
                    File.Copy(file, Path.Combine(singlesFolder, Path.GetFileName(file)));
                }

                foreach (var testfile in Directory.EnumerateFiles(singlesFolder))
                {
                    var pdfdoc = await PdfMetaData.GetPdfDocumentForFileAsync(testfile);
                }
            }
        }


        class SinglesTesthelper : IDisposable
        {
            public string testdir;
            public string singlesFolder;
            public IEnumerable<string> sourceFiles;
            public PdfViewerWindow pdfViewerWindow;
            readonly UnitTest1 test;
            public SinglesTesthelper(UnitTest1 test, Func<string, bool> funcFilter)
            {
                this.test = test;
                testdir = Path.Combine(Environment.CurrentDirectory, "testdir");
                singlesFolder = Path.Combine(testdir, "singles");
                test.AddLogEntry($"Test dir {testdir}");
                if (!Directory.Exists(testdir))
                {
                    Directory.CreateDirectory(testdir);
                }
                foreach (var file in Directory.EnumerateFiles(testdir))
                {
                    File.Delete(file);
                }
                if (Directory.Exists(singlesFolder))
                {
                    foreach (var file in Directory.EnumerateFiles(singlesFolder))
                    {
                        File.Delete(file);
                    }
                    Directory.Delete(singlesFolder, recursive: true);
                }
                Directory.CreateDirectory(singlesFolder);
                sourceFiles = Directory.GetFiles(Path.Combine(test.root1, @"ragtime\singles"), "*.pdf").OrderBy(p => p);
                foreach (var file in sourceFiles.OrderBy(f => f).Where(f => funcFilter(f)))
                {
                    var dest = Path.Combine(singlesFolder, Path.GetFileName(file));
                    File.Copy(file, dest);
                }
                pdfViewerWindow = new WpfPdfViewer.PdfViewerWindow
                {
                    _RootMusicFolder = testdir
                };
            }
            public async Task<PdfMetaData> CreateAndSaveBmkAsync()
            {
                (var lstPdfMetaFileData, var _) = await PdfMetaData.LoadAllPdfMetaDataFromDiskAsync(pdfViewerWindow._RootMusicFolder);
                var curmetadata = lstPdfMetaFileData[0];
                test.AddLogEntry($"metadata cnt = {lstPdfMetaFileData.Count}= {curmetadata}");
                Assert.AreEqual(1, lstPdfMetaFileData.Count);
                //                curmetadata.ToggleFavorite(PageNo: 2, IsFavorite: true);
                curmetadata.SaveIfDirty(ForceDirty: true);
                //                test.AddLogEntry(File.ReadAllText(curmetadata.PdfBmkMetadataFileName));
                foreach (var vol in curmetadata.lstVolInfo)
                {
                    test.AddLogEntry($" vol = {vol}");
                }
                return curmetadata;
            }

            public void Dispose()
            {
            }
        }

        [TestMethod]
        public async Task TestSinglesInsert1File()
        {
            var failMessage = string.Empty;
            var ev = new ManualResetEventSlim();
            var c = CreateExecutionContext();
            await c.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    foreach (var fileToAdd in new[] { "12th Street", "Black Cat", "Freckles" }) // insert before, between and after 14,40
                    {
                        AddLogEntry($"Doing = {fileToAdd}");
                        int num = 0;
                        using (var helper = new SinglesTesthelper(this, (str) =>
                        {
                            var include = false;
                            if (str.Contains("Crazy Bone") || str.Contains("Banana Peel"))
                            {
                                include = true;
                            }
                            num++;
                            return include;
                        }))
                        {
                            var curmetadata = await helper.CreateAndSaveBmkAsync();
                            DumpVolAndToc("Start", curmetadata);
                            Assert.AreEqual(2, curmetadata.lstVolInfo.Count, $"{fileToAdd}");
                            Assert.AreEqual(2, curmetadata.lstTocEntries.Count, $"{fileToAdd}");
                            if (fileToAdd == "12th Street")
                            {
                                Assert.AreEqual("Crazy Bone Rag", curmetadata.lstTocEntries[1].SongName, $"{fileToAdd}");
                                Assert.AreEqual(3, curmetadata.lstTocEntries[1].PageNo, $"{fileToAdd}");
                            }

                            var fileToAddFullPath = helper.sourceFiles.Where(f => f.Contains(fileToAdd)).First();
                            AddLogEntry($"Adding file  = {fileToAddFullPath}");
                            File.Copy(fileToAddFullPath,
                                Path.Combine(helper.singlesFolder, Path.GetFileName(fileToAddFullPath))
                                );
                            (var lstPdfMetaFileData, var lstFolders) = await PdfMetaData.LoadAllPdfMetaDataFromDiskAsync(helper.pdfViewerWindow._RootMusicFolder);
                            Assert.AreEqual(1, lstPdfMetaFileData.Count, $"{fileToAdd}");
                            curmetadata = lstPdfMetaFileData[0];
                            DumpVolAndToc("Done", curmetadata);
                            Assert.AreEqual(3, curmetadata.lstVolInfo.Count, $"{fileToAdd}");
                            Assert.AreEqual(3, curmetadata.lstTocEntries.Count, $"{fileToAdd}");
                            var lastone = string.Empty;
                            foreach (var vol in curmetadata.lstVolInfo)
                            {
                                Assert.IsTrue(string.Compare(lastone, vol.FileNameVolume) < 0, $" nskip {fileToAdd} sequence {lastone} {vol}");
                                lastone = vol.FileNameVolume;
                            }
                            if (fileToAdd == "12th Street" || fileToAdd == "Black Cat")
                            {
                                Assert.AreEqual("Crazy Bone Rag", curmetadata.lstTocEntries[2].SongName, $"{fileToAdd}");
                                Assert.AreEqual(6, curmetadata.lstTocEntries[2].PageNo, $"{fileToAdd}");
                            }
                            else
                            {
                                Assert.AreEqual("Freckles Rag", curmetadata.lstTocEntries[2].SongName, $"{fileToAdd}");
                                Assert.AreEqual(6, curmetadata.lstTocEntries[2].PageNo, $"{fileToAdd}");
                            }
                            curmetadata.SaveIfDirty(ForceDirty: true);
                            AddLogEntry(File.ReadAllText(curmetadata.PdfBmkMetadataFileName));
                        }
                    }
                }
                catch (Exception ex)
                {
                    failMessage = ex.ToString();
                }
                AddLogEntry("set event");
                ev.Set();
            });
            ev.Wait();
            AddLogEntry("Done wait event");
            Assert.IsTrue(string.IsNullOrEmpty(failMessage), failMessage);
        }
        void DumpVolAndToc(string description, PdfMetaData curMetaData)
        {
            AddLogEntry($"{description}");
            foreach (var vol in curMetaData.lstVolInfo)
            {
                AddLogEntry($"  Vol {vol}");
            }
            foreach (var toc in curMetaData.lstTocEntries)
            {
                AddLogEntry($"  TOC {toc}");
            }
            if (curMetaData.dictFav.Count != curMetaData.Favorites.Count)
            {
                AddLogEntry($"dictFav != Fav {curMetaData.dictFav.Count}  {curMetaData.Favorites.Count}");
            }
            foreach (var fav in curMetaData.dictFav)
            {
                AddLogEntry($"  Fav {fav}");
            }
        }

        [TestMethod]
        public async Task TestSinglesPreserveFavAndInk()
        {
            var failMessage = string.Empty;
            var ev = new ManualResetEventSlim();
            var c = CreateExecutionContext();
            await c.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    foreach (var fileToAdd in new[] { "12th Street", "Black Cat", "Freckles" }) // insert before, between and after 14,40
                    {
                        AddLogEntry($"Doing = {fileToAdd}");
                        int num = 0;
                        using (var helper = new SinglesTesthelper(this, (str) =>
                        {
                            var include = false;
                            if (str.Contains("Crazy Bone") || str.Contains("Banana Peel"))
                            {
                                include = true;
                            }
                            num++;
                            return include;
                        }))
                        {
                            var strComposer = "Charles L Johnson GREAT COMPOSER GREAT COMPOSER GREAT COMPOSER";
                            {
                                var curmetadata = await helper.CreateAndSaveBmkAsync();

                                Assert.AreEqual(2, curmetadata.lstVolInfo.Count);
                                Assert.AreEqual(2, curmetadata.lstTocEntries.Count);
                                for (int i = 0; i < curmetadata.MaxPageNum; i += 2)
                                {
                                    var volno = curmetadata.GetVolNumFromPageNum(i);
                                    curmetadata.ToggleFavorite(i, IsFavorite: true, FavoriteName: curmetadata.lstVolInfo[volno].FileNameVolume);
                                }
                                DumpVolAndToc("Start", curmetadata);
                                curmetadata.lstTocEntries.Where(t => t.SongName.Substring(0, 5) == "Crazy").First().Composer = strComposer;
                                curmetadata.SaveIfDirty(ForceDirty: true);
                                var fileToAddFullPath = helper.sourceFiles.Where(f => f.Contains(fileToAdd)).First();
                                AddLogEntry($"Adding file  = {fileToAddFullPath}");
                                File.Copy(fileToAddFullPath,
                                    Path.Combine(helper.singlesFolder, Path.GetFileName(fileToAddFullPath))
                                    );
                            }
                            {
                                (var lstPdfMetaFileData, var lstFolders) = await PdfMetaData.LoadAllPdfMetaDataFromDiskAsync(helper.pdfViewerWindow._RootMusicFolder);
                                Assert.AreEqual(1, lstPdfMetaFileData.Count);
                                var curmetadata = lstPdfMetaFileData[0];
                                DumpVolAndToc("Done", curmetadata);
                                Assert.AreEqual(3, curmetadata.lstVolInfo.Count);
                                Assert.AreEqual(3, curmetadata.lstTocEntries.Count);
                                Assert.AreEqual(strComposer, curmetadata.lstTocEntries.Where(t => t.SongName.Substring(0, 5) == "Crazy").First().Composer);
                                var lastone = string.Empty;
                                foreach (var vol in curmetadata.lstVolInfo)
                                {
                                    Assert.IsTrue(string.Compare(lastone, vol.FileNameVolume) < 0, $"sequence {lastone} {vol}");
                                    lastone = vol.FileNameVolume;
                                }
                                Assert.AreEqual("Crazy Bone Rag.pdf", curmetadata.dictFav.Values.ToList()[2].FavoriteName);
                                curmetadata.SaveIfDirty(ForceDirty: true);
                                AddLogEntry(File.ReadAllText(curmetadata.PdfBmkMetadataFileName));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    failMessage = ex.ToString();
                }
                AddLogEntry("set event");
                ev.Set();
            });
            ev.Wait();
            AddLogEntry("Done wait event");
            Assert.IsTrue(string.IsNullOrEmpty(failMessage), failMessage);
        }


        [TestMethod]
        [Ignore]
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
            AddLogEntry($"Got PDFMetaDataCnt={w.lstPdfMetaFileData.Count}");
            foreach (var pdfMetaData in w.lstPdfMetaFileData)
            {
                var bmi = pdfMetaData.GetBitmapImageThumbnailAsync();
                pdfMetaData.bitmapImageCache = null;
                //                AddLogEntry($" {pdfMetaData} {bmi.PixelWidth} {bmi.PixelHeight}");
            }
            //var classicalPdf = w.lstPdfMetaFileData[0];
            // with no renderoptions,wh=(794,1122), pixelHeight= (1589, 2245 )
            // with renderops = 150,225 wh= (225,150), pixelhw = (300, 450), dpix = dpiy = 192
            //            var bmi = classicalPdf.GetBitmapImageThumbnail();

        }
    }
}
