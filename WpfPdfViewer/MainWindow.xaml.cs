using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
                var dataFile = @"C:\Users\calvinh\OneDrive\Documents\SheetMusic\Ragtime\Collections\The Music of James Scott001.pdf";
                StorageFile f = await StorageFile.GetFileFromPathAsync(dataFile);
                var doc = await PdfDocument.LoadFromFileAsync(f);
                var nPageCount = doc.PageCount;
                nPageCount = 2;
                for (uint i = 0; i < nPageCount; i++)
                {
                    var bmi = new BitmapImage();
                    using (var page = doc.GetPage(i))
                    {
                        using (var strm = new InMemoryRandomAccessStream())
                        {
                            await page.RenderToStreamAsync(strm);
                            //var enc = new PngBitmapEncoder();
                            //enc.Frames.Add(BitmapFrame.Create)
                            bmi.StreamSource = strm.AsStream();
                        }
                    }
                    PdfPages.Add(bmi);
                }
                var im = new Image()
                {
                    Source = PdfPages[0]
                };
                this.Content = im;
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
    }
}
