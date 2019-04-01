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
using System.Windows.Shapes;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace WpfPdfViewer
{
    /// <summary>
    /// Interaction logic for ChooseMusic.xaml
    /// </summary>
    public partial class ChooseMusic : Window
    {
        PdfViewerWindow _pdfViewerWindow;
        TreeView _TreeView;
        readonly List<FavoriteEntry> _lstFavoriteEntries = new List<FavoriteEntry>();
        class FavoriteEntry
        {
            public Favorite _favorite;
            public PdfMetaData _pdfMetadata;
            public override string ToString()
            {
                return $"{_favorite} {_pdfMetadata}";
            }
        }

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
            this.Height = pdfViewerWindow.ActualHeight;
            this.txtCurrentRootFolder.Text = _pdfViewerWindow._RootMusicFolder;
            this.Loaded += ChooseMusic_Loaded;
            this.Owner = pdfViewerWindow;
        }
        async void BtnChangeMusicFolder_Click(object sender, RoutedEventArgs e)
        {
            var d = new System.Windows.Forms.FolderBrowserDialog
            {
                ShowNewFolderButton = false,
                Description = "Choose a folder with PDF music files",
                SelectedPath = this._pdfViewerWindow._RootMusicFolder
            };
            var res = d.ShowDialog();
            if (res == System.Windows.Forms.DialogResult.OK)
            {
                _pdfViewerWindow._RootMusicFolder = d.SelectedPath;
                await _pdfViewerWindow.LoadAllPdfMetaDataFromDiskAsync();
                this.txtCurrentRootFolder.Text = _pdfViewerWindow._RootMusicFolder;
                UpdateTreeView();
            }
        }

        async void UpdateTreeView()
        {
            _TreeView = new TreeView();
            this.dpTview.Children.Clear();
            this.dpTview.Children.Add(_TreeView);

            var tvitemFiles = new TreeViewItem()
            {
                Header = "Files"
            };
            _TreeView.Items.Add(tvitemFiles);

            var tvitemFavorites = new TreeViewItem()
            {
                Header = "Favorites"
            };
            _TreeView.Items.Add(tvitemFavorites);

            foreach (var pdfMetaDataItem in
                _pdfViewerWindow.
                lstPdfMetaFileData.
                OrderBy(p => System.IO.Path.GetFileNameWithoutExtension(p.curFullPathFile)))
            {
                foreach (var fav in pdfMetaDataItem.lstFavorites)
                {
                    _lstFavoriteEntries.Add(new FavoriteEntry() { _favorite = fav, _pdfMetadata = pdfMetaDataItem });
                }
                if (pdfMetaDataItem.PriorPdfMetaData == null)
                {
                    StorageFile f = await StorageFile.GetFileFromPathAsync(pdfMetaDataItem.curFullPathFile);
                    var pdfDoc = await PdfDocument.LoadFromFileAsync(f);
                    var bmi = new BitmapImage();
                    using (var page = pdfDoc.GetPage(0))
                    {
                        using (var strm = new InMemoryRandomAccessStream())
                        {
                            var rect = page.Dimensions.ArtBox;
                            var renderOpts = new PdfPageRenderOptions()
                            {
                                DestinationWidth = (uint)50,
                                DestinationHeight = (uint)80,
                            };

                            await page.RenderToStreamAsync(strm, renderOpts);
                            //var enc = new PngBitmapEncoder();
                            //enc.Frames.Add(BitmapFrame.Create)
                            bmi.BeginInit();
                            bmi.StreamSource = strm.AsStream();
                            bmi.CacheOption = BitmapCacheOption.OnLoad;
                            bmi.Rotation = (Rotation)pdfMetaDataItem.Rotation;
                            bmi.EndInit();
                        }
                    }

                    var tvItem = new TreeViewItem()
                    {
                        ToolTip = pdfMetaDataItem.curFullPathFile,
                        Tag = pdfMetaDataItem
                    };
                    var img = new Image()
                    {
                        Source = bmi
                    };
                    var sp = new StackPanel() { Orientation = Orientation.Horizontal };
                    sp.Children.Add(img);
                    sp.Children.Add(new TextBlock() { Text = System.IO.Path.GetFileNameWithoutExtension(pdfMetaDataItem.curFullPathFile) });
                    tvItem.Header = sp;
                    tvitemFiles.Items.Add(tvItem);
                }
            }

            foreach (var favEntry in _lstFavoriteEntries)
            {
                var tvItem = new TreeViewItem()
                {
                    Header = $"{favEntry}",
                    Tag = favEntry
                };
                tvitemFavorites.Items.Add(tvItem);
            }
            tvitemFavorites.IsExpanded = true;
            tvitemFiles.IsExpanded = true;
            _TreeView.MouseDoubleClick += (o, e) =>
            {
                BtnOk_Click(this, e);
            };
            _TreeView.KeyUp += (o, e) =>
            {
                if (e.Key == Key.Return)
                {
                    BtnOk_Click(this, e);
                }
            };
        }

        private void ChooseMusic_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateTreeView();
        }
        void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (_TreeView.SelectedItem != null)
            {
                var tg = ((TreeViewItem)_TreeView.SelectedItem).Tag;
                if (tg != null)
                {
                    if (tg is FavoriteEntry favoriteEntry)
                    {
                        chosenPdfMetaData = favoriteEntry._pdfMetadata;
                        chosenPdfMetaData.LastPageNo = favoriteEntry._favorite.Pageno;
                    }
                    else
                    {
                        chosenPdfMetaData = (PdfMetaData)tg;
                    }
                }
            }
            this.DialogResult = true; // chosenPdfMetaData  can be null
            this.Close();
        }
        void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
