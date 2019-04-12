using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using System.Windows.Shapes;

namespace WpfPdfViewer
{
    /// <summary>
    /// Interaction logic for MetaDataForm.xaml
    /// </summary>
    public partial class MetaDataForm : Window
    {
        public BitmapImage ImgThumb { get { return _pdfViewerWindow?.currentPdfMetaData?.GetBitmapImageThumbnail(); } }

        public ObservableCollection<TOCEntry> LstTOC { get { return new ObservableCollection<TOCEntry>(_pdfViewerWindow?.currentPdfMetaData?.lstTocEntries); } }

        readonly PdfViewerWindow _pdfViewerWindow;
        public MetaDataForm(PdfViewerWindow pdfViewerWindow)
        {
            InitializeComponent();
            this._pdfViewerWindow = pdfViewerWindow;
            this.DataContext = this;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {

        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}
