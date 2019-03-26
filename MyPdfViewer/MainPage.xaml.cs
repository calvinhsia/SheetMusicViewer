using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Data.Pdf;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace MyPdfViewer
{

    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();
            this.Loaded += MainPage_Loaded;
        }

        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var dataFile = @"C:\Users\calvinh\OneDrive\Documents\SheetMusic\Ragtime\Collections\The Music of James Scott001.pdf";
                StorageFile f = await StorageFile.GetFileFromPathAsync(dataFile);
                var doc = await PdfDocument.LoadFromFileAsync(f);
                var nPageCount = doc.PageCount;
//                nPageCount = 2;
                for (uint i = 0; i < nPageCount; i++)
                {
                    var bmi = new BitmapImage();
                    var page = doc.GetPage(i);
                    using (var strm = new InMemoryRandomAccessStream())
                    {
                        await page.RenderToStreamAsync(strm);
                        await bmi.SetSourceAsync(strm);
                    }
                    PdfPages.Add(bmi);
                }
                var y = "done";
                //var sv =new ScrollViewer()
                //{

                //};
                //this.Content = sv;
                //var itc = new ItemsControl();
                //sv.Content = itc;




            }
            catch (Exception ex)
            {
                this.Content =new TextBlock() { Text = ex.ToString() };
            }
        }

        public ObservableCollection<BitmapImage> PdfPages { get; set; } = new ObservableCollection<BitmapImage>();
    }
}
