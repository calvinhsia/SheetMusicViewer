using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
                return $"{_pdfMetadata.GetDescription(_favorite.Pageno)}";
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
            this.Loaded += ChooseMusic_Loaded;
            //            this.Owner = pdfViewerWindow;
        }
        private void ChooseMusic_Loaded(object sender, RoutedEventArgs e)
        {
            this.Top = _pdfViewerWindow.Top;
            this.Left = _pdfViewerWindow.Left;
            this.WindowState = WindowState.Maximized;
            this.txtCurrentRootFolder.Text = _pdfViewerWindow._RootMusicFolder;
            this.tabControl.SelectionChanged += (o, et) =>
                {
                    var tabItemHeaer = ((TabItem)(this.tabControl.SelectedItem)).Header as string;
                    switch (tabItemHeaer)
                    {
                        case "_Books":
                            break;
                        case "_Favorites":
                            FillFavoritesTab();
                            break;
                        case "_Query":
                            break;
                        case "_Playlists":
                            break;
                    }
                };
            FillBooksTab();
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
                FillFavoritesTab();
            }
        }
        private void FillBooksTab()
        {
            this.lbBooks.MouseDoubleClick += (o, e) =>
              {
                  BtnOk_Click(o, e);
              };
            this.lbBooks.KeyUp += (o, e) =>
             {
                 if (e.Key == Key.Return)
                 {
                     BtnOk_Click(this, e);
                 }
             };
            this.lbBooks.ItemsSource = BooksItemsSource();
        }

        IEnumerable<UIElement> BooksItemsSource()
        {
            foreach (var pdfMetaDataItem in
                _pdfViewerWindow.
                lstPdfMetaFileData.
                Where(p => p.PriorPdfMetaData == null).
                OrderBy(p => p.RelativeFileName))
            {
                var sp = new StackPanel() { Orientation = Orientation.Vertical };
                sp.Tag = pdfMetaDataItem;
                sp.Children.Add(new Image() { Source = pdfMetaDataItem.GetBitmapImageThumbnail()});
                sp.Children.Add(new TextBlock() { Text = pdfMetaDataItem.RelativeFileName });
                yield return sp;
            }
        }

        void FillFavoritesTab()
        {
            _TreeView = new TreeView();
            this.dpTview.Children.Clear();
            this.dpTview.Children.Add(_TreeView);

            var tvitemFavorites = new TreeViewItem()
            {
                Header = "Favorites"
            };
            _TreeView.Items.Add(tvitemFavorites);

            foreach (var pdfMetaDataItem in
                _pdfViewerWindow.
                lstPdfMetaFileData.
                OrderBy(p => p.RelativeFileName))
            {
                foreach (var fav in pdfMetaDataItem.Favorites)
                {
                    _lstFavoriteEntries.Add(new FavoriteEntry() { _favorite = fav, _pdfMetadata = pdfMetaDataItem });
                }
            }

            foreach (var favEntry in _lstFavoriteEntries)
            {
                var sp = new StackPanel() { Orientation = Orientation.Horizontal };
                sp.Children.Add(new Image() { Source = favEntry._pdfMetadata.GetBitmapImageThumbnail(), Height=80, Width=50 });
                sp.Children.Add(new TextBlock() { Text = favEntry.ToString() });
                var tvItem = new TreeViewItem()
                {
                    Header = sp,
                    Tag = favEntry
                };
                tvitemFavorites.Items.Add(tvItem);
            }
            tvitemFavorites.IsExpanded = true;
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

        void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            var tabItemHeaer = ((TabItem)(this.tabControl.SelectedItem)).Header as string;
            switch (tabItemHeaer)
            {
                case "_Books":
                    if (this.lbBooks.SelectedIndex >= 0)
                    {
                        var tg = (this.lbBooks.SelectedItem as FrameworkElement)?.Tag;
                        if (tg is PdfMetaData pdfMetaDataItem)
                        {
                            chosenPdfMetaData = pdfMetaDataItem;
                        }
                    }
                    break;
                case "_Favorites":
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
                    break;
                case "_Query":
                    break;
                case "_Playlists":
                    break;
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
