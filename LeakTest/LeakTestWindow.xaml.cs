using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;
using SheetMusicViewer;

namespace LeakTest
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        readonly Random _Rand = new Random();
        readonly string root1 = @"c:\Sheetmusic";
        readonly string root2 = @"f:\Sheetmusic";
        string Rootfolder { get { if (Directory.Exists(root1)) { return root1; } return root2; } }
        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var w = new SheetMusicViewer.PdfViewerWindow();
            w.ShowDialog();
            Environment.Exit(0);

        }

        private async void MainWindow_Loadedtry(object sender, RoutedEventArgs e)
        {
            var w = new SheetMusicViewer.PdfViewerWindow
            {
                //                    _RootMusicFolder = Path.Combine(Rootfolder, "FakeBooks")
                _RootMusicFolder = Rootfolder
            };

            (var lstMetaData, var _) = await PdfMetaData.LoadAllPdfMetaDataFromDiskAsync(w._RootMusicFolder);
            foreach (var currentPdfMetaData in lstMetaData.Where(p => p.GetFullPathFileFromVolno(volNo: 0).Contains("Classical Fake")))
            {
                var sw = Stopwatch.StartNew();
                //                    var currentPdfMetaData = lstMetaData.Where(m => m.GetFullPathFile(volNo: 0).Contains("Fake")).First();
                w.currentPdfMetaData = currentPdfMetaData;
                w.currentPdfMetaData.InitializeListPdfDocuments();
                w.ShowDialog();
                //for (int iter = 0; iter < 100; iter++)
                //{
                //    //var cacheEntry = PdfViewerWindow.CacheEntry.TryAddCacheEntry(mpdf.PageNumberOffset);
                //    //await cacheEntry.task;
                //    //// calling thread must be STA, UIThread
                //    //var res = cacheEntry.task.Result;
                //    var pageNo = 0;
                //    for (pageNo = currentPdfMetaData.PageNumberOffset; pageNo < currentPdfMetaData.NumPagesInSet + currentPdfMetaData.PageNumberOffset - 1; pageNo++)
                //    {
                //        var cacheEntry = w._pageCache.TryAddCacheEntry(pageNo);
                //        await cacheEntry.task;
                //        var bmi = cacheEntry.task.Result;
                //        this.Content = new Image() { Source = bmi };
                //        this.Title = $"{cnt++} {pageNo,8}   bmi={bmi.Width:n0}, {bmi.Height:n0}  {sw.Elapsed.TotalSeconds,8:n4} {currentPdfMetaData} ";
                //        //break;
                //    }
                //}
            }
        }

        private async void MainWindow_LoadedGood(object sender, RoutedEventArgs e)
        {
            int nPages = 0;
            var dpPage = new DockPanel();
            this.Content = dpPage;
            foreach (var pdfFile in Directory.GetFiles(Rootfolder, "*.pdf", SearchOption.AllDirectories))
            {
                var f = await StorageFile.GetFileFromPathAsync(pdfFile);
                var pdfDoc = await PdfDocument.LoadFromFileAsync(f);
                for (int i = 0; i < pdfDoc.PageCount; i++)
                {
                    using (var pdfPage = pdfDoc.GetPage((uint)i))
                    {
                        var bmi = new BitmapImage();
                        using (var strm = new InMemoryRandomAccessStream())
                        {
                            var rect = pdfPage.Dimensions.ArtBox;
                            var renderOpts = new PdfPageRenderOptions
                            {
                                DestinationWidth = (uint)(rect.Width + 100 * (_Rand.Next(100) - 100)),
                                DestinationHeight = (uint)(rect.Height + 200 * (_Rand.Next(200) - 100))
                            };
                            if (pdfPage.Rotation != PdfPageRotation.Normal)
                            {
                                renderOpts.DestinationHeight = (uint)rect.Width;
                                renderOpts.DestinationWidth = (uint)rect.Height;
                            }
                            await pdfPage.RenderToStreamAsync(strm, renderOpts);
                            bmi.BeginInit();
                            bmi.StreamSource = strm.AsStream();
                            bmi.CacheOption = BitmapCacheOption.OnLoad;
                            bmi.Rotation = (Rotation)(i % 4);
                            bmi.EndInit();
                            //var img = new Image()
                            //{
                            //    Source= bmi
                            //};
                            var imb = new ImageBrush(bmi);
                            var inkc = new InkCanvas()
                            {
                                Background = imb,
                                EditingMode = InkCanvasEditingMode.None
                            };
                            dpPage.Children.Clear();
                            dpPage.Children.Add(inkc);
                            //                            this.Content = inkc;
                        }

                    }
                    this.Title = $"Pages = {nPages++} Curpg={i} {System.IO.Path.GetFileNameWithoutExtension(pdfFile)}";
                }
            }

        }
    }
}
