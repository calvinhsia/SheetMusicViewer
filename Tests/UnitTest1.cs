﻿using System;
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
using SheetMusicViewer;
using static SheetMusicViewer.PdfViewerWindow;
using Windows.Storage;
using System.Reflection;
using System.Windows.Markup;
using System.Collections.ObjectModel;
using System.Runtime.ConstrainedExecution;
using System.Windows.Controls.Primitives;

namespace Tests
{
    public class TestBase
    {
        /// <summary>
        /// Creates a custom STA thread on which UI elements can run. Has execution context that allows asynchronous code to work
        /// </summary>
        public static async Task RunInSTAExecutionContextAsync(Func<Task> actionAsync, string description = "", int maxStackSize = 512 * 1024)
        {
            Dispatcher mySTADispatcher = null;
            var tcsGetExecutionContext = new TaskCompletionSource<int>();
            //            var tcsStaThreadDone = new TaskCompletionSource<int>();
            var myStaThread = new Thread(() =>
            {
                mySTADispatcher = Dispatcher.CurrentDispatcher;
                var syncContext = new DispatcherSynchronizationContext(mySTADispatcher); // Create/install the context
                SynchronizationContext.SetSynchronizationContext(syncContext);
                tcsGetExecutionContext.SetResult(0);// notify that sync context is ready
                try
                {
                    Dispatcher.Run();  // Start the Dispatcher Processing
                }
                catch (ThreadAbortException)
                {
                }
                catch (Exception) { }
                Debug.WriteLine($"Thread done {description}");
                //                tcsStaThreadDone.SetResult(0);
            }, maxStackSize: maxStackSize)
            {
                IsBackground = true,
                Name = $"MySta{description}" // can be called from within the same context (e.g. a prog bar) so distinguish thread names
            };
            myStaThread.SetApartmentState(ApartmentState.STA);
            myStaThread.Start();
            await tcsGetExecutionContext.Task; // wait for thread to set up STA sync context
            var tcsCallerAction = new TaskCompletionSource<int>();
            if (mySTADispatcher == null)
            {
                throw new NullReferenceException(nameof(mySTADispatcher));
            }

            await mySTADispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await actionAsync();
                }
                catch (Exception ex)
                {
                    tcsCallerAction.SetException(ex);
                    return;
                }
                finally
                {
                    Debug.WriteLine($"User code done. Shutting down dispatcher {description}");
                    mySTADispatcher.InvokeShutdown();
                }
                //              await tcsStaThreadDone.Task; // wait for STA thread to exit
                Debug.WriteLine($"StaThreadTask done");
                tcsCallerAction.SetResult(0);
            });
            await tcsCallerAction.Task;
            Debug.WriteLine($"sta thread finished {description}");
        }
        public TestContext TestContext { get; set; }
        [TestInitialize]
        public void TestInitialize()
        {
            TestContext.WriteLine($"{DateTime.Now} Starting test {TestContext.TestName}");
        }
        public static string GetSheetMusicFolder()
        {
            var folder = @"C:\Users\calvinh\OneDrive";
            if (!Directory.Exists(folder))
            {
                folder = @"d:\OneDrive";
            }
            return $@"{folder}\SheetMusic";

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
        //string testbmk = @"{GetOneDriveFolder()}\Documents\SheetMusic\FakeBooks\The Ultimate Pop Rock Fake Book.bmk";
        //        readonly string testPdf = @"C:\SheetMusic\FakeBooks\The Ultimate Pop Rock Fake Book.pdf";

        [TestMethod]
        [Ignore]
        public async Task TestExecContext()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                var w = new SheetMusicViewer.PdfViewerWindow
                {
                    _RootMusicFolder = GetSheetMusicFolder()
                };
                (var lstMetaData, var _) = await PdfMetaData.LoadAllPdfMetaDataFromDiskAsync(w._RootMusicFolder);
                var x = lstMetaData;
                w.ShowDialog();
            });
        }
        [TestMethod]
        public async Task SliderTest()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                await Task.Yield();
                var spXaml = @"
        <StackPanel x:Name=""sp"" Orientation = ""Vertical"">
            <TextBlock x:Name=""tbSlider""/>
            <Slider x:Name=""MySlider"" Maximum = ""100""/>
        </StackPanel>
