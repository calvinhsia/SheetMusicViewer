using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SheetMusicLib;

namespace SheetMusicViewer
{
    /// <summary>
    /// Interaction logic for ChooseMusic.xaml
    /// </summary>
    public partial class ChooseMusic : Window
    {
        public const string NewFolderDialogString = "New...";
        readonly PdfViewerWindow _pdfViewerWindow;
        TreeView _TreeView;
        bool doneLoading = false;

        public PdfMetaData chosenPdfMetaData = null;
        public ChooseMusic(PdfViewerWindow pdfViewerWindow)
        {
            InitializeComponent();
            this.ShowInTaskbar = false;
            //this.Topmost = true;
            this.Owner = Application.Current?.MainWindow;
            this._pdfViewerWindow = pdfViewerWindow;
            this.Loaded += ChooseMusic_Loaded;
            this.Top = _pdfViewerWindow.Top;
            this.Left = _pdfViewerWindow.Left;
            this.WindowState = WindowState.Maximized;
        }

        private void ChooseMusic_Loaded(object sender, RoutedEventArgs e)
        {
            var mruRootFolderItems = Properties.Settings.Default.RootFolderMRU;
            CboEnableCboSelectionChange = false;

            var chooseSortBy = Properties.Settings.Default.ChooseBooksSort;
            switch (chooseSortBy)
            {
                case "ByFolder":
                    rbtnByFolder.IsChecked = true;
                    break;
                case "ByDate":
                    rbtnByDate.IsChecked = true;
                    break;
                case "ByNumPages":
                    rbtnByNumPages.IsChecked = true;
                    break;
            }
            if (!string.IsNullOrEmpty(_pdfViewerWindow._RootMusicFolder))
            {
                this.cboRootFolder.Items.Add(new ComboBoxItem() { Content = _pdfViewerWindow._RootMusicFolder });
            }
            if (mruRootFolderItems != null && mruRootFolderItems.Count > 0)
            {
                foreach (var itm in mruRootFolderItems)
                {
                    if (!string.IsNullOrEmpty(_pdfViewerWindow._RootMusicFolder) && itm != _pdfViewerWindow._RootMusicFolder)
                    {
                        this.cboRootFolder.Items.Add(new ComboBoxItem() { Content = itm });
                    }
                }
            }
            this.cboRootFolder.Items.Add(new ComboBoxItem() { Content = NewFolderDialogString });
            if (this.cboRootFolder.Items.Count == 1)
            {
                CboEnableCboSelectionChange = true;
                this.cboRootFolder.SelectedIndex = 0;
            }
            else
            {
                this.cboRootFolder.SelectedIndex = 0;
                CboEnableCboSelectionChange = true;
            }
            //            this.cboRootFolder.SelectedIndex = 0;
            ActivateTab(string.Empty);
            this.tabControl.SelectionChanged += (o, et) =>
                {
                    var tabItemHeader = ((TabItem)(this.tabControl.SelectedItem)).Header as string;
                    ActivateTab(tabItemHeader);
                    et.Handled = true;
                };
            doneLoading = true;
        }
        private void CboRootFolder_DropDownOpened(object sender, EventArgs e)
        {
            if (this.cboRootFolder.Items.Count == 1)
            {
                ShowRootChooseRootFolderDialog();
            }
        }

