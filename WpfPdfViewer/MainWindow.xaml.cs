using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Printing;
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

namespace WpfPdfViewer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_LoadedAsync;
        }
        //public ObservableCollection<BitmapImage> PdfPages { get; set; } = new ObservableCollection<BitmapImage>();

        private async void MainWindow_LoadedAsync(object sender, RoutedEventArgs e)
        {
            // https://blog.pieeatingninjas.be/2016/02/06/displaying-pdf-files-in-a-uwp-app/
            // https://blogs.windows.com/buildingapps/2017/01/25/calling-windows-10-apis-desktop-application/#RWYkd5C4WTeEybol.97
            try
            {
                var docvwr = new DocumentViewer();
                var fixedDoc = new FixedDocument();
                docvwr.Document = fixedDoc;

                var titlePage = @"C:\Users\calvinh\OneDrive\Documents\SheetMusic\FakeBooks\ulimatePopRockCFaceUp\Ultimate Pop Rock Fake Book.pdf";
                var fTitle = await StorageFile.GetFileFromPathAsync(titlePage);
                var pdfDocTitle = await PdfDocument.LoadFromFileAsync(fTitle);
                var pgTitle = pdfDocTitle.GetPage(0);
                var rect = pgTitle.Dimensions.ArtBox;
                await AddPageToDoc(fixedDoc, pgTitle);

                var pdfDataFileOrig = @"C:\Users\calvinh\OneDrive\Documents\SheetMusic\FakeBooks\Ultimate Pop Rock Fake Book 1.pdf";
                var rotation = Rotation.Rotate180;
                //pdfSourceDoc = @"C:\Users\calvinh\OneDrive\Documents\SheetMusic\Ragtime\Collections\The Music of James Scott001.pdf";
                //rotation = Rotation.Rotate0;
                var fDone = false;
                int nVolNo = 1;
                while (!fDone)
                {
                    var pdfDataFileToUse = pdfDataFileOrig;
                    StorageFile f = await StorageFile.GetFileFromPathAsync(pdfDataFileToUse);
                    var pdfDoc = await PdfDocument.LoadFromFileAsync(f);
                    var nPageCount = pdfDoc.PageCount;
                    for (uint i = 0; i < nPageCount; i++)
                    {
                        using (var page = pdfDoc.GetPage(i))
                        {
                            await AddPageToDoc(fixedDoc, page, rotation);
                        }
                    }
                    if (!pdfDataFileOrig.EndsWith("1.pdf"))
                    {
                        break;
                    }
                    nVolNo++;
                    pdfDataFileToUse = pdfDataFileOrig.Replace("1.pdf", string.Empty) + nVolNo.ToString() + ".pdf";
                    if (!File.Exists(pdfDataFileToUse))
                    {
                        break;
                    }
                }

                this.Content = docvwr;

                //flowdoc.PageHeight = rect.Height;
                //flowdoc.PageWidth = rect.Width;
                //IDocumentPaginatorSource idps = flowdoc;

                //var pdlg = new PrintDialog();
                //var queueName = "Microsoft Print to PDF";
                //var pServer = new PrintServer();
                //var pqueues = pServer.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local });
                //pdlg.PrintQueue = new PrintQueue(pServer, queueName);
                //pdlg.PrintDocument(idps.DocumentPaginator, "testprint");

                //var im = new Image()
                //{
                //    Source = PdfPages[0]
                //};
                //this.Content = im;
                //var sv = new ScrollViewer();
                //this.Content = sv;
                //var ictrl = new ItemsControl();
                //sv.Content = ictrl;
                //var dt = new DataTemplate();
                //dt.con

                ////              ictrl.ItemsSource = new[] { "1", "2" };
                //ictrl.ItemsSource = PdfPages;

            }
            catch (Exception ex)
            {
                this.Content = ex.ToString();
            }
        }

        private async Task AddPageToDoc(FixedDocument fixedDoc, PdfPage page, Rotation rotation = Rotation.Rotate0)
        {
            var bmi = new BitmapImage();
            using (var strm = new InMemoryRandomAccessStream())
            {
                var rect = page.Dimensions.ArtBox;
                var renderOpts = new PdfPageRenderOptions()
                {
                    DestinationWidth = (uint)rect.Height,
                    DestinationHeight = (uint)rect.Width,
                };

                await page.RenderToStreamAsync(strm);
                //var enc = new PngBitmapEncoder();
                //enc.Frames.Add(BitmapFrame.Create)
                bmi.BeginInit();
                bmi.StreamSource = strm.AsStream();
                bmi.CacheOption = BitmapCacheOption.OnLoad;
                bmi.Rotation = rotation;
                bmi.EndInit();

                var img = new Image()
                {
                    Source = bmi
                };

                var fixedPage = new FixedPage();
                fixedPage.Children.Add(img);
                var pc = new PageContent();
                pc.Child = fixedPage;

                fixedDoc.Pages.Add(pc);

                //var pdlg = new PrintDialog(); //https://stackoverflow.com/questions/1661995/printing-a-wpf-bitmapimage
                //var queueName = "Microsoft Print to PDF";
                //var pServer = new PrintServer();
                //var pqueues = pServer.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local });
                //pdlg.PrintQueue = new PrintQueue(pServer, queueName);
                ////var para = new Paragraph(new Run("some text"));
                ////var flowdoc = new FlowDocument(para)
                ////{
                ////    Name = "myname"
                ////};
                ////IDocumentPaginatorSource idpSource = flowdoc;
                ////pdlg.PrintDocument(idpSource.DocumentPaginator, "testdesc");


                //var sp = new StackPanel();
                //var img = new Image()
                //{
                //    Source = bmi
                //};
                //sp.Children.Add(img);
                //sp.Measure(new Size(pdlg.PrintableAreaWidth, pdlg.PrintableAreaHeight));
                //sp.Arrange(new Rect(new Point(0, 0), sp.DesiredSize));
                //pdlg.PrintVisual(sp, "test");


            }
        }
    }
}
