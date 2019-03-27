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
        public ObservableCollection<BitmapImage> PdfPages { get; set; } = new ObservableCollection<BitmapImage>();

        private async void MainWindow_LoadedAsync(object sender, RoutedEventArgs e)
        {
            // https://blog.pieeatingninjas.be/2016/02/06/displaying-pdf-files-in-a-uwp-app/
            // https://blogs.windows.com/buildingapps/2017/01/25/calling-windows-10-apis-desktop-application/#RWYkd5C4WTeEybol.97
            try
            {
                var flowdoc = new FlowDocument()
                {
                    Name = "myname"
                };

                var titlePage = @"C:\Users\calvinh\OneDrive\Documents\SheetMusic\FakeBooks\ulimatePopRockCFaceUp\Ultimate Pop Rock Fake Book.pdf";
                var fTitle = await StorageFile.GetFileFromPathAsync(titlePage);
                var pdfDocTitle = await PdfDocument.LoadFromFileAsync(fTitle);
                var pgTitle = pdfDocTitle.GetPage(0);
                var rect = pgTitle.Dimensions.ArtBox;
                await AddPageToDoc(flowdoc, pgTitle);

                var pdfSourceDoc = @"C:\Users\calvinh\OneDrive\Documents\SheetMusic\FakeBooks\Ultimate Pop Rock Fake Book 1.pdf";
                var rotation = Rotation.Rotate180;
                pdfSourceDoc = @"C:\Users\calvinh\OneDrive\Documents\SheetMusic\Ragtime\Collections\The Music of James Scott001.pdf";
                rotation = Rotation.Rotate0;
                var dataFile = pdfSourceDoc;

                StorageFile f = await StorageFile.GetFileFromPathAsync(dataFile);
                var pdfDoc = await PdfDocument.LoadFromFileAsync(f);
                var nPageCount = pdfDoc.PageCount;
                //                nPageCount = 6;

                for (uint i = 70; i < nPageCount; i++)
                {
                    using (var page = pdfDoc.GetPage(i))
                    {
                        await AddPageToDoc(flowdoc, page, rotation);
                    }
                    //                    PdfPages.Add(bmi);
                }
                this.Content = flowdoc;

                flowdoc.PageHeight = rect.Height;
                flowdoc.PageWidth = rect.Width;
                IDocumentPaginatorSource idps = flowdoc;

                var pdlg = new PrintDialog();
                var queueName = "Microsoft Print to PDF";
                var pServer = new PrintServer();
                var pqueues = pServer.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local });
                pdlg.PrintQueue = new PrintQueue(pServer, queueName);
                pdlg.PrintDocument(idps.DocumentPaginator, "testprint");

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

        private async Task AddPageToDoc(FlowDocument flowdoc, PdfPage page, Rotation rotation = Rotation.Rotate0)
        {
            var bmi = new BitmapImage();
            using (var strm = new InMemoryRandomAccessStream())
            {
                var rect = page.Dimensions.ArtBox;
                var renderOpts = new PdfPageRenderOptions()
                {
                    DestinationWidth = (uint)rect.Height*2,
                    DestinationHeight = (uint)rect.Width*2,
                };

                await page.RenderToStreamAsync(strm, renderOpts);
                //var enc = new PngBitmapEncoder();
                //enc.Frames.Add(BitmapFrame.Create)
                bmi.BeginInit();
                bmi.StreamSource = strm.AsStream();
                bmi.CacheOption = BitmapCacheOption.OnLoad;
                bmi.Rotation = rotation;
                bmi.EndInit();

                var table = new Table();
                table.BreakPageBefore = true;
                //var col = new TableColumn();
                //table.Columns.Add(col);
                table.RowGroups.Add(new TableRowGroup());
                var row = new TableRow();
                table.RowGroups[0].Rows.Add(row);
                var cell = new TableCell();

                var para = new Paragraph(new Run("some text"));
                row.FontSize = 50;
                //cell.Blocks.Add(para);
                var img = new Image()
                {
                    Source = bmi
                };
                var uictr = new BlockUIContainer(img);
                //uictr.BreakPageBefore = true;
                cell.Blocks.Add(uictr);
                row.Cells.Add(cell);
                flowdoc.Blocks.Add(table);

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


                //var res = pdlg.ShowDialog();
                //if (res.HasValue && res.Value)
                //{

                //}
                //                            var newPdf = new PdfDocument();
            }
        }
    }
}
