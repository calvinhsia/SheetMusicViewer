using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace WpfPdfViewer
{
    public partial class PdfViewerWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public void OnMyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private int _CurrentPageNumber;
        public int CurrentPageNumber
        {
            get { return _CurrentPageNumber; }
            set
            {
                if (_CurrentPageNumber != value && currentPdfMetaData != null)
                {
                    _CurrentPageNumber = value;
                    this.dpPage.Children.Clear();// force regen
                    this.Dispatcher.InvokeAsync(async () => await ShowPageAsync(value, ClearCache: false));
                    OnMyPropertyChanged();
                }
            }
        }
        private int _MaxPageNumber;
        public int MaxPageNumber { get { return _MaxPageNumber; } set { if (_MaxPageNumber != value) { _MaxPageNumber = value; OnMyPropertyChanged(); } } }

        bool _fShow2Pages = true;
        public bool Show2Pages
        {
            get { return _fShow2Pages; }
            set
            {
                _fShow2Pages = value;
                chkFav1.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                this.dpPage.Children.Clear();
                this.Dispatcher.InvokeAsync(async () => await ShowPageAsync(CurrentPageNumber, ClearCache: true));
                OnMyPropertyChanged();
            }
        }
        internal int NumPagesPerView => _fShow2Pages ? 2 : 1;

        public bool PdfUIEnabled { get { return _currentPdfDocument != null; } set { OnMyPropertyChanged(); } }

        internal string _RootMusicFolder;
        internal readonly List<PdfMetaData> lstPdfMetaFileData = new List<PdfMetaData>();

        PdfDocument _currentPdfDocument = null;
        PdfMetaData currentPdfMetaData = null;
        static PdfViewerWindow s_pdfViewerWindow;

        class CacheEntry
        {
            /// <summary>
            /// add an item to cache. If already there, return it, else create new one
            /// </summary>
            /// <param name="PageNo"></param>
            /// <returns></returns>
            public static CacheEntry TryAddCacheEntry(int PageNo)
            {
                if (!s_pdfViewerWindow.dictCache.TryGetValue(PageNo, out var cacheEntry) ||
                    cacheEntry.cts.IsCancellationRequested ||
                    cacheEntry.task.IsCanceled)
                {
                    cacheEntry = new CacheEntry()
                    {
                        pageNo = PageNo,
                        age = currentCacheAge++
                    };
                    cacheEntry.task = s_pdfViewerWindow.CalculateGridForPageAync(cacheEntry);

                    int cacheSize = 500;
                    if (s_pdfViewerWindow.dictCache.Count > cacheSize)
                    {
                        var lst = s_pdfViewerWindow.dictCache.Values.OrderBy(s => s.age).Take(s_pdfViewerWindow.dictCache.Count - cacheSize);
                        foreach (var entry in lst)
                        {
                            s_pdfViewerWindow.dictCache.Remove(entry.pageNo);
                        }
                    }
                    s_pdfViewerWindow.dictCache[PageNo] = cacheEntry;
                }
                return cacheEntry;
            }
            internal CancellationTokenSource cts = new CancellationTokenSource();
            public int pageNo;
            public Task<Grid> task;
            public int age; // just a growing int
            public override string ToString()
            {
                return $"{pageNo} age={age} IsComp={task.IsCompleted}";
            }
        }
        static int currentCacheAge;
        readonly Dictionary<int, CacheEntry> dictCache = new Dictionary<int, CacheEntry>(); // for either show2pages, pageno ->grid. results in dupes if even, then odd number on show2pages

        public string FullPathCurrentPdfFile => currentPdfMetaData?.curFullPathFile;


        public PdfViewerWindow()
        {
            InitializeComponent();
            s_pdfViewerWindow = this;
            this.DataContext = this;
            this.Width = Properties.Settings.Default.MainWindowSize.Width;
            this.Height = Properties.Settings.Default.MainWindowSize.Height;
            this.Top = Properties.Settings.Default.MainWindowPos.Height;
            this.Left = Properties.Settings.Default.MainWindowPos.Width;
            this._RootMusicFolder = Properties.Settings.Default.RootMusicFolder;
            this.Show2Pages = Properties.Settings.Default.Show2Pages;

            this.Closed += (o, e) =>
              {
                  Properties.Settings.Default.Show2Pages = Show2Pages;
                  Properties.Settings.Default.RootMusicFolder = _RootMusicFolder;
                  Properties.Settings.Default.LastPDFOpen = FullPathCurrentPdfFile;
                  Properties.Settings.Default.MainWindowPos = new System.Drawing.Size((int)this.Left, (int)this.Top);
                  Properties.Settings.Default.MainWindowSize = new System.Drawing.Size((int)this.ActualWidth, (int)this.ActualHeight);
                  Properties.Settings.Default.MainWindowState = (int)this.WindowState;
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

        private async void MainWindow_LoadedAsync(object sender, RoutedEventArgs e)
        {
            // https://blogs.windows.com/buildingapps/2017/01/25/calling-windows-10-apis-desktop-application/#RWYkd5C4WTeEybol.97
            try
            {
                this.WindowState = (WindowState)Properties.Settings.Default.MainWindowState;
                this.WindowStyle = WindowStyle.None;
                var lastPdfOpen = Properties.Settings.Default.LastPDFOpen;
                if (string.IsNullOrEmpty(_RootMusicFolder))
                {
                    await ChooseMusic();
                }
                else
                {
                    await LoadAllPdfMetaDataFromDiskAsync();
                    currentPdfMetaData = lstPdfMetaFileData.Where(p => p.curFullPathFile == lastPdfOpen).FirstOrDefault();
                    if (currentPdfMetaData != null)
                    {
                        await LoadPdfFileAndShowAsync(currentPdfMetaData, currentPdfMetaData.LastPageNo);
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

        internal async Task LoadAllPdfMetaDataFromDiskAsync()
        {
            if (!string.IsNullOrEmpty(_RootMusicFolder) && Directory.Exists(_RootMusicFolder))
            {
                var pathCurrentMusicFolder = _RootMusicFolder;
                lstPdfMetaFileData.Clear();
                await Task.Run(() =>
                {
                    recurDirs(pathCurrentMusicFolder);
                    bool TryAddFile(string curFullPathFile)
                    {
                        var curPdfFileData = PdfMetaData.ReadPdfMetaData(curFullPathFile);
                        if (curPdfFileData != null)
                        {
                            lstPdfMetaFileData.Add(curPdfFileData);
                        }
                        return true;
                    }
                    void recurDirs(string curPath)
                    {
                        var lastFile = string.Empty;
                        foreach (var file in Directory.EnumerateFiles(curPath, "*.pdf").OrderBy(f => f)) // "file" is fullpath
                        {
                            if (TryAddFile(file) &&
                                !string.IsNullOrEmpty(lastFile) &&
                                System.IO.Path.GetDirectoryName(lastFile) == System.IO.Path.GetDirectoryName(file))
                            {
                                var isContinuation = false;
                                // if the prior added file and this file differ by ony a single char, treat as continuation. E.g. file1.pdf, file2.pdf
                                var justFnamelast = System.IO.Path.GetFileNameWithoutExtension(lastFile).Trim();
                                var justfnameCurrent = System.IO.Path.GetFileNameWithoutExtension(file).Trim();
                                if (justFnamelast.Length == justfnameCurrent.Length) // file1, file2
                                {
                                    if (justFnamelast.Substring(0, justFnamelast.Length - 1) ==
                                       justfnameCurrent.Substring(0, justfnameCurrent.Length - 1))
                                    {
                                        isContinuation = true;
                                    }
                                }
                                else
                                {  // file, file2
                                    if (justFnamelast == justfnameCurrent.Substring(0, justfnameCurrent.Length - 1))
                                    {
                                        isContinuation = true;
                                    }
                                }
                                if (isContinuation)
                                {
                                    lstPdfMetaFileData[lstPdfMetaFileData.Count - 2].SucceedingPdfMetaData = lstPdfMetaFileData[lstPdfMetaFileData.Count - 1];
                                    lstPdfMetaFileData[lstPdfMetaFileData.Count - 1].PriorPdfMetaData = lstPdfMetaFileData[lstPdfMetaFileData.Count - 2];
                                }
                            }

                            lastFile = file;
                        }
                        foreach (var dir in Directory.EnumerateDirectories(curPath))
                        {
                            recurDirs(dir);
                        }
                    }
                });
            }
        }

        private async Task LoadPdfFileAndShowAsync(PdfMetaData pdfMetaData, int PageNo)
        {
            CloseCurrentPdfFile();
            currentPdfMetaData = pdfMetaData;
            StorageFile f = await StorageFile.GetFileFromPathAsync(currentPdfMetaData.curFullPathFile);
            var pdfDoc = await PdfDocument.LoadFromFileAsync(f);
            if (pdfDoc.IsPasswordProtected)
            {
                this.dpPage.Children.Add(new TextBlock() { Text = $"Password Protected {currentPdfMetaData.curFullPathFile}" });
            }
            this._currentPdfDocument = pdfDoc;
            this.MaxPageNumber = (int)_currentPdfDocument.PageCount;
            this.slider.Maximum = this.MaxPageNumber;
            this.slider.IsDirectionReversed = true;
            this.PdfUIEnabled = true;
            this.txtBoxTitle.Text = $"{currentPdfMetaData?.curFullPathFile}";
            if (PageNo == int.MaxValue)
            {
                PageNo = MaxPageNumber - 1;
            }
            await ShowPageAsync(PageNo, ClearCache: true);
        }

        /// <summary>
        /// Show a page or 2
        /// </summary>
        /// <param name="pageNo"></param>
        /// <param name="ClearCache"></param>
        private async Task ShowPageAsync(int pageNo, bool ClearCache)
        {
            if (ClearCache)
            {
                dictCache.Clear();
            }
            if (currentPdfMetaData == null)
            {
                this.dpPage.Children.Clear();
                return;
            }
            if (pageNo == CurrentPageNumber && dpPage.Children.Count > 0)
            {
                return;
            }
            if (pageNo < 0)
            {
                if (currentPdfMetaData.PriorPdfMetaData != null)
                {
                    var prior = currentPdfMetaData.PriorPdfMetaData;
                    await LoadPdfFileAndShowAsync(prior, int.MaxValue);
                    return;
                }
                pageNo = 0;
            }
            if (pageNo >= MaxPageNumber)
            {
                if (currentPdfMetaData.SucceedingPdfMetaData != null)
                {
                    var next = currentPdfMetaData.SucceedingPdfMetaData;
                    await LoadPdfFileAndShowAsync(next, PageNo: 0);
                    return;
                }
                pageNo = MaxPageNumber - NumPagesPerView;
                if (pageNo < 0)
                {
                    pageNo = 0;
                }
            }
            try
            {
                this.CurrentPageNumber = pageNo;
                if (!dictCache.TryGetValue(pageNo, out var cacheEntry) || cacheEntry.task.IsCanceled)
                {
                    cacheEntry = CacheEntry.TryAddCacheEntry(pageNo);
                }
                if (pageNo + NumPagesPerView <= MaxPageNumber && !dictCache.ContainsKey(pageNo + NumPagesPerView))
                {
                    CacheEntry.TryAddCacheEntry(pageNo + NumPagesPerView);
                }
                if (pageNo - NumPagesPerView >= 0 && !dictCache.ContainsKey(pageNo - NumPagesPerView))
                {
                    CacheEntry.TryAddCacheEntry(pageNo - NumPagesPerView);
                }
                if (!cacheEntry.task.IsCompleted)
                {
                    await cacheEntry.task;
                }
                if (this.CurrentPageNumber == pageNo) // user might have typed ahead
                {
                    this.dpPage.Children.Clear();
                    if (cacheEntry.task.IsCanceled)
                    {
                        dictCache.Remove(cacheEntry.pageNo);
                    }
                    var grid = cacheEntry.task.Result;
                    this.dpPage.Children.Add(grid);
                    chkFav0.IsChecked = currentPdfMetaData.IsFavorite(pageNo);
                    if (NumPagesPerView > 1)
                    {
                        chkFav1.IsChecked = currentPdfMetaData.IsFavorite(pageNo + 1);
                    }
                }
                else
                {
                    var numPendingTasks = dictCache.Values.Where(v => !v.task.IsCompleted).Count();
                    // The user could have held down rt-arrow, advancing thru doc faster than we can render
                    // so we check for any prior tasks that have not completed and cancel them.
                    var lstToDelete = new List<int>();
                    foreach (var entry in dictCache.Values.Where(v => !v.task.IsCompleted))
                    {
                        if (entry.pageNo != pageNo) // don't del the task getting the current pgno
                        {
                            lstToDelete.Add(entry.pageNo);
                            entry.cts.Cancel();
                        }
                    }
                    if (lstToDelete.Count > 0)
                    {
                        foreach (var item in lstToDelete)
                        {
                            dictCache.Remove(item);
                        }
                    }
                    "asdf".ToString();
                }

            }
            catch (Exception ex)
            {
                this.dpPage.Children.Clear();
                System.Windows.Forms.MessageBox.Show($"Exception showing {currentPdfMetaData.curFullPathFile}\r\n {ex.ToString()}");
                CloseCurrentPdfFile();
            }
        }

        async Task<Grid> CalculateGridForPageAync(CacheEntry cacheEntry)
        {
            var grid = new Grid();
            var lstItems = new List<UIElement>();
            var pageNo = cacheEntry.pageNo;
            try
            {
                for (int i = 0; i < NumPagesPerView; i++)
                {
                    cacheEntry.cts.Token.ThrowIfCancellationRequested();
                    if (pageNo + i < _MaxPageNumber)
                    {
                        using (var pdfPage = _currentPdfDocument.GetPage((uint)(pageNo + i)))
                        {
                            var bmi = new BitmapImage();
                            using (var strm = new InMemoryRandomAccessStream())
                            {
                                var rect = pdfPage.Dimensions.ArtBox;
                                var renderOpts = new PdfPageRenderOptions()
                                {
                                    DestinationWidth = (uint)rect.Width,
                                    DestinationHeight = (uint)rect.Height,
                                };
                                if (pdfPage.Rotation != PdfPageRotation.Normal)
                                {
                                    renderOpts.DestinationHeight = (uint)rect.Width;
                                    renderOpts.DestinationWidth = (uint)rect.Height;
                                }

                                await pdfPage.RenderToStreamAsync(strm, renderOpts);
                                cacheEntry.cts.Token.ThrowIfCancellationRequested();
                                bmi.BeginInit();
                                bmi.StreamSource = strm.AsStream();
                                bmi.Rotation = (Rotation)currentPdfMetaData.Rotation;
                                bmi.CacheOption = BitmapCacheOption.OnLoad;
                                bmi.EndInit();

                                var img = new Image()
                                {
                                    Source = bmi
                                };
                                //                            img.Stretch = Stretch.Uniform;
                                if (NumPagesPerView > 1)
                                {
                                    if (i == 0)
                                    {
                                        img.HorizontalAlignment = HorizontalAlignment.Right; // put lefthand page close to middle
                                    }
                                    else
                                    {
                                        img.HorizontalAlignment = HorizontalAlignment.Left; // put righthand page close to middle
                                    }
                                }
                                lstItems.Add(img);
                            }
                        }
                    }
                }
                if (lstItems.Count == 1)
                {
                    //grid.RowDefinitions.Add(new RowDefinition() { Height = (GridLength)new GridLengthConverter().ConvertFromString("Auto") });
                    //grid.RowDefinitions.Add(new RowDefinition() { Height = (GridLength)new GridLengthConverter().ConvertFromString("*") });
                    //                    var x = new RowDefinition() { Height = new GridLength(0, GridUnitType.Star) };
                    grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
                    grid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) });

                    //var chkFav = new CheckBox()
                    //{
                    //    Content = "Favorite",
                    //    Foreground = Brushes.Yellow,
                    //    Tag = pageNo,
                    //    IsChecked = currentPdfMetaData.IsFavorite(pageNo),
                    //    HorizontalAlignment = HorizontalAlignment.Right
                    //};
                    //chkFav.Checked += (o, e) =>
                    //{
                    //    currentPdfMetaData.ToggleFavorite((int)chkFav.Tag, true);
                    //};
                    //chkFav.Unchecked += (o, e) =>
                    //{
                    //    currentPdfMetaData.ToggleFavorite((int)chkFav.Tag, false);
                    //};
                    //grid.Children.Add(chkFav);
                    //Grid.SetRow(chkFav, 0);
                    grid.Children.Add(lstItems[0]);
                    Grid.SetRow(lstItems[0], 1);
                }
                else
                {
                    //                    grid1.ColumnDefinitions.Add(new ColumnDefinition() { Width = (GridLength)(new GridLengthConverter()).ConvertFromString("Auto") });
                    grid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(this.dpPage.ActualWidth / NumPagesPerView) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(this.dpPage.ActualWidth / NumPagesPerView) });
                    for (int i = 0; i < lstItems.Count; i++)
                    {
                        //var chkFav = new CheckBox()
                        //{
                        //    Content = "Favorite",
                        //    Foreground = Brushes.Yellow,
                        //    Tag = pageNo + i,
                        //    IsChecked = currentPdfMetaData.IsFavorite(pageNo + i),
                        //    HorizontalAlignment = (i == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right)
                        //};
                        //chkFav.Checked += (o, e) =>
                        //{
                        //    currentPdfMetaData.ToggleFavorite((int)chkFav.Tag, true);
                        //};
                        //chkFav.Unchecked += (o, e) =>
                        //{
                        //    currentPdfMetaData.ToggleFavorite((int)chkFav.Tag, false);
                        //};
                        //grid.Children.Add(chkFav);
                        //Grid.SetColumn(chkFav, i);
                        grid.Children.Add(lstItems[i]);
                        Grid.SetColumn(lstItems[i], i);
                    }
                }
            }
            catch (OperationCanceledException) // includes TaskCanceledException
            {
            }
            return grid;
        }
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            var elmWithFocus = Keyboard.FocusedElement;
            if (!(elmWithFocus is TextBox) && !(elmWithFocus is Slider))
            {
                switch (e.Key)
                {
                    case Key.Up:
                    case Key.PageUp:
                    case Key.Left:
                        e.Handled = true;
                        Navigate(forward: false);
                        break;
                    case Key.Down:
                    case Key.PageDown:
                    case Key.Right:
                        Navigate(forward: true);
                        e.Handled = true;
                        break;
                }
            }
        }
        private void ChkFavToggled(object sender, RoutedEventArgs e)
        {
            var nameSender = ((CheckBox)sender).Name;
            // e.RoutedEvent.Name == "Checked", "Unchecked"
            // ((CheckBox)sender).Name == "chkFav0"
            //    currentPdfMetaData.ToggleFavorite((int)chkFav.Tag, true);
            var pgno = CurrentPageNumber + (nameSender == "chkFav0" ? 0 : 1);
            var isChked = e.RoutedEvent.Name == "Checked";
            currentPdfMetaData.ToggleFavorite(pgno, isChked);

        }

        async void BtnRotate_Click(object sender, RoutedEventArgs e)
        {
            currentPdfMetaData.Rotation = (currentPdfMetaData.Rotation + 1) % 4;
            this.dpPage.Children.Clear();
            await ShowPageAsync(CurrentPageNumber, ClearCache: true);
        }

        async void Navigate(bool forward)
        {
            var newPageNo = CurrentPageNumber;
            if (forward)
            {
                newPageNo += NumPagesPerView;
            }
            else
            {
                newPageNo -= NumPagesPerView;
            }
            await ShowPageAsync(newPageNo, ClearCache: false);
        }

        void BtnBookMarks_Click(object sender, RoutedEventArgs e)
        {

        }

        protected override async void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            if (_currentPdfDocument != null)
            {
                this.dpPage.Children.Clear();
                await ShowPageAsync(CurrentPageNumber, ClearCache: true);
            }
        }

        void BtnPrevNext_Click(object sender, RoutedEventArgs e)
        {
            var forwardOrBack = sender is Button b && b.Name != "btnPrev";
            Navigate(forwardOrBack);
        }

        //private async void TxtPageNo_LostFocus(object sender, RoutedEventArgs e)
        //{
        //    if (int.TryParse(txtPageNo.Text, out var newpgno))
        //    {
        //        await ShowPdfFileAsync(newpgno);
        //    }
        //}
        private void DpPage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(this.dpPage);
            var leftSide = pos.X < this.dpPage.ActualWidth / 2;
            Navigate(forward: leftSide ? false : true);
        }


        async Task<bool> ChooseMusic()
        {
            var retval = false;
            var win = new ChooseMusic();
            win.Initialize(this);
            if (win.ShowDialog() == true)
            {
                currentPdfMetaData = win.chosenPdfMetaData;
                if (currentPdfMetaData != null)
                {
                    await LoadPdfFileAndShowAsync(currentPdfMetaData, currentPdfMetaData.LastPageNo);
                    retval = true;
                }
            }
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
        void CloseCurrentPdfFile()
        {
            if (currentPdfMetaData != null)
            {
                dictCache.Clear();
                currentPdfMetaData.LastPageNo = CurrentPageNumber;
                PdfMetaData.SavePdfFileData(currentPdfMetaData);
                _currentPdfDocument = null;
                currentPdfMetaData = null;
                MaxPageNumber = 0;
                CurrentPageNumber = 0;
                this.dpPage.Children.Clear();
            }
            this.PdfUIEnabled = false;
        }

        async static Task<DocumentViewer> CombinePDFsToASinglePdfAsync()
        {
            await CombinePDFsToASinglePdfAsync();// to quiet warning about unused func
            var docvwr = new DocumentViewer();

            //             //pdfSourceDoc = @"C:\Users\calvinh\OneDrive\Documents\SheetMusic\Ragtime\Collections\The Music of James Scott001.pdf";
            //rotation = Rotation.Rotate0;

            var fixedDoc = await ConvertMultiPdfToSingleAsync(
                @"C:\Users\calvinh\OneDrive\Documents\SheetMusic\FakeBooks\Ultimate Pop Rock Fake Book.pdf",
                Rotation.Rotate0,
                @"C:\Users\calvinh\OneDrive\Documents\SheetMusic\FakeBooks\Ultimate Pop Rock Fake Book 1.pdf",
                Rotation.Rotate180);
            docvwr.Document = fixedDoc;
            //IDocumentPaginatorSource idps = fixedDoc;
            //var pdlg = new PrintDialog();
            //var queueName = "Microsoft Print to PDF";
            //var pServer = new PrintServer();
            //var pqueues = pServer.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local });
            //pdlg.PrintQueue = new PrintQueue(pServer, queueName);
            //pdlg.PrintDocument(idps.DocumentPaginator, "testprint");
            return docvwr;
        }

        async static Task<FixedDocument> ConvertMultiPdfToSingleAsync(string titlePage, Rotation rotationTitlePage, string vol1, Rotation rotation)
        {
            var fixedDoc = new FixedDocument();
            if (!string.IsNullOrEmpty(titlePage) && File.Exists(titlePage))
            {
                var fTitle = await StorageFile.GetFileFromPathAsync(titlePage);
                var pdfDocTitle = await PdfDocument.LoadFromFileAsync(fTitle);
                var pgTitle = pdfDocTitle.GetPage(0);
                await AddPageToDocAsync(fixedDoc, pgTitle, rotationTitlePage);
            }
            var fDone = false;
            int nVolNo = 1;
            var pdfDataFileToUse = vol1;
            while (!fDone)
            {
                StorageFile f = await StorageFile.GetFileFromPathAsync(pdfDataFileToUse);
                var pdfDoc = await PdfDocument.LoadFromFileAsync(f);
                var nPageCount = pdfDoc.PageCount;
                for (int i = 0; i < nPageCount; i++)
                {
                    using (var page = pdfDoc.GetPage((uint)i))
                    {
                        await AddPageToDocAsync(fixedDoc, page, rotation);
                    }
                }
                if (!vol1.EndsWith("1.pdf"))
                {
                    break;
                }
                nVolNo++;
                pdfDataFileToUse = vol1.Replace("1.pdf", string.Empty) + nVolNo.ToString() + ".pdf";
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
    }
}
