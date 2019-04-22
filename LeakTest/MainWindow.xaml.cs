using System;
using System.Collections.Generic;
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

namespace LeakTest
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        readonly string root1 = @"c:\Sheetmusic";
        readonly string root2 = @"f:\Sheetmusic";
        string Rootfolder { get { if (Directory.Exists(root1)) { return root1; } return root2; } }
        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
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
                    using (var page = pdfDoc.GetPage((uint)i))
                    {
                        var bmi = new BitmapImage();
                        using (var strm = new InMemoryRandomAccessStream())
                        {
                            await page.RenderToStreamAsync(strm);
                            bmi.BeginInit();
                            bmi.StreamSource = strm.AsStream();
                            bmi.CacheOption = BitmapCacheOption.OnLoad;
                            bmi.Rotation = Rotation.Rotate180;
                            bmi.EndInit();
                            //var img = new Image()
                            //{
                            //    Source= bmi
                            //};
                            var imb = new ImageBrush(bmi);
                            var inkc = new InkCanvas()
                            {
                                Background = imb,
                                EditingMode=InkCanvasEditingMode.None
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
