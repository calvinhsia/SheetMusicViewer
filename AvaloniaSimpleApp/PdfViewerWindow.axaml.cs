using Avalonia.Controls;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AvaloniaSimpleApp;

public partial class PdfViewerWindow : Window, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    private int _currentPageNumber;
    private int _touchCount;
    private bool _show2Pages = true;
    private bool _pdfUIEnabled;
    private string _pdfTitle = string.Empty;
    private string _description0 = string.Empty;
    private string _description1 = string.Empty;

    public PdfViewerWindow()
    {
        InitializeComponent();
        DataContext = this;
        
        // Initialize with some test values
        PdfTitle = "Sample PDF Document";
        CurrentPageNumber = 1;
        MaxPageNumberMinus1 = 99;
        PdfUIEnabled = true;
        Description0 = "Page 1 Description";
        Description1 = "Page 2 Description";
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public int CurrentPageNumber
    {
        get => _currentPageNumber;
        set
        {
            _currentPageNumber = value;
            OnPropertyChanged();
        }
    }

    public int MaxPageNumberMinus1 { get; set; }

    public int NumPagesPerView => _show2Pages ? 2 : 1;

    public bool Show2Pages
    {
        get => _show2Pages;
        set
        {
            _show2Pages = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NumPagesPerView));
        }
    }

    public bool PdfUIEnabled
    {
        get => _pdfUIEnabled;
        set
        {
            _pdfUIEnabled = value;
            OnPropertyChanged();
        }
    }

    public string PdfTitle
    {
        get => _pdfTitle;
        set
        {
            _pdfTitle = value;
            OnPropertyChanged();
        }
    }

    public string Description0
    {
        get => _description0;
        set
        {
            _description0 = value;
            OnPropertyChanged();
        }
    }

    public string Description1
    {
        get => _description1;
        set
        {
            _description1 = value;
            OnPropertyChanged();
        }
    }

    public int TouchCount
    {
        get => _touchCount;
        set
        {
            _touchCount = value;
            OnPropertyChanged();
        }
    }
}