        private void CboRootFolder_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is ComboBox cbo && cbo.IsDropDownOpen && e.Key == Key.Delete)
            {
                if (cbo.Items.Count > 0) // can't delete the NewFolderDialog at the end
                {
                    foreach (ComboBoxItem itm in cbo.Items)
                    {
                        if (itm.IsHighlighted && (string)itm.Content != NewFolderDialogString)
                        {
                            CboEnableCboSelectionChange = false;
                            cbo.Items.Remove(itm);
                            CboEnableCboSelectionChange = true;
                            break;
                        }
                    }
                }
                e.Handled = true;
            }
        }

        bool CboEnableCboSelectionChange = true;
        private async void CboRootFolder_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboEnableCboSelectionChange)
            {
                if (this.cboRootFolder.SelectedItem != null)
                {
                    var path = (string)((ComboBoxItem)this.cboRootFolder.SelectedItem).Content;
                    if (path == NewFolderDialogString)
                    {
                        ShowRootChooseRootFolderDialog();
                    }
                    else
                    {
                        await ChangeRootFolderAsync(path);
                    }
                }
            }
        }

        private async void ShowRootChooseRootFolderDialog()
        {
            var d = new System.Windows.Forms.FolderBrowserDialog
            {
                ShowNewFolderButton = false,
                Description = "Choose a root folder with PDF music files",
                SelectedPath = this._pdfViewerWindow._RootMusicFolder
            };
            var res = d.ShowDialog();
            if (res == System.Windows.Forms.DialogResult.OK)
            {
                CboEnableCboSelectionChange = false;
                this.cboRootFolder.Items.Insert(0, new ComboBoxItem() { Content = d.SelectedPath });
                this.cboRootFolder.SelectedIndex = 0;
                CboEnableCboSelectionChange = true;
                await ChangeRootFolderAsync(d.SelectedPath);
            }
            else
            {
                CboEnableCboSelectionChange = false;
                this.cboRootFolder.SelectedIndex = 0;
                CboEnableCboSelectionChange = true;
            }
        }

        private async Task ChangeRootFolderAsync(string selectedPath)
        {
            var col = new StringCollection
                {
                    selectedPath
                };

            foreach (ComboBoxItem itm in this.cboRootFolder.Items)
            {
                var str = (string)itm.Content;
                if (!col.Contains(str) && str != NewFolderDialogString)
                {
                    col.Add((string)itm.Content);
                }
            }
            Properties.Settings.Default.RootFolderMRU = col;
            Properties.Settings.Default.Save();
            // now that we have the col in MRU order, we want to rearrange the cbo.items in same order
            CboEnableCboSelectionChange = false;
            this.cboRootFolder.Items.Clear();
            foreach (var itm in col)
            {
                this.cboRootFolder.Items.Add(new ComboBoxItem() { Content = itm });
            }
            this.cboRootFolder.Items.Add(new ComboBoxItem() { Content = NewFolderDialogString });
            this.cboRootFolder.SelectedIndex = 0;
            CboEnableCboSelectionChange = true;


            if (Directory.Exists(selectedPath))
            {
                _pdfViewerWindow._RootMusicFolder = selectedPath;
            }
            //            this.cboRootFolder.Text = _pdfViewerWindow._RootMusicFolder;
            this.tabControl.SelectedIndex = 0;
            _pdfViewerWindow.CloseCurrentPdfFile();
            _pdfViewerWindow.lstPdfMetaFileData.Clear(); // release mem
            (_pdfViewerWindow.lstPdfMetaFileData, _pdfViewerWindow.lstFolders) = await PdfMetaData.LoadAllPdfMetaDataFromDiskAsync(selectedPath);
            this.lbBooks.ItemsSource = null;
            this.dpTview.Children.Clear();
            this.dpQuery.Children.Clear();
            this.dpPlaylists.Children.Clear();
            ActivateTab(string.Empty);
        }

        private void ActivateTab(string tabItemHeader)
        {
            if (string.IsNullOrEmpty(tabItemHeader))
            {
                tabItemHeader = Properties.Settings.Default.ChooseQueryTab;
            }

            switch (tabItemHeader)
            {
                case "_Books":
                    FillBooksTab();
                    if (this.tabControl.SelectedIndex != 0)
                    {
                        this.tabControl.SelectedIndex = 0;
                    }
                    else
                    {
                    }
                    break;
                case "Fa_vorites":
                    FillFavoritesTab();
                    if (this.tabControl.SelectedIndex != 1)
                    {
                        this.tabControl.SelectedIndex = 1;
                    }
                    else
                    {
                    }
                    break;
                case "_Query":
                    FillQueryTab();
                    if (this.tabControl.SelectedIndex != 2)
                    {
                        this.tabControl.SelectedIndex = 2;
                    }
                    else
                    {
                    }
                    break;
                case "_Playlists":
                    break;
            }
        }

        private void FillQueryTab()
        {
            if (this.dpQuery.Children.Count == 0 && _pdfViewerWindow.lstPdfMetaFileData != null)
            {
                var uberToc = new List<Tuple<PdfMetaData, TOCEntry>>();
                foreach (var pdfMetaDataItem in _pdfViewerWindow.lstPdfMetaFileData)
                {
                    foreach (var tentry in pdfMetaDataItem.lstTocEntries)
                    {
                        uberToc.Add(Tuple.Create(pdfMetaDataItem, tentry));
                    }
                }
                var q = from tup in uberToc
                        let itm = tup.Item2
                        let FileInfo = new FileInfo(tup.Item1.GetFullPathFileFromPageNo(itm.PageNo))
                        orderby itm.SongName
                        select new
                        {
                            itm.SongName,
                            Page = itm.PageNo,
                            Vol = tup.Item1.GetVolNumFromPageNum(itm.PageNo),
                            itm.Composer,
                            CompositionDate = itm.Date,
                            Fav = tup.Item1.IsFavorite(itm.PageNo) ? "Fav" : string.Empty,
                            BookName = tup.Item1.GetFullPathFileFromVolno(volNo: 0, MakeRelative: true),
                            itm.Notes,
                            Acquisition = FileInfo.LastWriteTime,
                            Access = FileInfo.LastAccessTime,
                            Created = FileInfo.CreationTime,
                            _Tup = tup
                        };

                var br = new BrowsePanel(q,
                    colWidths: new[] { 300, 40, 30, 100, 80, 30, 200, 500 });
                br.BrowseList.SelectionChanged += (o, e) =>
                {
                    e.Handled = true; // prevent bubbling SelectionChanged up to tabcontrol
                };
                br.BrowseList.MouseDoubleClick += (o, e) =>
                {
                    if (br.BrowseList.SelectedIndex >= 0)
                    {
                        BtnOk_Click(o, e);
                    }
                };
                br.BrowseList.KeyUp += (o, e) =>
                {
                    //                    BtnOk_Click(o, e);
                };
                this.dpQuery.Children.Add(br);
            }
        }

        private async void FillBooksTab()
        {
            if (this.lbBooks.ItemsSource == null)
            {
                var lstBooks = new ObservableCollection<UIElement>();
                this.lbBooks.ItemsSource = lstBooks;
                this.lbBooks.KeyUp += (o, e) =>
                 {
                     if (e.Key == Key.Return)
                     {
                         BtnOk_Click(this, e);
                     }
                 };
                this.lbBooks.SelectionChanged += (o, e) =>
                {
                    e.Handled = true; // prevent bubbling SelectionChanged up to tabcontrol
                };

                var lstFoldrs = new ObservableCollection<UIElement>();
                foreach (var folder in _pdfViewerWindow.lstFolders)
                {
                    var chkbox = new CheckBox()
                    {
                        Content = folder,
                        IsChecked = true
                    };
                    chkbox.Checked += async (o, e) =>
                    {
                        await FillBookItemsAsync();
                    };
                    chkbox.Unchecked += async (o, e) =>
                    {
                        await FillBookItemsAsync();
                    };
                    lstFoldrs.Add(chkbox);
                }
                tbxFilter.TextChanged += async (o, e) =>
                   {
                       await FillBookItemsAsync();
                   };
            }
            await FillBookItemsAsync();
        }
        private void Rbtn_Checked(object sender, RoutedEventArgs e)
        {
            if (doneLoading) // the load sets the initial btn state which fires the Checked.
            {
                FillBooksTab();
            }
        }

        async Task FillBookItemsAsync()
        {
            var lstBooks = this.lbBooks.ItemsSource as ObservableCollection<UIElement>;
            lstBooks.Clear();
            var nBooks = 0;
            var nSongs = 0;
            var nPages = 0;
            var nFavs = 0;
            foreach (var pdfMetaDataItem in
                    _pdfViewerWindow.
                    lstPdfMetaFileData.
                    OrderBy(p =>
                    {
                        if (this.rbtnByDate.IsChecked == true)
                        {
                            var date = p.dtLastWrite; // (new System.IO.FileInfo(p.PdfBmkMetadataFileName)).LastWriteTime;
                            return (DateTime.Now - date).TotalSeconds.ToString("0000000000");
                        }
                        else if (this.rbtnByFolder.IsChecked == true)
                        {
                            return p.GetFullPathFileFromVolno(volNo: 0, MakeRelative: true);
                        }
                        else
                        {
                            return (100000 - p.NumPagesInSet).ToString("00000");
                        }
                    }))
            {
                if (tbxFilter.Text.Trim().Length > 0)
                {
                    if (pdfMetaDataItem.GetFullPathFileFromVolno(volNo: 0, MakeRelative: true).IndexOf(tbxFilter.Text.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                }
                var contentControl = new MyContentControl(pdfMetaDataItem); // ContentControl has doubleclick event
                var sp = new StackPanel() { Orientation = Orientation.Vertical };
                await pdfMetaDataItem.GetBitmapImageThumbnailAsync();
                var img = new Image
                {
                    Source = pdfMetaDataItem?.bitmapImageCache,
                    ToolTip = $"{pdfMetaDataItem} + {pdfMetaDataItem.dtLastWrite}"
                };
                sp.Children.Add(img);
                sp.Children.Add(new TextBlock()
                {
                    Text = pdfMetaDataItem.GetFullPathFileFromVolno(volNo: 0, MakeRelative: true),
                    ToolTip = pdfMetaDataItem.GetFullPathFileFromVolno(volNo: 0)
                });
                var data = $"#Sngs={pdfMetaDataItem.GetSongCount()} Pg={pdfMetaDataItem.GetTotalPageCount()} Fav={pdfMetaDataItem.dictFav.Count}";
                sp.Children.Add(new TextBlock()
                {
                    Text = $"#Sngs={pdfMetaDataItem.GetSongCount()} Pg={pdfMetaDataItem.GetTotalPageCount()} Fav={pdfMetaDataItem.dictFav.Count}",
                    ToolTip = data
                });
                await Task.Delay(0);
                contentControl.Content = sp;
                lstBooks.Add(contentControl);
                contentControl.MouseDoubleClick += (o, e) =>
                {
                    BtnOk_Click(this, e);
                };
                contentControl.TouchDown += (o, e) =>
                {
                    if (PdfViewerWindow.IsDoubleTap(this.lbBooks, e))
                    {
                        this.lbBooks.SelectedItem = o;
                        BtnOk_Click(o, e);
                    }
                };
                nBooks++;
                nSongs += pdfMetaDataItem.lstTocEntries.Count;
                nPages += pdfMetaDataItem.NumPagesInSet;
                nFavs += pdfMetaDataItem.dictFav.Count;
            }
            this.tbxTotals.Text = $@"Total #Books = {nBooks} # Songs = {nSongs:n0} # Pages = {nPages:n0} #Fav={nFavs:n0}";
            if (lstBooks.Count > 0)
            {
                this.lbBooks.ScrollIntoView(lstBooks[0]);
            }
        }

        async void FillFavoritesTab()
        {
            if (this.dpTview.Children.Count == 0)
            {
                _TreeView = new TreeView();
                this.dpTview.Children.Clear();
                this.dpTview.Children.Add(_TreeView);
                var lstFavoriteEntries = new List<Tuple<PdfMetaData, Favorite>>();

                var tvitemFavorites = new TreeViewItem()
                {
                    Header = "Favorites"
                };
                _TreeView.Items.Add(tvitemFavorites);
                if (_pdfViewerWindow.lstPdfMetaFileData != null)
                {
                    foreach (var pdfMetaDataItem in
                        _pdfViewerWindow.
                        lstPdfMetaFileData.
                        OrderBy(p => p.GetFullPathFileFromVolno(volNo: 0, MakeRelative: true)))
                    {
                        foreach (var fav in pdfMetaDataItem.dictFav.Values)
                        {
                            lstFavoriteEntries.Add(Tuple.Create(pdfMetaDataItem, fav));
                        }
                    }

                    foreach (var tupFav in lstFavoriteEntries)
                    {
                        var sp = new StackPanel() { Orientation = Orientation.Horizontal };
                        sp.Children.Add(new Image() { Source = await tupFav.Item1.GetBitmapImageThumbnailAsync(), Height = 80, Width = 50 });
                        sp.Children.Add(new TextBlock() { Text = tupFav.Item1.GetDescription(tupFav.Item2.Pageno) });
                        sp.Children.Add(new TextBlock() { Text = $" Page {tupFav.Item2.Pageno}" });
                        var tvItem = new MyTreeViewItem(tupFav.Item1, tupFav.Item2)
                        {
                            Header = sp
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
            }
        }

        void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            var tabItemHeaer = ((TabItem)(this.tabControl.SelectedItem)).Header as string;
            switch (tabItemHeaer)
            {
                case "_Books":
                    if (this.lbBooks.SelectedIndex >= 0)
                    {
                        var mycontent = this.lbBooks.SelectedItem as MyContentControl;
                        chosenPdfMetaData = mycontent.pdfMetaDataItem;
                    }
                    else
                    {
                        // nothing is selected. we'll terminate dialog anyway, but allow nothing to be selected
                    }
                    var oldChooseSortby = Properties.Settings.Default.ChooseBooksSort;
                    var newChooseSortBy = rbtnByDate.IsChecked == true ? "ByDate" : (rbtnByFolder.IsChecked == true ? "ByFolder" : "ByNumPages");
                    if (newChooseSortBy != oldChooseSortby)
                    {
                        Properties.Settings.Default.ChooseBooksSort = newChooseSortBy;
                        Properties.Settings.Default.Save();
                    }
                    break;
                case "Fa_vorites":
                    if (_TreeView.SelectedItem != null)
                    {
                        if (_TreeView.SelectedItem is MyTreeViewItem myTreeViewItem)
                        {
                            chosenPdfMetaData = myTreeViewItem.pdfMetaData;
                            chosenPdfMetaData.LastPageNo = myTreeViewItem.favEntry.Pageno;
                        }
                    }
                    break;
                case "_Query":
                    var br = (BrowsePanel)this.dpQuery.Children[0];
                    var selitem = br.BrowseList.SelectedItem;
                    if (selitem != null)
                    {
                        var tdescitem = TypeDescriptor.GetProperties(selitem)["_Tup"];
                        Tuple<PdfMetaData, TOCEntry> tup = (Tuple<PdfMetaData, TOCEntry>)tdescitem.GetValue(selitem);
                        chosenPdfMetaData = tup.Item1;
                        chosenPdfMetaData.LastPageNo = tup.Item2.PageNo;
                    }
                    break;
                case "_Playlists":
                    break;
            }
            Properties.Settings.Default.ChooseQueryTab = ((TabItem)(this.tabControl.SelectedItem)).Header as string;
            this.DialogResult = true; // chosenPdfMetaData  can be null
            this.Close();
            e.Handled = true;
        }
        void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            this.Close();
        }



        private void WrapPanel_ManipulationStarting(object sender, ManipulationStartingEventArgs e)
        {
            //            e.IsSingleTouchEnabled = false;
            e.ManipulationContainer = this.tabControl;
            e.Handled = true;
        }
        private void WrapPanel_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            //thisjust gets the source. 
            // I cast it to FE because I wanted to use ActualWidth for Center. You could try RenderSize as alternate
            if (e.Source is FrameworkElement element)
            {
                //e.DeltaManipulation has the changes 
                // Scale is a delta multiplier; 1.0 is last size,  (so 1.1 == scale 10%, 0.8 = shrink 20%) 
                // Rotate = Rotation, in degrees
                // Pan = Translation, == Translate offset, in Device Independent Pixels 

                var deltaManipulation = e.DeltaManipulation;
                var matrix = ((MatrixTransform)element.RenderTransform).Matrix;
                // find the old center; arguably this could be cached 
                Point center = new(element.ActualWidth / 2, element.ActualHeight / 2);
                // transform it to take into account transforms from previous manipulations 
                center = matrix.Transform(center);
                //this will be a Zoom. 
                matrix.ScaleAt(deltaManipulation.Scale.X, deltaManipulation.Scale.Y, center.X, center.Y);
                // Rotation 
                matrix.RotateAt(e.DeltaManipulation.Rotation, center.X, center.Y);
                //Translation (pan) 
                matrix.Translate(e.DeltaManipulation.Translation.X, e.DeltaManipulation.Translation.Y);

                element.RenderTransform = new MatrixTransform(matrix);
                //                ((MatrixTransform)element.RenderTransform).Matrix = matrix;

                e.Handled = true;
            }
        }

        private void WrapPanel_ManipulationInertiaStarting(object sender, ManipulationInertiaStartingEventArgs e)
        {
            // Decrease the velocity of the Rectangle's movement by 
            // 10 inches per second every second.
            // (10 inches * 96 pixels per inch / 1000ms^2)
            e.TranslationBehavior.DesiredDeceleration = 10.0 * 96.0 / (1000.0 * 1000.0);

            // Decrease the velocity of the Rectangle's resizing by 
            // 0.1 inches per second every second.
            // (0.1 inches * 96 pixels per inch / (1000ms^2)
            e.ExpansionBehavior.DesiredDeceleration = 0.1 * 96 / (1000.0 * 1000.0);

            // Decrease the velocity of the Rectangle's rotation rate by 
            // 2 rotations per second every second.
            // (2 * 360 degrees / (1000ms^2)
            e.RotationBehavior.DesiredDeceleration = 720 / (1000.0 * 1000.0);

            e.Handled = true;
        }

    }
    internal class MyContentControl : ContentControl
    {
        public PdfMetaData pdfMetaDataItem;
        public MyContentControl()
        {
        }

        public MyContentControl(PdfMetaData pdfMetaDataItem)
        {
            this.pdfMetaDataItem = pdfMetaDataItem;
        }
    }
    internal class MyTreeViewItem : TreeViewItem
    {
        public readonly PdfMetaData pdfMetaData;
        public readonly Favorite favEntry;
        public MyTreeViewItem(PdfMetaData pdfMetaData, Favorite favEntry)
        {
            this.pdfMetaData = pdfMetaData;
            this.favEntry = favEntry;
        }
    }
}
