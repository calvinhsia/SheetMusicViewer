using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
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
    public partial class MetaDataForm : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public void OnMyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public BitmapImage ImgThumb { get { return _pdfViewerWindow.currentPdfMetaData?.GetBitmapImageThumbnail(); } }

        public int PageNumberOffset { get; set; }
        public List<string> LstVolInfo { get; set; }

        public string DocNotes { get; set; }

        ObservableCollection<TOCEntry> _lstToc;
        public ObservableCollection<TOCEntry> LstTOC { get { return _lstToc; } set { _lstToc = value; OnMyPropertyChanged(); } }

        ObservableCollection<FavDisp> _lstFavDisp;
        public ObservableCollection<FavDisp> LstFavDisp { get { return _lstFavDisp; } set { _lstFavDisp = value; OnMyPropertyChanged(); } }
        public class FavDisp
        {
            public int PageNo { get; set; }
            public string Description { get; set; }
        }


        readonly PdfViewerWindow _pdfViewerWindow;
        public MetaDataForm(PdfViewerWindow pdfViewerWindow)
        {
            InitializeComponent();
            this._pdfViewerWindow = pdfViewerWindow;
            LstTOC = new ObservableCollection<TOCEntry>();
            foreach (var itm in pdfViewerWindow.currentPdfMetaData.lstTocEntries.OrderBy(p => p.PageNo))
            {
                LstTOC.Add((TOCEntry)itm.Clone());
            }
            LstVolInfo = new List<string>();
            int volno = 0;
            var pgno = pdfViewerWindow.currentPdfMetaData.PageNumberOffset;
            foreach (var vol in pdfViewerWindow.currentPdfMetaData.lstVolInfo)
            {
                LstVolInfo.Add($"Vol={volno++} Pg= {pgno,3} {vol}");
                pgno += vol.NPagesInThisVolume;
            }
            //var pgno = pdfViewerWindow.currentPdfMetaData.PageNumberOffset;
            //Array.ForEach<PdfVolumeInfo>(pdfViewerWindow.currentPdfMetaData.lstVolInfo.ToArray(),
            //    {
            //    (p => LstVolInfo.Add($"Vol={volno++} {p}")
            //    ););
            LstFavDisp = new ObservableCollection<FavDisp>();
            foreach (var fav in pdfViewerWindow.currentPdfMetaData.dictFav.Values)
            {
                LstFavDisp.Add(new FavDisp() { PageNo = fav.Pageno, Description = pdfViewerWindow.currentPdfMetaData.GetDescription(fav.Pageno) });
            }

            this.PageNumberOffset = pdfViewerWindow.currentPdfMetaData.PageNumberOffset;
            this.DocNotes = pdfViewerWindow.currentPdfMetaData.Notes;
            this.DataContext = this;
            this.Left = Properties.Settings.Default.EditWindowPos.Width;
            this.Top = Properties.Settings.Default.EditWindowPos.Height;
            this.Width = Properties.Settings.Default.EditWindowSize.Width;
            this.Height = Properties.Settings.Default.EditWindowSize.Height;
            this.Closed += (o, e) =>
              {
                  Properties.Settings.Default.EditWindowPos = new System.Drawing.Size((int)this.Left, (int)this.Top);
                  Properties.Settings.Default.EditWindowSize = new System.Drawing.Size((int)this.ActualWidth, (int)this.ActualHeight);
                  Properties.Settings.Default.Save();
              };
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {

        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            _pdfViewerWindow.currentPdfMetaData.InitializeDictToc(LstTOC.ToList());
            _pdfViewerWindow.currentPdfMetaData.Notes = DocNotes?.Trim();
            _pdfViewerWindow.currentPdfMetaData.PageNumberOffset = PageNumberOffset;
            PdfMetaData.SavePdfMetaFileData(_pdfViewerWindow.currentPdfMetaData, ForceSave: true);
            this.Close();
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            // PageNo,Songname, Composer, Date, Notes
            var clipText = System.Windows.Forms.Clipboard.GetText(System.Windows.Forms.TextDataFormat.Text);
            if (!string.IsNullOrEmpty(clipText))
            {
                var lstImportedToc = new List<TOCEntry>();
                try
                {
                    var lines = clipText.Split("\r\n".ToArray(), StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var parts = line.Split("\t".ToArray());
                        var newEntry = new TOCEntry();

                        if (parts.Length > 0)
                        {
                            newEntry.PageNo = int.Parse(parts[0]);
                        }
                        if (parts.Length > 1 && !string.IsNullOrEmpty(parts[1]))
                        {
                            newEntry.SongName = parts[1].Trim();
                        }
                        if (parts.Length > 2 && !string.IsNullOrEmpty(parts[2]))
                        {
                            newEntry.Composer = parts[2].Trim();
                        }
                        if (parts.Length > 3 && !string.IsNullOrEmpty(parts[3]))
                        {
                            newEntry.Date = parts[3].Trim();
                        }
                        if (parts.Length > 4 && !string.IsNullOrEmpty(parts[4]))
                        {
                            newEntry.Notes = parts[4].Trim();
                        }
                        lstImportedToc.Add(newEntry);
                    }
                }
                catch (Exception ex)
                {
                    var loc = lstImportedToc.Count() == 0 ? "at first line" : $"after {lstImportedToc[lstImportedToc.Count() - 1]}";
                    System.Windows.Forms.MessageBox.Show($"Exception {loc}  {ex.Message}");
                }
                LstTOC = new ObservableCollection<TOCEntry>(lstImportedToc.OrderBy(p => p.PageNo));
            }
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (LstTOC.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var itm in LstTOC)
                {
                    sb.Append($"{itm.PageNo}\t{itm.SongName}\t{itm.Composer}\t{itm.Date}\t{itm.Notes}\r\n");
                }
                System.Windows.Forms.Clipboard.SetText(sb.ToString(), System.Windows.Forms.TextDataFormat.Text);
            }
        }

        private void BtnClearTOC_Click(object sender, RoutedEventArgs e)
        {
            this.LstTOC.Clear();
        }

        private void TbxNumeric_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox txtbox)
            {
                var txt = txtbox.Text;
                txtbox.Text = new string(txt.Where(c => char.IsDigit(c) || c == '-').ToArray());
                txtbox.SelectionStart = txtbox.Text.Length;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var ndx = 0;
            // count # of TOC items < curpage
            foreach (var item in LstTOC)
            {
                if (item.PageNo >= _pdfViewerWindow.CurrentPageNumber)
                {
                    break;
                }
                ndx++;
            }
            this.lvTOC.SelectedIndex = ndx;
            this.lvTOC.ScrollIntoView(this.lvTOC.SelectedItem);
            // find the 1st one beyond, then go back 1
            //var ndxclosest = _pdfViewerWindow.currentPdfMetaData.dictToc.Keys.FindIndexOfFirstGTorEQTo(_pdfViewerWindow.CurrentPageNumber);

            //if (ndxclosest > 0 && ndxclosest < _pdfViewerWindow.currentPdfMetaData.dictToc.Count)
            //{
            //    var key = _pdfViewerWindow.currentPdfMetaData.dictToc.Keys[ndxclosest - 1];
            //    var tocitem = _pdfViewerWindow.currentPdfMetaData.dictToc[key];


            //}

        }
    }
}
