using MemSpect;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WpfPdfViewer
{
    /// <summary>
    /// Interaction logic for ChooseMusic.xaml
    /// </summary>
    public partial class ChooseMusic : Window
    {
        public const string NewFolderDialogString = "New...";
        readonly PdfViewerWindow _pdfViewerWindow;
        TreeView _TreeView;
        readonly List<Favorite> _lstFavoriteEntries = new List<Favorite>();

        public PdfMetaData chosenPdfMetaData = null;
        public ChooseMusic(PdfViewerWindow pdfViewerWindow)
        {
            InitializeComponent();
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
                    else
                    {
                        await ChangeRootFolderAsync(path);
                    }
                }
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


            _pdfViewerWindow._RootMusicFolder = selectedPath;
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
                case "_Favorites":
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
                var uberToc = new List<TOCEntry>();
                foreach (var pdfMetaDataItem in
                    _pdfViewerWindow.
                    lstPdfMetaFileData.
                    OrderBy(p => p.GetFullPathFile(volNo: 0, MakeRelative: true)))
                {
                    foreach (var tentry in pdfMetaDataItem.lstTocEntries)
                    {
                        tentry.Tag = pdfMetaDataItem;
                        uberToc.Add(tentry);
                    }
                }
                var q = from itm in uberToc
                        select new
                        {
                            itm.SongName,
                            itm.Composer,
                            itm.Date,
                            itm.Notes,
                            itm.PageNo,
                            FileName = ((PdfMetaData)itm.Tag).GetFullPathFile(volNo: 0, MakeRelative: true),
                            _TocEntry = itm
                        };

                var br = new Browse(q,
                    fAllowHeaderClickSort: true,
                    fAllowBrowFilter: true,
                    ColWidths: new[] { 300, 300, 200, 200, 60, 500 });
                br._BrowseList.SelectionChanged += (o, e) =>
                {
                    e.Handled = true; // prevent bubbling SelectionChanged up to tabcontrol
                };
                br._BrowseList.MouseDoubleClick += (o, e) =>
                {
                    if (this.lbBooks.SelectedIndex >= 0)
                    {
                        BtnOk_Click(o, e);
                    }
                };
                br._BrowseList.KeyUp += (o, e) =>
                {
                    BtnOk_Click(o, e);
                };
                this.dpQuery.Children.Add(br);
            }
        }


        private async void FillBooksTab()
        {
            if (this.lbBooks.ItemsSource == null)
            {
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

                var lstBooks = new ObservableCollection<UIElement>();
                this.lbBooks.ItemsSource = lstBooks;

                var lstFoldrs = new ObservableCollection<UIElement>();
                this.lbfolders.ItemsSource = lstFoldrs;
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
                await FillBookItemsAsync();
                async Task FillBookItemsAsync()
                {
                    lstBooks.Clear();
                    var nBooks = 0;
                    var nSongs = 0;
                    var nPages = 0;
                    var nFavs = 0;
                    foreach (var pdfMetaDataItem in
                            _pdfViewerWindow.
                            lstPdfMetaFileData.
                            OrderBy(p => p.GetFullPathFile(volNo: 0, MakeRelative: true)))
                    {
                        var includeThisItem = false;
                        foreach (CheckBox chk in lstFoldrs)
                        {
                            if (chk.IsChecked == true)
                            {
                                var str = chk.Content as string + System.IO.Path.DirectorySeparatorChar.ToString();
                                if (pdfMetaDataItem.GetFullPathFile(volNo: 0, MakeRelative: true).IndexOf(str) >= 0)
                                {
                                    includeThisItem = true;
                                    break;
                                }
                            }
                        }
                        if (!includeThisItem)
                        {
                            continue;
                        }
                        if (tbxFilter.Text.Trim().Length > 0)
                        {
                            if (pdfMetaDataItem.GetFullPathFile(volNo: 0, MakeRelative: true).IndexOf(tbxFilter.Text.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                continue;
                            }
                        }
                        //if (pdfMetaDataItem._FullPathFile)
                        //{

                        //}
                        var contentControl = new ContentControl();
                        var sp = new StackPanel() { Orientation = Orientation.Vertical };
                        contentControl.Tag = pdfMetaDataItem;
                        await pdfMetaDataItem.GetBitmapImageThumbnailAsync();
                        sp.Children.Add(new Image() { Source = pdfMetaDataItem?.bitmapImageCache });
                        sp.Children.Add(new TextBlock()
                        {
                            Text = pdfMetaDataItem.GetFullPathFile(volNo: 0, MakeRelative: true),
                            ToolTip = pdfMetaDataItem.GetFullPathFile(volNo: 0)
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
                    this.tbxTotals.Text = $@"Total #Books = {
                        nBooks} # Songs = {
                        nSongs:n0} # Pages = {
                        nPages:n0} #Fav={
                        nFavs:n0}";

                }
            }
        }

        async void FillFavoritesTab()
        {
            if (this.dpTview.Children.Count == 0)
            {
                _TreeView = new TreeView();
                this.dpTview.Children.Clear();
                this.dpTview.Children.Add(_TreeView);

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
                        OrderBy(p => p.GetFullPathFile(volNo: 0, MakeRelative: true)))
                    {
                        foreach (var fav in pdfMetaDataItem.dictFav.Values)
                        {
                            fav.Tag = pdfMetaDataItem;
                            _lstFavoriteEntries.Add(fav);
                        }
                    }

                    foreach (var favEntry in _lstFavoriteEntries)
                    {
                        var sp = new StackPanel() { Orientation = Orientation.Horizontal };
                        sp.Children.Add(new Image() { Source = await ((PdfMetaData)favEntry.Tag).GetBitmapImageThumbnailAsync(), Height = 80, Width = 50 });
                        sp.Children.Add(new TextBlock() { Text = ((PdfMetaData)favEntry.Tag).GetDescription(favEntry.Pageno) });
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
                        var tg = (this.lbBooks.SelectedItem as FrameworkElement)?.Tag;
                        if (tg is PdfMetaData pdfMetaDataItem)
                        {
                            chosenPdfMetaData = pdfMetaDataItem;
                        }
                    }
                    else
                    {
                        // nothing is selected. we'll terminate dialog anyway, but allow nothing to be selected
                    }
                    break;
                case "_Favorites":
                    if (_TreeView.SelectedItem != null)
                    {
                        var tg = ((TreeViewItem)_TreeView.SelectedItem).Tag;
                        if (tg != null)
                        {
                            if (tg is Favorite favoriteEntry)
                            {
                                chosenPdfMetaData = (PdfMetaData)favoriteEntry.Tag;
                                chosenPdfMetaData.LastPageNo = favoriteEntry.Pageno;
                            }
                            else
                            {
                                chosenPdfMetaData = (PdfMetaData)tg;
                            }
                        }
                    }
                    break;
                case "_Query":
                    var br = (Browse)this.dpQuery.Children[0];
                    var selitem = br._BrowseList.SelectedItem;
                    if (selitem != null)
                    {
                        var tdescitem = TypeDescriptor.GetProperties(selitem)["_TocEntry"];
                        var TocEntry = (TOCEntry)tdescitem.GetValue(selitem);
                        chosenPdfMetaData = (PdfMetaData)TocEntry.Tag;
                        chosenPdfMetaData.LastPageNo = TocEntry.PageNo;
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
            //this just gets the source. 
            // I cast it to FE because I wanted to use ActualWidth for Center. You could try RenderSize as alternate
            if (e.Source is FrameworkElement element)
            {
                //e.DeltaManipulation has the changes 
                // Scale is a delta multiplier; 1.0 is last size,  (so 1.1 == scale 10%, 0.8 = shrink 20%) 
                // Rotate = Rotation, in degrees
                // Pan = Translation, == Translate offset, in Device Independent Pixels 

                var deltaManipulation = e.DeltaManipulation;
                var matrix = ((MatrixTransform)element.RenderTransform).Matrix;
                // find the old center; arguaby this could be cached 
                Point center = new Point(element.ActualWidth / 2, element.ActualHeight / 2);
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
}
