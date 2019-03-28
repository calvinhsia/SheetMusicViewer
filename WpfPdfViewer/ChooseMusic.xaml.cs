using System;
using System.Collections.Generic;
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
    /// Interaction logic for ChooseMusic.xaml
    /// </summary>
    public partial class ChooseMusic : Window
    {
        PdfViewerWindow _pdfViewerWindow;
        TreeView _TreeView;
        public PdfMetaData chosenPdfMetaData = null;
        public ChooseMusic()
        {
            InitializeComponent();
        }

        internal void Initialize(PdfViewerWindow pdfViewerWindow)
        {
            this._pdfViewerWindow = pdfViewerWindow;
            this.Top = pdfViewerWindow.Top;
            this.Left = pdfViewerWindow.Left;
            this.Loaded += ChooseMusic_Loaded;
        }

        private void ChooseMusic_Loaded(object sender, RoutedEventArgs e)
        {
            _TreeView = new TreeView();
            this.dpTview.Children.Add(_TreeView);
            foreach (var pdfMetaDataItem in 
                _pdfViewerWindow.
                lstPdfMetaFileData.
                OrderBy(p=> System.IO.Path.GetFileNameWithoutExtension(p.curFullPathFile)))
            {
                var tvItem = new TreeViewItem()
                {
                    Header = System.IO.Path.GetFileNameWithoutExtension(pdfMetaDataItem.curFullPathFile),
                    ToolTip = pdfMetaDataItem.curFullPathFile,
                    Tag = pdfMetaDataItem
                };
                _TreeView.Items.Add(tvItem);
            }

        }
        void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (_TreeView.SelectedItem != null)
            {
                chosenPdfMetaData =(PdfMetaData) ((TreeViewItem)_TreeView.SelectedItem).Tag;
            }
            this.DialogResult = true;
            this.Close();
        }
        void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
