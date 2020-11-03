using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Printing;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace SheetMusicViewer
{
    public partial class PdfViewerWindow : Window, INotifyPropertyChanged
    {
        public const string MyAppName = "MyPdfViewer";
        public event PropertyChangedEventHandler PropertyChanged;
        // zoom gesture: https://stackoverflow.com/questions/25861840/zoom-pinch-detection-in-a-wpf-usercontrol

        //public static readonly RoutedEvent PdfExceptionEvent =
        //    EventManager.RegisterRoutedEvent("PdfExceptionEvent",
        //        RoutingStrategy.Bubble,
        //        typeof(EventHandler<PdfExceptionEventAgs>), typeof(PdfViewerWindow));

        public event EventHandler<PdfExceptionEventAgs> PdfExceptionEvent;
        public class PdfExceptionEventAgs : EventArgs
        {
            public Exception ErrorException { get; private set; }
            public string Message { get; set; }
            public PdfExceptionEventAgs(string Message, Exception errException)
            {
                ErrorException = errException;
                this.Message = Message;
            }
        }
        public bool IsTesting; // if testing, we don't want to save bookmarks, lastpageno, etc.
        public void OnException(string Message, Exception ex)
        {
            var args = new PdfExceptionEventAgs(Message, ex);
            // Debugger.Break();
            PdfExceptionEvent.Invoke(this, args);
        }

        public void OnMyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private int _CurrentPageNumber;
        /// <summary>
        /// This is the page number. it ranges from <see cref="PdfMetaData.PageNumberOffset"/> to  <see cref="PdfMetaData.MaxPageNum"/>
        /// </summary>
        public int CurrentPageNumber
        {
            get { return _CurrentPageNumber; }
            set
            {
                _CurrentPageNumber = value;
                OnMyPropertyChanged();
            }
        }
        public int MaxPageNumber { get { return currentPdfMetaData == null ? 0 : (int)currentPdfMetaData.MaxPageNum; } }
        public int MaxPageNumberMinus1 { get { return currentPdfMetaData == null ? 0 : (int)currentPdfMetaData.MaxPageNum - 1; } }
        public string PdfTitle
        {
            get
            {
                var title = string.Empty;
                if (currentPdfMetaData != null)
                {
                    title = currentPdfMetaData?.GetFullPathFileFromVolno(volNo: 0, MakeRelative: true);
                    if (currentPdfMetaData.IsSinglesFolder)
                    {
                        title = System.IO.Path.GetDirectoryName(title);
                    }
                }
                return title;
            }
        }

        public BitmapImage ImgThumbImage { get { return currentPdfMetaData?.bitmapImageCache; } }
        public string Description0 => currentPdfMetaData?.GetDescription(CurrentPageNumber);
        public string Description1 => currentPdfMetaData?.GetDescription(CurrentPageNumber + 1);
        internal bool _fShow2Pages = true;
        public bool Show2Pages
        {
            get { return _fShow2Pages; }
            set
            {
                _fShow2Pages = value;
                chkFav1.Visibility = value ? Visibility.Visible : Visibility.Hidden;
                chkInk1.Visibility = value ? Visibility.Visible : Visibility.Hidden;
                txtDesc1.Visibility = value ? Visibility.Visible : Visibility.Hidden;
                this.Dispatcher.InvokeAsync(async () => await ShowPageAsync(CurrentPageNumber, ClearCache: true, resetRenderTransform: true));
                OnMyPropertyChanged();
                OnMyPropertyChanged("NumPagesPerView");
            }
        }
        public int NumPagesPerView => _fShow2Pages ? 2 : 1;

        public bool PdfUIEnabled { get { return currentPdfMetaData != null; } set { OnMyPropertyChanged(); } }

        internal string _RootMusicFolder;
        internal List<PdfMetaData> lstPdfMetaFileData = new List<PdfMetaData>();
        internal List<string> lstFolders = new List<string>();

        internal PdfMetaData currentPdfMetaData = null;
        internal static PdfViewerWindow s_pdfViewerWindow;
        internal MyInkCanvas[] inkCanvas = new MyInkCanvas[2];

        internal PageCache _pageCache;
        public PdfViewerWindow(string rootFolderForTesting)
        {
            this._RootMusicFolder = rootFolderForTesting;
            this.InitializePdfViewerWindow();
        }

        private void InitializePdfViewerWindow()
        {
            InitializeComponent();
            s_pdfViewerWindow = this;
            _pageCache = new PageCache(this);
            this.DataContext = this;
            this.Width = Properties.Settings.Default.MainWindowSize.Width;
            this.Height = Properties.Settings.Default.MainWindowSize.Height;
            this.Top = Properties.Settings.Default.MainWindowPos.Height;
            this.Left = Properties.Settings.Default.MainWindowPos.Width;
            if (string.IsNullOrEmpty(this._RootMusicFolder))
            {
                var mruRootFolder = Properties.Settings.Default.RootFolderMRU;
                if (mruRootFolder != null && mruRootFolder.Count > 0)
                {
                    this._RootMusicFolder = Properties.Settings.Default.RootFolderMRU[0];
                }
                //            this._RootMusicFolder = @"C:\Users\calvinh\OneDrive\Documents\SheetMusic\Jazz";
                this.Show2Pages = Properties.Settings.Default.Show2Pages;
                this.chkFullScreen.IsChecked = Properties.Settings.Default.IsFullScreen;
            }
            PdfExceptionEvent += (o, e) =>
            {
                var logfile = System.IO.Path.Combine(_RootMusicFolder, $"{MyAppName}.log");
                var dt = DateTime.Now.ToString("MM/dd/yy hh:mm:ss");
                File.AppendAllText(logfile, $"\r\n{dt} {Environment.GetEnvironmentVariable("COMPUTERNAME")} {e.Message} {e.ErrorException} ");
                MessageBox.Show(e.Message + "\r\r" + e.ErrorException.ToString());
            };


            this.Closed += (o, e) =>
            {
                Properties.Settings.Default.Show2Pages = Show2Pages;
                Properties.Settings.Default.LastPDFOpen = currentPdfMetaData?.GetFullPathFileFromVolno(volNo: 0, MakeRelative: true);
                Properties.Settings.Default.MainWindowPos = new System.Drawing.Size((int)this.Left, (int)this.Top);
                Properties.Settings.Default.MainWindowSize = new System.Drawing.Size((int)this.ActualWidth, (int)this.ActualHeight);
                Properties.Settings.Default.IsFullScreen = this.chkFullScreen.IsChecked == true;
                Properties.Settings.Default.Save();
                CloseCurrentPdfFile();
                /*
0:000> k
# ChildEBP RetAddr  
00 00afe7c0 775a2949 ntdll!NtWaitForSingleObject+0xc [minkernel\ntdll\wow6432\objfre\i386\usrstubs.asm @ 129] 
01 00afe834 775a28a2 KERNELBASE!WaitForSingleObjectEx+0x99
*** ERROR: Symbol file could not be found.  Defaulted to export symbols for nvwgf2um.dll - 
02 00afe848 5ca830a4 KERNELBASE!WaitForSingleObject+0x12
WARNING: Stack unwind information not available. Following frames may be wrong.
03 00afed7c 5ca8445a nvwgf2um!OpenAdapter12+0x163614
04 00afed98 5ca8477b nvwgf2um!OpenAdapter12+0x1649ca
05 00afedc4 5c9c99c4 nvwgf2um!OpenAdapter12+0x164ceb
06 00afedd0 5e9f3405 nvwgf2um!OpenAdapter12+0xa9f34
07 00afee00 5e9f3375 d3d11!NDXGI::CDevice::DestroyDriverInstance+0x55 [onecoreuap\windows\directx\dxg\d3d11\d3dcore\lowfreq\dxgidevice.cpp @ 1253] 
08 00afee2c 5e9f3300 d3d11!CContext::LUCBeginLayerDestruction+0x71 [onecoreuap\windows\directx\dxg\d3d11\d3dcore\lowfreq\device.cpp @ 10510] 
09 00afee34 5ea0de14 d3d11!CBridgeImpl<ILayeredUseCounted,ID3D11LayeredUseCounted,CLayeredObject<CContext> >::LUCBeginLayerDestruction+0x10 [internal\onecoreuapwindows\private\inc\directx\dxg\layered.hpp @ 149] 
0a (Inline) -------- d3d11!NOutermost::CDeviceChild::LUCBeginLayerDestruction+0x2e [onecoreuap\windows\directx\dxg\d3d11\d3dcore\lowfreq\outermost.cpp @ 387] 
0b (Inline) -------- d3d11!CUseCountedObject<NOutermost::CDeviceChild>::LUCBeginLayerDestruction+0x2e [onecoreuap\windows\directx\dxg\d3d11\d3dcore\inc\outermost.inl @ 471] 
0c (Inline) -------- d3d11!NOutermost::CDeviceChild::FinalRelease+0x166 [onecoreuap\windows\directx\dxg\d3d11\d3dcore\inc\outermost.inl @ 819] 
0d (Inline) -------- d3d11!CUseCountedObject<NOutermost::CDeviceChild>::FinalRelease+0x166 [onecoreuap\windows\directx\dxg\d3d11\d3dcore\inc\outermost.inl @ 261] 
0e (Inline) -------- d3d11!CUseCountedObject<NOutermost::CDeviceChild>::{dtor}+0x166 [onecoreuap\windows\directx\dxg\d3d11\d3dcore\inc\outermost.inl @ 268] 
0f (Inline) -------- d3d11!CUseCountedObject<NOutermost::CDeviceChild>::Delete+0x17b [onecoreuap\windows\directx\dxg\d3d11\d3dcore\inc\outermost.inl @ 500] 
10 00afee58 5ea0773e d3d11!CUseCountedObject<NOutermost::CDeviceChild>::UCDestroy+0x184 [onecoreuap\windows\directx\dxg\d3d11\d3dcore\inc\outermost.inl @ 457] 
11 00afee80 5e9f35c2 d3d11!CUseCountedObject<NOutermost::CDeviceChild>::UCReleaseUse+0xde [onecoreuap\windows\directx\dxg\d3d11\d3dcore\inc\outermost.inl @ 447] 
12 00afee8c 5e9f3574 d3d11!CLockOwnerChild<CDevice,0>::UCReleaseUse+0x16 [internal\onecoreuapwindows\private\inc\directx\dxg\layered.inl @ 415] 
13 00afeea4 5ea25980 d3d11!CDevice::LLOBeginLayerDestruction+0xf4 [onecoreuap\windows\directx\dxg\d3d11\d3dcore\lowfreq\device.cpp @ 10290] 
14 00afeeac 5ea25add d3d11!CBridgeImpl<ILayeredLockOwner,ID3D11LayeredDevice,CLayeredObject<CDevice> >::LLOBeginLayerDestruction+0x10 [internal\onecoreuapwindows\private\inc\directx\dxg\layered.hpp @ 138] 
15 00afeed8 5ea259d0 d3d11!NDXGI::CDevice::LLOBeginLayerDestruction+0x109 [onecoreuap\windows\directx\dxg\d3d11\d3dcore\inc\dxgidevice.inl @ 556] 
16 00afeee0 5ea259b7 d3d11!CBridgeImpl<ILayeredLockOwner,ID3D11LayeredDevice,CLayeredObject<NDXGI::CDevice> >::LLOBeginLayerDestruction+0x10 [internal\onecoreuapwindows\private\inc\directx\dxg\layered.hpp @ 138] 
17 00afeef0 5ea25b0c d3d11!NOutermost::CDevice::LLOBeginLayerDestruction+0x27 [onecoreuap\windows\directx\dxg\d3d11\d3dcore\lowfreq\outermost.cpp @ 80] 
18 (Inline) -------- d3d11!NOutermost::CDevice::FinalRelease+0x13 [onecoreuap\windows\directx\dxg\d3d11\d3dcore\inc\outermost.inl @ 182] 
19 (Inline) -------- d3d11!TComObject<NOutermost::CDevice>::FinalRelease+0x13 [onecoreuap\windows\directx\dxg\d3d11\d3dcore\inc\outermost.inl @ 48] 
1a (Inline) -------- d3d11!TComObject<NOutermost::CDevice>::{dtor}+0x17 [onecoreuap\windows\directx\dxg\d3d11\d3dcore\inc\outermost.inl @ 55] 
1b 00afef04 5ea0db7c d3d11!TComObject<NOutermost::CDevice>::`scalar deleting destructor'+0x1c
1c (Inline) -------- d3d11!TComObject<NOutermost::CDevice>::Release+0x2e [onecoreuap\windows\directx\dxg\d3d11\d3dcore\inc\outermost.inl @ 78] 
1d (Inline) -------- d3d11!ATL::CComObjectRootBase::OuterRelease+0x47 [sdk\inc\atlmfc\atlcom.h @ 2254] 
1e 00afef1c 0ff0e3c1 d3d11!CLayeredObject<CDevice>::CContainedObject::Release+0x4c [internal\onecoreuapwindows\private\inc\directx\dxg\layered.hpp @ 207] 
1f 00afef2c 0fea6087 Windows_Data_Pdf!Microsoft::WRL::ComPtr<ABI::Windows::Security::Cryptography::Certificates::ICertificate>::InternalRelease+0x1e [sdk\inc\wrl\client.h @ 176] 
20 00afef3c 0ff9ffac Windows_Data_Pdf!std::_Ref_count_base::_Decref+0x27 [internal\sdk\inc\ucrt\stl120\memory @ 808] 
21 (Inline) -------- Windows_Data_Pdf!std::_Ptr_base<Windows::Data::Pdf::CPdfStreamRenderer>::_Decref+0xc [internal\sdk\inc\ucrt\stl120\memory @ 1081] 
22 (Inline) -------- Windows_Data_Pdf!std::shared_ptr<Windows::Data::Pdf::CPdfStreamRenderer>::{dtor}+0xc [internal\sdk\inc\ucrt\stl120\memory @ 1374] 
23 00afef50 0fea6087 Windows_Data_Pdf!Windows::Data::Pdf::CPdfStatics::~CPdfStatics+0xc6
24 00afef60 77039272 Windows_Data_Pdf!std::_Ref_count_base::_Decref+0x27 [internal\sdk\inc\ucrt\stl120\memory @ 808] 
25 00afef9c 7703917e ucrtbase!<lambda_f03950bc5685219e0bcd2087efbe011e>::operator()+0xc2 [minkernel\crts\ucrt\src\appcrt\startup\onexit.cpp @ 206] 
26 00afefd0 7703914a ucrtbase!__crt_seh_guarded_call<int>::operator()<<lambda_69a2805e680e0e292e8ba93315fe43a8>,<lambda_f03950bc5685219e0bcd2087efbe011e> &,<lambda_03fcd07e894ec930e3f35da366ca99d6> >+0x30 [minkernel\crts\ucrt\devdiv\vcruntime\inc\internal_shared.h @ 204] 
27 (Inline) -------- ucrtbase!__acrt_lock_and_call+0x17 [minkernel\crts\ucrt\inc\corecrt_internal.h @ 970] 
28 00afeff0 77037397 ucrtbase!_execute_onexit_table+0x2a [minkernel\crts\ucrt\src\appcrt\startup\onexit.cpp @ 231] 
29 00aff028 0ff1ec14 ucrtbase!__crt_state_management::wrapped_invoke<int (__cdecl*)(_onexit_table_t *),_onexit_table_t *,int>+0x56 [minkernel\crts\ucrt\inc\corecrt_internal_state_isolation.h @ 362] 
2a 00aff030 0ff1e5a7 Windows_Data_Pdf!__scrt_dllmain_uninitialize_c+0x13 [minkernel\crts\ucrt\devdiv\vcstartup\src\utility\utility.cpp @ 398] 
2b 00aff064 0ff1e470 Windows_Data_Pdf!dllmain_crt_process_detach+0x35 [minkernel\crts\ucrt\devdiv\vcstartup\src\startup\dll_dllmain.cpp @ 107] 
2c 00aff070 0ff1e6a7 Windows_Data_Pdf!dllmain_crt_dispatch+0x50 [minkernel\crts\ucrt\devdiv\vcstartup\src\startup\dll_dllmain.cpp @ 144] 
2d 00aff0b0 0ff1e74e Windows_Data_Pdf!dllmain_dispatch+0xaf [minkernel\crts\ucrt\devdiv\vcstartup\src\startup\dll_dllmain.cpp @ 211] 
2e 00aff0c4 77710466 Windows_Data_Pdf!_DllMainCRTStartup+0x1e [minkernel\crts\ucrt\devdiv\vcstartup\src\startup\dll_dllmain.cpp @ 252] 
2f 00aff0e4 776dd40c ntdll!LdrxCallInitRoutine+0x16 [minkernel\ntdll\i386\ldrthunk.asm @ 91] 
30 00aff130 776eb044 ntdll!LdrpCallInitRoutine+0x55 [minkernel\ntdll\ldr.c @ 208] 
31 00aff1c8 776feed5 ntdll!LdrShutdownProcess+0xf4 [minkernel\ntdll\ldrinit.c @ 6207] 
32 00aff298 762a4b92 ntdll!RtlExitUserProcess+0xb5 [minkernel\ntdll\rtlstrt.c @ 1585] 
33 00aff2ac 74c314a0 kernel32!ExitProcessImplementation+0x12
34 00aff52c 74c315c3 mscoreei!RuntimeDesc::ShutdownAllActiveRuntimes+0x34c [f:\dd\ndp\clr\src\dlls\shim\shimapi.cpp @ 828] 
35 00aff538 746925b4 mscoreei!CLRRuntimeHostInternalImpl::ShutdownAllRuntimesThenExit+0x13 [f:\dd\ndp\clr\src\dlls\shim\shimapi.cpp @ 4262] 
36 00aff570 7469253a clr!EEPolicy::ExitProcessViaShim+0x79 [f:\dd\ndp\clr\src\vm\eepolicy.cpp @ 616] 
37 00aff7a4 746a6511 clr!SafeExitProcess+0x137 [f:\dd\ndp\clr\src\vm\eepolicy.cpp @ 584] 
38 00aff7b4 746a6558 clr!HandleExitProcessHelper+0x63 [f:\dd\ndp\clr\src\vm\eepolicy.cpp @ 724] 
39 00aff7c8 746a5869 clr!EEPolicy::HandleExitProcess+0x50 [f:\dd\ndp\clr\src\vm\eepolicy.cpp @ 1190] 
3a 00aff808 74675a0c clr!_CorExeMainInternal+0x1b1 [f:\dd\ndp\clr\src\vm\ceemain.cpp @ 2864] 
3b 00aff844 74c2d93b clr!_CorExeMain+0x4d [f:\dd\ndp\clr\src\vm\ceemain.cpp @ 2776] 
3c 00aff884 74cae80e mscoreei!_CorExeMain+0x10e [f:\dd\ndp\clr\src\dlls\shim\shim.cpp @ 6420] 
3d 00aff894 74cb43f8 mscoree!ShellShim__CorExeMain+0x9e [onecore\com\netfx\windowsbuilt\shell_shim\v2api.cpp @ 277] 
3e 00aff89c 762a0179 mscoree!_CorExeMain_Exported+0x8 [onecore\com\netfx\windowsbuilt\shell_shim\v2api.cpp @ 1223] 
3f 00aff8ac 7770662d kernel32!BaseThreadInitThunk+0x19
40 00aff908 777065fd ntdll!__RtlUserThreadStart+0x2f [minkernel\ntdll\rtlstrt.c @ 1163] 
41 00aff918 00000000 ntdll!_RtlUserThreadStart+0x1b [minkernel\ntdll\rtlstrt.c @ 1080] 
                 */
                Environment.Exit(0);
            };
            this.Loaded += MainWindow_LoadedAsync;
        }

        public PdfViewerWindow()
        {
            this.InitializePdfViewerWindow();
        }

        private async void MainWindow_LoadedAsync(object sender, RoutedEventArgs e)
        {
            // https://blogs.windows.com/buildingapps/2017/01/25/calling-windows-10-apis-desktop-application/#RWYkd5C4WTeEybol.97
            try
            {
                //var folder = @"C:\Users\calvinh\OneDrive\Documents\SheetMusic\Pop";
                //var pdfName = "150 of the Most Beautiful Songs Ever 0.pdf";
                //var pathPdf = System.IO.Path.Combine(folder, pdfName);
                //await CombinePDFsToASinglePdfAsync(pathPdf);
                //return;
                this.ChkfullScreenToggled(this, new RoutedEventArgs() { RoutedEvent = (this.chkFullScreen.IsChecked == true ? CheckBox.CheckedEvent : CheckBox.UncheckedEvent) });
                this.Title = MyAppName; // for task bar
                var lastPdfOpen = Properties.Settings.Default.LastPDFOpen;
                if (string.IsNullOrEmpty(_RootMusicFolder))
                {
                    await ChooseMusic();
                }
                else
                {
                    (lstPdfMetaFileData, lstFolders) = await PdfMetaData.LoadAllPdfMetaDataFromDiskAsync(_RootMusicFolder);
                    this.btnChooser.IsEnabled = true;
                    var lastPdfMetaData = lstPdfMetaFileData.Where(p => p.GetFullPathFileFromVolno(volNo: 0, MakeRelative: true) == lastPdfOpen).FirstOrDefault();
                    if (lastPdfMetaData != null)
                    {
                        await LoadPdfFileAndShowAsync(lastPdfMetaData, lastPdfMetaData.LastPageNo);
                        await GetAllBitMapImagesAsync(); // while showing a doc, we can get the bmps
                    }
                    else
                    {
                        await ChooseMusic();
                    }
                }
            }
            catch (Exception ex)
            {
                this.Content = ex.ToString();
            }
        }

        async public Task GetAllBitMapImagesAsync()
        {
            foreach (var pdfmetadataitem in lstPdfMetaFileData)
            {
                await pdfmetadataitem.GetBitmapImageThumbnailAsync();
            }
        }

        internal async Task LoadPdfFileAndShowAsync(PdfMetaData pdfMetaData, int PageNo)
        {
            CloseCurrentPdfFile();
            currentPdfMetaData = pdfMetaData;
            currentPdfMetaData.InitializeListPdfDocuments();
            _DisableSliderValueChanged = true;
            this.slider.Minimum = currentPdfMetaData.PageNumberOffset;
            this.slider.Maximum = this.MaxPageNumber - 1;
            this.slider.LargeChange = Math.Max((int)(.1 * (this.MaxPageNumber - this.slider.Minimum)), 1); // 10%
            this.slider.Value = PageNo;
            _DisableSliderValueChanged = false;
            //this.slider.IsDirectionReversed = true;
            this.PdfUIEnabled = true;
            this.SetTitle();
            OnMyPropertyChanged(nameof(PdfTitle));
            OnMyPropertyChanged(nameof(ImgThumbImage));
            OnMyPropertyChanged(nameof(MaxPageNumber));
            OnMyPropertyChanged(nameof(MaxPageNumberMinus1));
            await ShowPageAsync(PageNo, ClearCache: true, resetRenderTransform: true);
        }

        internal void SetTitle()
        {
            var strTitle = $"{MyAppName} {currentPdfMetaData?.GetFullPathFileFromVolno(volNo: 0, MakeRelative: false)}";
#if DEBUG

            if (MyInkCanvas._NumInstances > 2)
            {
                strTitle += $" InkInstances= {MyInkCanvas._NumInstances}";
            }
#endif //DEBUG
            this.Title = strTitle;
            OnMyPropertyChanged(nameof(Title));
        }


        /// <summary>
        /// Show a page or 2
        /// </summary>
        /// <param name="pageNo"></param>
        /// <param name="ClearCache"></param>
        internal async Task ShowPageAsync(int pageNo, bool ClearCache, bool forceRedraw = false, bool resetRenderTransform = false)
        {
            try
            {
                if (ClearCache)
                {
                    _pageCache.ClearCache();
                }
                if (currentPdfMetaData == null)
                {
                    this.dpPage.Children.Clear();
                    return;
                }
                if (forceRedraw || resetRenderTransform)
                {
                    this.dpPage.Children.Clear();
                }
                if (resetRenderTransform)
                {
                    this.dpPage.RenderTransform = Transform.Identity;
                }
                if (pageNo < this.slider.Minimum)
                {
                    pageNo = (int)this.slider.Minimum;
                }
                if (pageNo >= MaxPageNumber)
                {
                    pageNo -= NumPagesPerView;
                    if (pageNo < 0)
                    {
                        pageNo = (int)this.slider.Minimum;
                    }
                }
                _DisableSliderValueChanged = true;
                this.CurrentPageNumber = pageNo;
                _DisableSliderValueChanged = false;
                // we want to add items to the cache, but in a priority order: high pri are the 1 or 2 current pages.
                var cacheEntryCurrentPage = _pageCache.TryAddCacheEntry(pageNo);
                var cacheEntryNextPage = _pageCache.TryAddCacheEntry(pageNo + 1);
                if (NumPagesPerView == 1)
                {
                    _pageCache.TryAddCacheEntry(pageNo + 2);
                    _pageCache.TryAddCacheEntry(pageNo - 1);
                }
                else
                {
                    _pageCache.TryAddCacheEntry(pageNo + 2); // order is important
                    _pageCache.TryAddCacheEntry(pageNo + 3);
                    _pageCache.TryAddCacheEntry(pageNo - 1);
                    _pageCache.TryAddCacheEntry(pageNo - 2);
                }
                if (this.CurrentPageNumber == pageNo) // user might have typed ahead
                {
                    try
                    {
                        if (cacheEntryCurrentPage != null && cacheEntryCurrentPage.task.IsCanceled)
                        {
                            cacheEntryCurrentPage = _pageCache.TryAddCacheEntry(pageNo);
                        }
                        if (cacheEntryCurrentPage == null)
                        {
                            return;
                        }
                        var gridContainer = new Grid();
                        var bitmapimageCurPage = await cacheEntryCurrentPage.task;
                        if (!cacheEntryCurrentPage.task.IsCompleted)
                        {
                            return;
                        }
                        //                        var imageCurPage = new Image() { Source = bitmapimageCurPage };
                        inkCanvas[0] = new MyInkCanvas(bitmapimageCurPage.Item1, this, chkInk0.IsChecked == true, CurrentPageNumber);
                        //chkInk0.Checked += inkCanvas[0].ChkInkToggledOnCanvas; //cause leak via WPF RoutedEvents
                        /*
->chkInk0 = System.Windows.Controls.CheckBox 0x03148ed8 (248)
 ->_dispatcher = System.Windows.Threading.Dispatcher 0x030cac08 (132)
 ->_dType = System.Windows.DependencyObjectType 0x03122cf4 (20)
 ->_effectiveValues = ValueType[](Count=22) 0x031d2680 (188)
  ->MS.Utility.FrugalMap 0x031d2790 (12)
  ->System.Boolean 0x030efcb4 (12)
  ->System.Boolean 0x031490a8 (12)
  ->System.Collections.Generic.List`1<System.Windows.DependencyObject>(Count=8) 0x031acbe4 (24)
  ->System.Collections.Hashtable 0x0319aad4 (52)
  ->System.String 0x03149078 (28)  chkInk0
  ->System.String 0x03149094 (20)  Ink
  ->System.String 0x03149308 (86)  Turn on/off inking with mouse or pen
  ->System.Windows.Controls.ControlTemplate 0x03144eac (132)
  ->System.Windows.DeferredThemeResourceReference 0x0313c09c (24)
  ->System.Windows.EventHandlersStore 0x03148ffc (12)
   ->_entries = MS.Utility.ThreeObjectMap 0x03149054 (36)
    ->_entry0 = MS.Utility.FrugalObjectList`1<System.Windows.RoutedEventHandlerInfo> 0x03149008 (12)
     ->_listStore = MS.Utility.ArrayItemList`1<System.Windows.RoutedEventHandlerInfo> 0x034bb3d4 (16)
      ->_entries = ValueType[](Count=9) 0x034bb3e4 (84)
       ->System.Windows.RoutedEventHandler(Target=WpfPdfViewer.MyInkCanvas 0x03445c94) 0x0344ec34 (32)
       ->System.Windows.RoutedEventHandler(Target=WpfPdfViewer.MyInkCanvas 0x03455c00) 0x0345919c (32)
       ->System.Windows.RoutedEventHandler(Target=WpfPdfViewer.MyInkCanvas 0x0345eba8) 0x03462144 (32)
       ->System.Windows.RoutedEventHandler(Target=WpfPdfViewer.MyInkCanvas 0x03487b38) 0x0348b0e0 (32)
       ->System.Windows.RoutedEventHandler(Target=WpfPdfViewer.MyInkCanvas 0x03490c64) 0x034942cc (32)
       ->System.Windows.RoutedEventHandler(Target=WpfPdfViewer.MyInkCanvas 0x034b7ccc) 0x034bb3b4 (32)
       ->System.Windows.RoutedEventHandler(Target=WpfPdfViewer.MyInkCanvas 0x034c46e0) 0x034c7c7c (32)
       ->System.Windows.RoutedEventHandler(Target=WpfPdfViewer.MyInkCanvas 0x034fe118) 0x035016c0 (32)
                         * */
                        //WeakEventManager<CheckBox, RoutedEventArgs>.AddHandler(chkInk0, "Checked", inkCanvas[0].ChkInkToggled); // this will use WeakEvents and thus won't leak.
                        var gridCurPage = new Grid();
                        gridCurPage.Children.Add(inkCanvas[0]);
                        gridContainer.Children.Add(gridCurPage);
                        if (NumPagesPerView != 1)
                        {
                            if (cacheEntryNextPage != null && cacheEntryNextPage.task.IsCanceled)
                            {
                                cacheEntryNextPage = _pageCache.TryAddCacheEntry(pageNo + 1);
                            }
                            if (cacheEntryNextPage != null)
                            {
                                gridContainer.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(this.dpPage.ActualWidth / NumPagesPerView) });
                                gridContainer.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(this.dpPage.ActualWidth / NumPagesPerView) });
                                var bitmapimageNextPage = await cacheEntryNextPage.task;
                                if (!cacheEntryNextPage.task.IsCompleted)
                                {
                                    return;
                                }
                                inkCanvas[1] = new MyInkCanvas(bitmapimageNextPage.Item1, this, chkInk1.IsChecked == true, CurrentPageNumber + 1);
                                //chkInk1.Checked += inkCanvas[1].ChkInkToggled;  // cause leak
                                inkCanvas[0].HorizontalAlignment = HorizontalAlignment.Right;
                                inkCanvas[1].HorizontalAlignment = HorizontalAlignment.Left; // put righthand page close to middle
                                var gridNextPage = new Grid();
                                gridNextPage.Children.Add(inkCanvas[1]);
                                Grid.SetColumn(gridNextPage, 1);
                                gridContainer.Children.Add(gridNextPage);
                            }
                        }
                        this.dpPage.Children.Clear();
                        this.dpPage.Children.Add(gridContainer);
                        OnMyPropertyChanged(nameof(Description0));
                        this.chkFavoriteEnabled = false;
                        chkFav0.IsChecked = currentPdfMetaData.IsFavorite(pageNo);
                        if (NumPagesPerView > 1)
                        {
                            OnMyPropertyChanged(nameof(Description1));
                            chkFav1.IsChecked = currentPdfMetaData.IsFavorite(pageNo + 1);
                        }
                        this.chkFavoriteEnabled = true;
                    }
                    catch (OperationCanceledException ex)
                    {
                        ex.ToString();
                    }
                    if (this.CurrentPageNumber != pageNo) // user typed ahead ?
                    {
                        _pageCache.PurgeIfNecessary(pageNo);
                    }
                }
                else
                {
                    _pageCache.PurgeIfNecessary(pageNo);
                }
            }
            catch (Exception ex)
            {
                this.dpPage.Children.Clear();

                //RaiseEvent(new PdfExceptionEventAgs(PdfExceptionEvent, this, null));
                System.Windows.Forms.MessageBox.Show($"Exception showing {currentPdfMetaData.GetFullPathFileFromPageNo(pageNo)}\r\n {ex.ToString()}");
                OnException($"Showing {currentPdfMetaData.GetFullPathFileFromPageNo(pageNo)}", ex);
                CloseCurrentPdfFile();
            }
        }

        CancellationTokenSource ctsPageScan;
        protected async override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            var elmWithFocus = Keyboard.FocusedElement;
            var isCtrlKeyDown = e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control);
            if (isCtrlKeyDown || !(elmWithFocus is TextBox) && !(elmWithFocus is Slider)) // tbx and slider should get the keystroke and process it 
            {
                switch (e.Key)
                {
                    case Key.System: // alt
                        if (e.SystemKey == Key.E)
                        {
                            //ImgThumb_MouseDown(this, null);
                            //e.Handled = true;
                        }
                        break;
                    case Key.Home:
                        await ShowPageAsync(currentPdfMetaData.PageNumberOffset, ClearCache: false);
                        break;
                    case Key.End:
                        await ShowPageAsync(this.MaxPageNumber - 1, ClearCache: false);
                        break;
                    case Key.Up:
                    case Key.PageUp:
                    case Key.Left:
                        NavigateAsync(-NumPagesPerView);
                        e.Handled = true;
                        break;
                    case Key.Down:
                    case Key.PageDown:
                    case Key.Right:
                        if (isCtrlKeyDown)
                        {
                            if (ctsPageScan == null)
                            {
                                ctsPageScan = new CancellationTokenSource();
                                var done = false;
                                IsTesting = true;
                                _pageCache.ClearCache();
                                while (!done)
                                {
                                    CurrentPageNumber = currentPdfMetaData.PageNumberOffset;
                                    async Task LoopCurrentBook()
                                    {
                                        for (int pg = CurrentPageNumber + NumPagesPerView; pg < MaxPageNumber; pg += NumPagesPerView)
                                        {
                                            if (ctsPageScan.IsCancellationRequested)
                                            {
                                                done = true;
                                                break;
                                            }
                                            await ShowPageAsync(pg, ClearCache: false);
                                            //                                            await Task.Delay(270); // allow enough time to render
                                        }
                                    }
                                    if (e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Alt))
                                    {
                                        foreach (var pdfMetadata in this.lstPdfMetaFileData)
                                        {
                                            await this.LoadPdfFileAndShowAsync(pdfMetadata, pdfMetadata.PageNumberOffset);
                                            await LoopCurrentBook();
                                            if (done)
                                            {
                                                break;
                                            }
                                        }
                                        done = true;
                                    }
                                    else
                                    {
                                        await LoopCurrentBook();
                                        done = true;
                                    }
                                }
                                IsTesting = false;
                                ctsPageScan = null;
                            }
                            else
                            {
                                ctsPageScan.Cancel();
                            }
                        }
                        else
                        {
                            NavigateAsync(NumPagesPerView);
                            e.Handled = true;
                        }
                        break;
                }
            }
        }
        private void ChkFavToggled(object sender, RoutedEventArgs e)
        {
            if (this.chkFavoriteEnabled)
            {
                var nameSender = ((CheckBox)sender).Name;
                var pgno = CurrentPageNumber + (nameSender == "chkFav0" ? 0 : 1);
                var isChked = e.RoutedEvent.Name == "Checked";
                currentPdfMetaData.ToggleFavorite(pgno, isChked);
            }
        }

        private async void ChkInkToggled(object sender, RoutedEventArgs e)
        {
            try
            {
                var nameSender = ((CheckBox)sender).Name;
                var pgno = CurrentPageNumber + (nameSender == "chkInk0" ? 0 : 1);
                var curCanvas = inkCanvas[pgno - CurrentPageNumber];
                curCanvas.ChkInkToggledOnCanvas(sender, e);
                await ShowPageAsync(CurrentPageNumber, ClearCache: false, forceRedraw: true);
            }
            catch (Exception)
            {
            }
        }

        async void BtnRotate_Click(object sender, RoutedEventArgs e)
        {
            currentPdfMetaData.Rotate(CurrentPageNumber);
            await ShowPageAsync(CurrentPageNumber, ClearCache: true, forceRedraw: true, resetRenderTransform: true);
        }

        async void NavigateAsync(int delta)
        {
            var newPageNo = CurrentPageNumber + delta;
            await ShowPageAsync(newPageNo, ClearCache: false);
        }

        DispatcherTimer _resizeTimer;
        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            if (currentPdfMetaData != null)
            {
                if (_resizeTimer == null)
                {
                    _resizeTimer = new DispatcherTimer()
                    {
                        Interval = TimeSpan.FromSeconds(1)
                    };
                    _resizeTimer.Tick += async (o, e) =>
                    {
                        _resizeTimer.IsEnabled = false;
                        await ShowPageAsync(CurrentPageNumber, ClearCache: true, resetRenderTransform: true);
                    };
                }
                _resizeTimer.Stop();
                _resizeTimer.Start();
            }
        }

        async void BtnPrevNext_Click(object sender, RoutedEventArgs e)
        {
            var isPrevious = sender is Button b && b.Name == "btnPrev";
            if (currentPdfMetaData.dictFav.Count > 0)
            {
                var ndxClosest = currentPdfMetaData.dictFav.Keys.FindIndexOfFirstGTorEQTo(CurrentPageNumber);
                if (!isPrevious)
                {
                    if (ndxClosest >= 0 && ndxClosest < currentPdfMetaData.dictFav.Count)
                    {
                        var pgNextFav = currentPdfMetaData.dictFav.Keys[ndxClosest];
                        if (pgNextFav == CurrentPageNumber)
                        {
                            ndxClosest++;
                            if (ndxClosest < currentPdfMetaData.dictFav.Count)
                            {
                                pgNextFav = currentPdfMetaData.dictFav.Keys[ndxClosest];
                            }
                        }
                        await ShowPageAsync(pgNextFav, ClearCache: false);
                    }
                }
                else
                { // prev fav
                    ndxClosest--;
                    if (ndxClosest >= 0 && ndxClosest < currentPdfMetaData.dictFav.Count)
                    {
                        var pgPriorNextFav = currentPdfMetaData.dictFav.Keys[ndxClosest];
                        if (pgPriorNextFav == CurrentPageNumber)
                        {
                            ndxClosest++;
                            if (ndxClosest < currentPdfMetaData.dictFav.Count)
                            {
                                pgPriorNextFav = currentPdfMetaData.dictFav.Keys[ndxClosest];
                            }
                        }
                        await ShowPageAsync(pgPriorNextFav, ClearCache: false);
                    }
                }
            }
            else
            {
                var delta = isPrevious ? -NumPagesPerView : NumPagesPerView;
                NavigateAsync(delta);
            }
        }

        int lastTouchTimeStamp = 0; //msecs wraps neg in 24.9 days  
        private void DpPage_TouchDown(object sender, TouchEventArgs e)
        {
            var diff = Math.Abs(e.Timestamp - lastTouchTimeStamp);
            if (diff > System.Windows.Forms.SystemInformation.DoubleClickTime) // == 500) // debounce
            {
                var pos = e.GetTouchPoint(this.dpPage).Position;
                e.Handled = OnMouseOrTouchDown(pos, e);
            }
            lastTouchTimeStamp = e.Timestamp;
        }
        private void DpPage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var diff = Math.Abs(e.Timestamp - lastTouchTimeStamp);
            if (diff > System.Windows.Forms.SystemInformation.DoubleClickTime) // == 500) // a touch can also send mousedown events. So filter out mousedown immediately after a touch
            {
                var pos = e.GetPosition(this.dpPage);
                e.Handled = OnMouseOrTouchDown(pos, e);
            }
        }

        private bool OnMouseOrTouchDown(Point pos, InputEventArgs e)
        {
            var handled = false;
            //            var thresh = 100;
            //if (e is TouchEventArgs) // for mouse input we don't do anything on double click, so 2 quick clicks should be 2 single clicks. For touch, we need to de-bounce (filter out 2 touches within 2 msecs)
            //{
            //    handled = true;
            //}
            // a touch sends both a touch and a mouse, (even if touch is handled) so we need to filter
            if (e is MouseButtonEventArgs || pos.Y > .75 * dpPage.ActualHeight) // must be bottom portion of page for touch: top part is for zooming
            {
                var delta = NumPagesPerView;
                if (NumPagesPerView > 1)
                {
                    var distToMiddle = Math.Abs(this.dpPage.ActualWidth / 2 - pos.X);
                    if (distToMiddle < this.dpPage.ActualWidth / 4)
                    {
                        delta = 1;
                    }
                }
                var leftSide = pos.X < this.dpPage.ActualWidth / 2;
                if (leftSide)
                {
                    delta = -delta;
                }
                NavigateAsync(delta);
                handled = true;
            }
            return handled;
        }

        private void DpPage_ManipulationStarting(object sender, ManipulationStartingEventArgs e)
        {
            //            e.IsSingleTouchEnabled = false;
            e.ManipulationContainer = this;
            e.Handled = true;

        }

        private void DpPage_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            // if we're inking, we don't want to be zooming too... makes a messs
            if (chkInk0.IsChecked == false && chkInk1.IsChecked == false)
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
                    Point center = new Point(e.ManipulationOrigin.X, e.ManipulationOrigin.Y); // new Point(element.ActualWidth / 2, element.ActualHeight / 2);
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
        }
        private void DpPage_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                var pos = e.GetPosition(this.dpPage);
                var matrix = ((MatrixTransform)this.dpPage.RenderTransform).Matrix;
                pos = matrix.Transform(pos);
                var delt = 1.3 * (e.Delta / 30.0);
                if (e.Delta < 0)
                {
                    delt = -1 / delt;
                }
                matrix.ScaleAt(delt, delt, pos.X, pos.Y);
                this.dpPage.RenderTransform = new MatrixTransform(matrix);
            }
            else
            {
                this.dpPage.RenderTransform = Transform.Identity;
            }
            e.Handled = true;
        }

        private void DpPage_ManipulationInertiaStarting(object sender, ManipulationInertiaStartingEventArgs e)
        {
        }

        async Task<bool> ChooseMusic()
        {
            var retval = false;
            this.btnChooser.IsEnabled = false;
            var win = new ChooseMusic(this);
            if (win.ShowDialog() == true)
            {
                var pdfMetaData = win.chosenPdfMetaData;
                if (pdfMetaData != null)
                {
                    await LoadPdfFileAndShowAsync(pdfMetaData, pdfMetaData.LastPageNo);
                    retval = true;
                }
                else
                {
                    CloseCurrentPdfFile();
                }
            }
            this.btnChooser.IsEnabled = true;
            return retval;

        }
        async void BtnChooser_Click(object sender, RoutedEventArgs e)
        {
            await ChooseMusic();
        }
        void BtnQuit_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
        internal void CloseCurrentPdfFile()
        {
            this.dpPage.Children.Clear();
            _pageCache.ClearCache();
            if (currentPdfMetaData != null)
            {
                currentPdfMetaData.LastPageNo = CurrentPageNumber;
                currentPdfMetaData.SaveIfDirty();
                currentPdfMetaData = null;
                CurrentPageNumber = 0;
            }
            this.Title = MyAppName;
            OnMyPropertyChanged(nameof(MaxPageNumber));
            OnMyPropertyChanged(nameof(MaxPageNumberMinus1));
            OnMyPropertyChanged(nameof(Title));
            OnMyPropertyChanged(nameof(PdfTitle));
            OnMyPropertyChanged(nameof(Description0));
            OnMyPropertyChanged(nameof(Description1));
            OnMyPropertyChanged(nameof(ImgThumbImage));
            this.PdfUIEnabled = false;
        }

        internal async static Task<DocumentViewer> CombinePDFsToASinglePdfAsync(string pathPdf)
        {
            await CombinePDFsToASinglePdfAsync("to quiet warning about unused func");
            //             //pdfSourceDoc = @"C:\Users\calvinh\OneDrive\Documents\SheetMusic\Ragtime\Collections\The Music of James Scott001.pdf";
            //rotation = Rotation.Rotate0;

            var fixedDoc = await ConvertMultiPdfToSingleAsync(pathPdf);
            //var fixedDoc = await ConvertMultiPdfToSingleAsync(
            //    @"C:\Users\calvinh\OneDrive\Documents\SheetMusic\FakeBooks\Ultimate Pop Rock Fake Book.pdf",
            //    Rotation.Rotate0,
            //    @"C:\Users\calvinh\OneDrive\Documents\SheetMusic\FakeBooks\Ultimate Pop Rock Fake Book 1.pdf",
            //    Rotation.Rotate180);
            var docvwr = new DocumentViewer
            {
                Document = fixedDoc
            };
            IDocumentPaginatorSource idps = fixedDoc;
            var pdlg = new PrintDialog();
            var queueName = "Microsoft Print to PDF";
            var pServer = new PrintServer();
            //            var pqueues = pServer.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local });
            pdlg.PrintQueue = new PrintQueue(pServer, queueName);
            pdlg.PrintDocument(idps.DocumentPaginator, "testprint");
            return docvwr;
        }

        async static Task<FixedDocument> ConvertMultiPdfToSingleAsync(string vol0)
        {
            var fixedDoc = new FixedDocument();
            //if (!string.IsNullOrEmpty(titlePage) && File.Exists(titlePage))
            //{
            //    var fTitle = await StorageFile.GetFileFromPathAsync(titlePage);
            //    var pdfDocTitle = await PdfDocument.LoadFromFileAsync(fTitle);
            //    var pgTitle = pdfDocTitle.GetPage(0);
            //    await AddPageToDocAsync(fixedDoc, pgTitle, rotationTitlePage);
            //}
            var fDone = false;
            int nVolNo = 0;
            var pdfDataFileToUse = vol0;
            while (!fDone)
            {
                StorageFile f = await StorageFile.GetFileFromPathAsync(pdfDataFileToUse);
                var pdfDoc = await PdfDocument.LoadFromFileAsync(f);
                var nPageCount = pdfDoc.PageCount;
                for (int i = 0; i < nPageCount; i++)
                {
                    using (var page = pdfDoc.GetPage((uint)i))
                    {
                        await AddPageToDocAsync(fixedDoc, page, (nVolNo == 0 ? Rotation.Rotate0 : Rotation.Rotate180));
                    }
                }
                if (!vol0.EndsWith("0.pdf"))
                {
                    break;
                }
                nVolNo++;
                pdfDataFileToUse = vol0.Replace("0.pdf", string.Empty) + nVolNo.ToString() + ".pdf";
                if (!File.Exists(pdfDataFileToUse))
                {
                    break;
                }
            }
            return fixedDoc;
        }

        private static async Task AddPageToDocAsync(FixedDocument fixedDoc, PdfPage page, Rotation rotation = Rotation.Rotate0)
        {
            var bmi = new BitmapImage();
            using (var strm = new InMemoryRandomAccessStream())
            {
                var rect = page.Dimensions.ArtBox;
                //var renderOpts = new PdfPageRenderOptions()
                //{
                //    DestinationWidth = (uint)rect.Height,
                //    DestinationHeight = (uint)rect.Width,
                //};

                await page.RenderToStreamAsync(strm);
                //var enc = new PngBitmapEncoder();
                //enc.Frames.Add(BitmapFrame.Create)
                bmi.BeginInit();
                bmi.StreamSource = strm.AsStream();
                bmi.CacheOption = BitmapCacheOption.OnLoad;
                bmi.Rotation = rotation;
                bmi.EndInit();

                var img = new Image()
                {
                    Source = bmi
                };

                var fixedPage = new FixedPage();
                fixedPage.Children.Add(img);
                fixedPage.Height = rect.Width;
                fixedPage.Width = rect.Height;
                var pc = new PageContent
                {
                    Child = fixedPage
                };

                fixedDoc.Pages.Add(pc);

                //var sp = new StackPanel();
                //var img = new Image()
                //{
                //    Source = bmi
                //};
                //sp.Children.Add(img);
                //sp.Measure(new Size(pdlg.PrintableAreaWidth, pdlg.PrintableAreaHeight));
                //sp.Arrange(new Rect(new Point(0, 0), sp.DesiredSize));
                //pdlg.PrintVisual(sp, "test");
            }
        }

        private void Slider_TouchDown(object sender, TouchEventArgs e)
        {
            e.Handled = true;
        }
        private bool _DisableSliderValueChanged;
        void OnSliderThumbDragstarted(object sender, RoutedEventArgs e)
        {
            _DisableSliderValueChanged = true;
        }
        void OnSliderThumbDragCompleted(object sender, RoutedEventArgs e)
        {
            _DisableSliderValueChanged = false;
            OnSliderValueChanged(sender, e);
        }
        void OnSliderValueChanged(object sender, RoutedEventArgs e)
        {
            if (!_DisableSliderValueChanged)
            {
                if (currentPdfMetaData != null)
                {
                    this.Dispatcher.InvokeAsync(async () => await ShowPageAsync(CurrentPageNumber, ClearCache: false, forceRedraw: true));
                }
            }
        }

        private void ChkfullScreenToggled(object sender, RoutedEventArgs e)
        {
            if (e.RoutedEvent.Name == "Checked")
            {
                this.WindowState = WindowState.Maximized;
                this.WindowStyle = WindowStyle.None;
            }
            else
            {
                //                this.WindowState = WindowState.Maximized;
                this.WindowStyle = WindowStyle.SingleBorderWindow;
                this.WindowState = WindowState.Normal;
            }
        }
        bool IsShowingMetaDataForm = false;
        private async void ImgThumb_Click(object sender, RoutedEventArgs e)
        {
            if (!IsShowingMetaDataForm && currentPdfMetaData != null)
            {
                IsShowingMetaDataForm = true;
                var curdata = currentPdfMetaData;
                var curpageno = CurrentPageNumber;
                var w = new MetaDataForm(this);
                if (w.ShowDialog() == true)
                {
                    IsShowingMetaDataForm = false;
                    if (w.PageNumberResult.HasValue)
                    {
                        curpageno = w.PageNumberResult.Value;
                    }
                    if (curpageno < curdata.PageNumberOffset || curpageno >= curdata.MaxPageNum)
                    {
                        curpageno = curdata.PageNumberOffset;
                    }
                    await LoadPdfFileAndShowAsync(curdata, curpageno);  // user could have changed the PageNumberOffset, so we need to reload the doc
                }
                else
                {
                    IsShowingMetaDataForm = false;
                }
            }
        }
        private static readonly Stopwatch _doubleTapStopwatch = new Stopwatch();
        private static Point _lastTapLocation;
        private bool chkFavoriteEnabled;

        public static double GetDistanceBetweenPoints(Point p, Point q)
        {
            double a = p.X - q.X;
            double b = p.Y - q.Y;
            double distance = Math.Sqrt(a * a + b * b);
            return distance;
        }
        public static bool IsDoubleTap(IInputElement sender, TouchEventArgs e)
        {
            Point currentTapPosition = e.GetTouchPoint(sender).Position;
            bool tapsAreCloseInDistance = GetDistanceBetweenPoints(currentTapPosition, _lastTapLocation) < 40;
            _lastTapLocation = currentTapPosition;

            TimeSpan elapsed = _doubleTapStopwatch.Elapsed;
            _doubleTapStopwatch.Restart();
            //var x = System.Windows.Forms.SystemInformation.DoubleClickSize; // 4, 4
            //var y = System.Windows.Forms.SystemInformation.DoubleClickTime; // 700
            bool tapsAreCloseInTime = (elapsed != TimeSpan.Zero && elapsed < TimeSpan.FromMilliseconds(700));

            return tapsAreCloseInDistance && tapsAreCloseInTime;
        }
    }
}