";
//                var spXamlBindingWorks = @"
//        <StackPanel x:Name=""sp"" Orientation = ""Vertical"">
//            <TextBlock x:Name=""tbSlider"" Text = ""{Binding ElementName=MySlider, Path=Value, UpdateSourceTrigger=PropertyChanged}""/>
//            <Slider x:Name=""MySlider"" Maximum = ""100""/>
//        </StackPanel>
//";
                var strxaml =
    $@"<Grid
xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
xmlns:l=""clr-namespace:{this.GetType().Namespace};assembly={System.IO.Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location)}"" 
        Margin=""5,5,5,5"">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width = ""200""/>
            <ColumnDefinition Width = ""3""/>
            <ColumnDefinition Width = ""*""/>
        </Grid.ColumnDefinitions>
        <ListView x:Name=""lvData""/>
        <GridSplitter Grid.Column = ""1"" HorizontalAlignment=""Center"" VerticalAlignment=""Stretch"" Width = ""3"" Background=""LightBlue""/>
        <DockPanel Grid.Column=""2"">
        {spXaml}

        </DockPanel>
    </Grid>
";
                var grid = (Grid)XamlReader.Parse(strxaml);
                var slider = (Slider)grid.FindName("MySlider");
                var tbSlider = (TextBlock)grid.FindName("tbSlider");
                Popup popupSliderValue = null;
                TextBlock tbPopup = null;
                slider.ValueChanged+= (o, e) =>
                {
                    tbSlider.Text = $"VC {slider.Value}";
                    tbPopup.Text = $"VC {slider.Value}";
                };
                slider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((o, e) =>
                {
                    popupSliderValue = new Popup()
                    {
                        PlacementTarget = slider,
                        Placement = PlacementMode.Relative,
                        HorizontalOffset = -100,
                        VerticalOffset = 20
                    };
                    popupSliderValue.IsOpen = true;
                    popupSliderValue.Visibility = Visibility.Visible;
                    //windSliderValue.Show();
                    tbPopup = new TextBlock() { Text = $"DragStarted {slider.Value}", Foreground =System.Windows.Media.Brushes.Black };
                    var border = new Border()
                    {
                        BorderThickness = new Thickness(1), 
                        Background = System.Windows.Media.Brushes.LightYellow
                    };
                    border.Child = tbPopup;
                    popupSliderValue.Child = border;
                    popupSliderValue.BringIntoView();
                    //windSliderValue.Content = tbSliderValueWindow;
                    tbSlider.Text = $"DragStarted {slider.Value}";
                }), handledEventsToo: true);
                slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((o, e) =>
                {
                    tbSlider.Text = $"DragCompleted {slider.Value}";
                    //windSliderValue.Close();
                    popupSliderValue.IsOpen = false;
                    popupSliderValue = null;
                }), handledEventsToo: true);
                var w = new Window();
                w.Content = grid;
                w.Show();
                await Task.Delay(10000);

            });
        }



        [TestMethod]
        [Ignore]
        public async Task TestUpdaateBmkWriteTime()
        {
            var tcsStaThread = new TaskCompletionSource<int>();
            var c = CreateExecutionContext(tcsStaThread);
            await c.Dispatcher.InvokeAsync(async () =>
            {
                var w = new SheetMusicViewer.PdfViewerWindow
                {
                    _RootMusicFolder = GetSheetMusicFolder()
                };
                (var lstMetaData, var _) = await PdfMetaData.LoadAllPdfMetaDataFromDiskAsync(w._RootMusicFolder);
                foreach (var itm in lstMetaData)
                {
                    //                        itm.SaveIfDirty(ForceDirty: true);
                }
                c.Dispatcher.InvokeShutdown();
                AddLogEntry("Done all");
            });
            await tcsStaThread.Task;
        }

        [TestMethod]
        [Ignore]
        public async Task TestStress()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                var w = new SheetMusicViewer.PdfViewerWindow
                {
                    _RootMusicFolder = GetSheetMusicFolder()
                };
                (var lstMetaData, var _) = await PdfMetaData.LoadAllPdfMetaDataFromDiskAsync(w._RootMusicFolder);
                var testw = new Window();
                var ctsDone = new CancellationTokenSource();
                testw.Closed += (o, e) =>
                {
                    ctsDone.Cancel();
                };
                testw.Show();
                try
                {
                    foreach (var currentPdfMetaData in lstMetaData)
                    {
                        AddLogEntry($"Starting {currentPdfMetaData}");
                        w.Title = currentPdfMetaData.ToString();
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
                                ctsDone.Token.ThrowIfCancellationRequested();
                                var (pdfDoc, pdfPgno) = await currentPdfMetaData.GetPdfDocumentForPageno(pageNo);
                                //                    var pdfPgNo = currentPdfMetaData.GetPdfVolPageNo(pageNo + i);
                                if (pdfDoc != null && pdfPgno >= 0 && pdfPgno < pdfDoc.PageCount)
                                {
                                    using var pdfPage = pdfDoc.GetPage((uint)(pdfPgno));
                                    using var strm = new InMemoryRandomAccessStream();
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
                                    var sp = new StackPanel() { Orientation = Orientation.Vertical };
                                    sp.Children.Add(new TextBlock()
                                    {
                                        Text = $"{currentPdfMetaData} {pdfPgno}"
                                    });
                                    sp.Children.Add(new Image() { Source = bmi, Stretch = System.Windows.Media.Stretch.None });
                                    testw.Content = sp;

                                    if (pdfPage.Rotation != PdfPageRotation.Rotate270 && pdfPage.Rotation != PdfPageRotation.Rotate90)
                                    {
                                        AddLogEntry($"got page {pageNo,5}   strms={strm.Size,10:n0} {pdfPage.Rotation,10} {rect}  {currentPdfMetaData} ");
                                    }
                                }
                                //                                await Task.Delay(TimeSpan.FromSeconds(2));
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                }
                AddLogEntry("Done all");
            });
            AddLogEntry("Done all..exit test");
        }
        [TestMethod]
        [Ignore]
        public async Task TestStressOnePage()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                var pdfFileName = $@"{GetSheetMusicFolder()}\Pop\PopSingles\Be Our Guest - G Major - MN0174098.pdf"; var pageNo = 1;

                //var pdfFileName = $@"{GetOneDriveFolder()}\SheetMusic\Pop\PopSingles\Bohemian Rhapsody - Bb Major.pdf";
                //var pdfFileName = $@"{GetOneDriveFolder()}\SheetMusic\Pop\PopSingles\HisTheme.pdf";
                //var pdfFileName = $@"{GetOneDriveFolder()}\SheetMusic\Ragtime\Collections\Best of Ragtime.pdf";                var pageNo = 2;
                using var fstrm = await FileRandomAccessStream.OpenAsync(pdfFileName, FileAccessMode.Read);
                var pdfDoc = await PdfDocument.LoadFromStreamAsync(fstrm);

                var testw = new Window()
                {
                    WindowState = WindowState.Maximized
                };
                var ctsDone = new CancellationTokenSource();
                testw.Closed += (o, e) =>
                {
                    ctsDone.Cancel();
                };
                testw.Show();
                try
                {
                    using var pdfPage = pdfDoc.GetPage((uint)(pageNo));
                    var rect = pdfPage.Dimensions.ArtBox;
                    var renderOpts = new PdfPageRenderOptions()
                    {
                        DestinationWidth = (uint)rect.Width,
                        DestinationHeight = (uint)rect.Height,
                        SourceRect = new Windows.Foundation.Rect(0, 0, rect.Width, rect.Height)
                    };
                    if (pdfPage.Rotation != PdfPageRotation.Normal)
                    {
                        renderOpts.DestinationHeight = (uint)rect.Width;
                        renderOpts.DestinationWidth = (uint)rect.Height;
                    }
                    var ctr = 0;
                    var dictCheckSums = new Dictionary<ulong, int>(); // chksum=>cnt of chksum
                    while (true)
                    {
                        ctsDone.Token.ThrowIfCancellationRequested();

                        using var strm = new InMemoryRandomAccessStream();
                        await pdfPage.RenderToStreamAsync(strm, renderOpts);
                        await strm.FlushAsync();
                        var chksum = 0UL;
                        var st = strm.AsStream();
                        var bytes = new byte[st.Length];
                        st.Read(bytes, 0, (int)st.Length);
                        Array.ForEach(bytes, (b) => { chksum += b; });
                        dictCheckSums[chksum] = dictCheckSums.TryGetValue(chksum, out var val) ? val + 1 : 1;

                        var bmi = new BitmapImage();
                        bmi.BeginInit();
                        bmi.StreamSource = strm.CloneStream().AsStream();
                        bmi.Rotation = Rotation.Rotate0;
                        bmi.CacheOption = BitmapCacheOption.OnLoad;
                        bmi.EndInit();
                        var sp = new StackPanel() { Orientation = Orientation.Vertical };
                        sp.Children.Add(new TextBlock()
                        {
                            Text = $"{pdfFileName} {pageNo} {ctr++,5}  # unique checksums = {dictCheckSums.Count} CurChkSum {chksum}"
                        });
                        sp.Children.Add(new Image() { Source = bmi, Stretch = System.Windows.Media.Stretch.None });
                        testw.Content = sp;
                        //                        await Task.Delay(TimeSpan.FromMilliseconds(200));
                    }
                }
                catch (OperationCanceledException)
                {
                    AddLogEntry($"OpCancelled");
                }
                AddLogEntry("Done all");
            });
            AddLogEntry("Done all..exit test");
        }


        [TestMethod]
        [Ignore]
        public async Task TestStressOnePageMultiChecksum()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                //var pdfFileName = $@"{GetOneDriveFolder()}\SheetMusic\Pop\PopSingles\Be Our Guest - G Major - MN0174098.pdf";
                //pdfFileName = $@"{GetOneDriveFolder()}\SheetMusic\Pop\PopSingles\Bohemian Rhapsody - Bb Major.pdf";
                //pdfFileName = $@"{GetOneDriveFolder()}\SheetMusic\Pop\PopSingles\HisTheme.pdf";
                //pdfFileName = $@"{GetOneDriveFolder()}\SheetMusic\Ragtime\Collections\Best of Ragtime.pdf";
                var testw = new Window();
                var ctsDone = new CancellationTokenSource();
                testw.Closed += (o, e) =>
                {
                    ctsDone.Cancel();
                };
                var strxaml =
    $@"<Grid
xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
xmlns:l=""clr-namespace:{this.GetType().Namespace};assembly={System.IO.Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location)}"" 
        Margin=""5,5,5,5"">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width = ""200""/>
            <ColumnDefinition Width = ""3""/>
            <ColumnDefinition Width = ""*""/>
        </Grid.ColumnDefinitions>
        <ListView x:Name=""lvData""/>
        <GridSplitter Grid.Column = ""1"" HorizontalAlignment=""Center"" VerticalAlignment=""Stretch"" Width = ""3"" Background=""LightBlue""/>
        <DockPanel Grid.Column=""2"" x:Name=""dpPDF""/>
    </Grid>
";
                var grid = (System.Windows.Controls.Grid)(XamlReader.Parse(strxaml));
                var dpPDF = (DockPanel)grid.FindName("dpPDF");
                var lvData = (ListView)grid.FindName("lvData");
                testw.Content = grid;
                testw.Show();
                var folder = $@"{GetSheetMusicFolder()}\Ragtime\Collections";
                var lstdata = new ObservableCollection<string>();
                lvData.ItemsSource = lstdata;
                foreach (var pdfFileName in Directory.EnumerateFiles(folder, "*.pdf"))
                {
                    using var fstrm = await FileRandomAccessStream.OpenAsync(pdfFileName, FileAccessMode.Read);
                    var pdfDoc = await PdfDocument.LoadFromStreamAsync(fstrm);

                    var numIterPerPage = 100;
                    try
                    {
                        for (var pageNo = 0; pageNo < pdfDoc.PageCount; pageNo++)
                        {
                            var dictCheckSums = new Dictionary<ulong, int>(); // chksum=>cnt of chksum
                            using var pdfPage = pdfDoc.GetPage((uint)(pageNo));
                            using var strm = new InMemoryRandomAccessStream();
                            var rect = pdfPage.Dimensions.ArtBox;
                            var renderOpts = new PdfPageRenderOptions()
                            {
                                DestinationWidth = (uint)rect.Width,
                                DestinationHeight = (uint)rect.Height,
                                SourceRect = new Windows.Foundation.Rect(0, 0, rect.Width, rect.Height)
                            };
                            for (var ctr = 0; ctr < numIterPerPage; ctr++)
                            {
                                ctsDone.Token.ThrowIfCancellationRequested();

                                await pdfPage.RenderToStreamAsync(strm, renderOpts);
                                await strm.FlushAsync();
                                var chksum = 0UL;
                                var st = strm.AsStream();
                                var bytes = new byte[st.Length];
                                st.Read(bytes, 0, (int)st.Length);
                                Array.ForEach(bytes, (b) => { chksum += b; });
                                dictCheckSums[chksum] = dictCheckSums.TryGetValue(chksum, out var val) ? val + 1 : 1;

                                //var bmi = new BitmapImage();
                                //bmi.BeginInit();
                                //bmi.StreamSource = strm.CloneStream().AsStream();
                                //bmi.Rotation = Rotation.Rotate0;
                                //bmi.CacheOption = BitmapCacheOption.OnLoad;
                                //bmi.EndInit();
                                var sp = new StackPanel() { Orientation = Orientation.Vertical };
                                sp.Children.Add(new TextBlock()
                                {
                                    Text = $"{pdfFileName}({pageNo}/{pdfDoc.PageCount}) {ctr,5} #chk={dictCheckSums.Count} Chk={chksum:n0} "
                                });
                                //sp.Children.Add(new Image() { Source = bmi, Stretch = System.Windows.Media.Stretch.None });
                                dpPDF.Children.Clear();
                                dpPDF.Children.Add(sp);
                            }
                            if (dictCheckSums.Count >= 1)
                            {
                                var str = $"{dictCheckSums.Count} {Path.GetFileName(pdfFileName)}({pageNo}/{pdfDoc.PageCount})";
                                lstdata.Add(str);
                                AddLogEntry(str);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
                AddLogEntry("Done all");
            });
            AddLogEntry("Done all..exit test");
        }


        [TestMethod]
        [Ignore]
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
            var tcsStaThread = new TaskCompletionSource<int>();
            var c = CreateExecutionContext(tcsStaThread);
            await c.Dispatcher.InvokeAsync((Func<Task>)(async () =>
            {
                var w = new global::SheetMusicViewer.PdfViewerWindow(GetSheetMusicFolder(), UseSettings: false)
                {
                    //                    _RootMusicFolder = Path.Combine(GetSheetMusicFolder(), "FakeBooks")
                    IsTesting = true
                };
                w.Show();
                (var lstMetaData, var _) = await SheetMusicViewer.PdfMetaData.LoadAllPdfMetaDataFromDiskAsync((string)w._RootMusicFolder);
                foreach (var currentPdfMetaData in Enumerable.Where<SheetMusicViewer.PdfMetaData>(lstMetaData, (Func<SheetMusicViewer.PdfMetaData, bool>)(d => (bool)d._FullPathFile.Contains((string)@"Ragtime\Singles"))))
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
            }));
            ev.Wait();
        }

        private MyExecutionContext CreateExecutionContext(TaskCompletionSource<int> tcsStaThread)
        {
            const string Threadname = "MySTAThread";
            var tcs = new TaskCompletionSource<MyExecutionContext>();

            var mySTAThread = new Thread(() =>
            {
                // Create the context, and install it:
                var dispatcher = Dispatcher.CurrentDispatcher;
                var syncContext = new DispatcherSynchronizationContext(dispatcher);

                SynchronizationContext.SetSynchronizationContext(syncContext);

                tcs.SetResult(new MyExecutionContext
                {
                    DispatcherSynchronizationContext = syncContext,
                    Dispatcher = dispatcher
                });

                // Start the Dispatcher Processing
                AddLogEntry($"{Threadname}  dispatcher run");
                Dispatcher.Run();
                AddLogEntry($"{Threadname} dispatcher done");
                tcsStaThread.SetResult(0);
            });

            mySTAThread.SetApartmentState(ApartmentState.STA);
            mySTAThread.Name = Threadname;
            AddLogEntry($"{Threadname} start");
            mySTAThread.Start();

            return tcs.Task.Result;
        }

        internal class MyExecutionContext
        {
            public DispatcherSynchronizationContext DispatcherSynchronizationContext { get; set; }
            public Dispatcher Dispatcher { get; set; }
        }

        [TestMethod]
        [Ignore]
        public async Task TestReadBmkData()
        {
            var w = new SheetMusicViewer.PdfViewerWindow
            {
                _RootMusicFolder = GetSheetMusicFolder()
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
        [Ignore]
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
                var sourceFiles = Directory.GetFiles(Path.Combine(GetSheetMusicFolder(), @"ragtime\singles"), "*.pdf").OrderBy(p => p);
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
                sourceFiles = Directory.GetFiles(Path.Combine(GetSheetMusicFolder(), @"ragtime\singles"), "*.pdf").OrderBy(p => p);
                foreach (var file in sourceFiles.OrderBy(f => f).Where(f => funcFilter(f)))
                {
                    var dest = Path.Combine(singlesFolder, Path.GetFileName(file));
                    File.Copy(file, dest);
                }
                pdfViewerWindow = new SheetMusicViewer.PdfViewerWindow
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
        [Ignore]
        public async Task TestSinglesInsert1File()
        {
            var failMessage = string.Empty;
            var ev = new ManualResetEventSlim();
            var tcsStaThread = new TaskCompletionSource<int>();
            var c = CreateExecutionContext(tcsStaThread);
            await c.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    foreach (var fileToAdd in new[] { "12th Street", "Black Cat", "Freckles" }) // insert before, between and after 14,40
                    {
                        AddLogEntry($"Doing = {fileToAdd}");
                        int num = 0;
                        using var helper = new SinglesTesthelper(this, (str) =>
                        {
                            var include = false;
                            if (str.Contains("Crazy Bone") || str.Contains("Banana Peel"))
                            {
                                include = true;
                            }
                            num++;
                            return include;
                        });
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
        [Ignore]
        public async Task TestSinglesPreserveFavAndInk()
        {
            var failMessage = string.Empty;
            var ev = new ManualResetEventSlim();
            var tcsStaThread = new TaskCompletionSource<int>();
            var c = CreateExecutionContext(tcsStaThread);
            await c.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    foreach (var fileToAdd in new[] { "12th Street", "Black Cat", "Freckles" }) // insert before, between and after 14,40
                    {
                        AddLogEntry($"Doing = {fileToAdd}");
                        int num = 0;
                        using var helper = new SinglesTesthelper(this, (str) =>
                        {
                            var include = false;
                            if (str.Contains("Crazy Bone") || str.Contains("Banana Peel"))
                            {
                                include = true;
                            }
                            num++;
                            return include;
                        });
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
                            curmetadata.lstTocEntries.Where(t => t.SongName[..5] == "Crazy").First().Composer = strComposer;
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
                            Assert.AreEqual(strComposer, curmetadata.lstTocEntries.Where(t => t.SongName[..5] == "Crazy").First().Composer);
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
            var w = new SheetMusicViewer.PdfViewerWindow
            {
                _RootMusicFolder = GetSheetMusicFolder()
            };
            await PdfMetaData.LoadAllPdfMetaDataFromDiskAsync(w._RootMusicFolder);
            for (int i = 0; i < 11; i++)
            {
                GetBMPs(w);
            }
        }
        void GetBMPs(SheetMusicViewer.PdfViewerWindow w)
        {
            AddLogEntry($"Got PDFMetaDataCnt={w.lstPdfMetaFileData.Count}");
            foreach (var pdfMetaData in w.lstPdfMetaFileData)
            {
                _ = pdfMetaData.GetBitmapImageThumbnailAsync();
                pdfMetaData.bitmapImageCache = null;
                //                AddLogEntry($" {pdfMetaData} {bmi.PixelWidth} {bmi.PixelHeight}");
            }
            //var classicalPdf = w.lstPdfMetaFileData[0];
            // with no renderoptions,wh=(794,1122), pixelHeight= (1589, 2245 )
            // with renderops = 150,225 wh= (225,150), pixelhw = (300, 450), dpix = dpiy = 192
            //            var bmi = classicalPdf.GetBitmapImageThumbnail();

        }

        [TestMethod]
        [Ignore]
        public async Task TestMainWindow()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                await Task.Yield();
                var ctsDone = new CancellationTokenSource();
                var mainwindow = new PdfViewerWindow(rootFolderForTesting: null, UseSettings: false);
                mainwindow.Closed += (o, e) =>
                {
                    ctsDone.Cancel();
                };
                mainwindow.ShowDialog();

            });
        }
    }
}
