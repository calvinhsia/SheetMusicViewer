using System.Text.Json;
using System.Text.Json.Serialization;

namespace SheetMusicLib;

/// <summary>
/// Cross-platform application settings using JSON file storage.
/// Works on Windows, macOS, and Linux.
/// 
/// Settings are split into two categories:
/// - Local settings (LocalApplicationData): Machine-specific data like window positions, file paths
/// - Roaming settings (music root folder): User data that should sync across machines
///   like playlists and user preferences. Stored in {MusicRootFolder}/.sheetmusicviewer/userdata.json
///   which keeps user data with the music collection (typically already in OneDrive).
/// </summary>
public class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static string _localSettingsPath = null!;
    private static string? _musicRootFolder;
    private static AppSettings? _instance;
    private static readonly object _lock = new();
    
    private const string RoamingFolderName = ".sheetmusicviewer";
    private const string RoamingFileName = "userdata.json";

    /// <summary>
    /// Gets the singleton instance of AppSettings.
    /// Call Initialize() first to set up the settings paths.
    /// </summary>
    public static AppSettings Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= Load();
                }
            }
            return _instance;
        }
    }

    /// <summary>
    /// Initialize the settings with a custom application name.
    /// Call this once at application startup.
    /// </summary>
    /// <param name="appName">Application name for the settings folder</param>
    public static void Initialize(string appName = "SheetMusicViewer")
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _localSettingsPath = Path.Combine(localAppData, appName, "settings.json");
    }
    
    /// <summary>
    /// Sets the music root folder, which determines where roaming settings are stored.
    /// Roaming settings (playlists, preferences) are stored in {musicRootFolder}/.sheetmusicviewer/userdata.json
    /// This keeps user data with the music collection, which is typically already in OneDrive.
    /// </summary>
    /// <param name="musicRootFolder">Path to the music root folder containing PDF files</param>
    public static void SetMusicRootFolder(string? musicRootFolder)
    {
        lock (_lock)
        {
            var previousFolder = _musicRootFolder;
            _musicRootFolder = musicRootFolder;
            
            // Reload roaming settings if instance exists and music folder changed
            if (_instance != null && musicRootFolder != previousFolder && !string.IsNullOrEmpty(musicRootFolder))
            {
                _instance.LoadRoamingFromMusicFolder();
            }
        }
    }

    /// <summary>
    /// Gets the path where local (machine-specific) settings are stored.
    /// </summary>
    public static string LocalSettingsPath
    {
        get
        {
            if (string.IsNullOrEmpty(_localSettingsPath))
            {
                Initialize();
            }
            return _localSettingsPath;
        }
    }
    
    /// <summary>
    /// Gets the path where roaming (user data) settings are stored.
    /// Returns null if no music root folder has been set.
    /// </summary>
    public static string? RoamingSettingsPath
    {
        get
        {
            if (string.IsNullOrEmpty(_musicRootFolder))
            {
                return null;
            }
            return Path.Combine(_musicRootFolder, RoamingFolderName, RoamingFileName);
        }
    }
    
    /// <summary>
    /// For backward compatibility - returns local settings path.
    /// </summary>
    public static string SettingsPath => LocalSettingsPath;

    #region User-Configurable Options (shown in Options dialog) - ROAMING

    /// <summary>
    /// User-configurable options that appear in the Options dialog.
    /// These are settings the user explicitly chooses to change.
    /// Access via: AppSettings.Instance.UserOptions.PropertyName
    /// Note: These settings ROAM with the music folder.
    /// </summary>
    public UserOptionsSettings UserOptions { get; set; } = new();

    /// <summary>
    /// Settings that appear in the Options dialog.
    /// These settings roam with the music folder.
    /// </summary>
    public class UserOptionsSettings
    {
        // Cloud/Performance settings
        /// <summary>
        /// If true, skip cloud-only files (OneDrive/cloud storage) instead of triggering download.
        /// This improves performance when loading thumbnails but shows placeholder images for cloud-only files.
        /// </summary>
        public bool SkipCloudOnlyFiles { get; set; } = false;

        // Double-tap detection settings
        /// <summary>
        /// Time threshold in milliseconds for double-tap detection.
        /// Two taps within this time window are considered a double-tap.
        /// </summary>
        public int DoubleTapTimeThresholdMs { get; set; } = 500;

        /// <summary>
        /// Distance threshold in pixels for double-tap detection.
        /// Two taps within this distance are considered at the same location.
        /// </summary>
        public double DoubleTapDistanceThreshold { get; set; } = 50;

        // Cache settings
        /// <summary>
        /// Maximum number of rendered pages to keep in the page cache.
        /// Higher values use more memory but improve navigation speed.
        /// </summary>
        public int PageCacheMaxSize { get; set; } = 50;

        /// <summary>
        /// If true, disables the page cache entirely. Useful for performance testing.
        /// Each page navigation will re-render the page from the PDF.
        /// </summary>
        public bool DisablePageCache { get; set; } = false;

        /// <summary>
        /// Maximum number of PDF file byte arrays to keep in memory per document.
        /// Each volume's bytes are cached for faster page rendering.
        /// </summary>
        public int PdfBytesCacheMaxVolumes { get; set; } = 5;

        /// <summary>
        /// Maximum degree of parallelism for background thumbnail loading.
        /// Higher values load faster but use more CPU.
        /// </summary>
        public int ThumbnailLoadingParallelism { get; set; } = 4;

        /// <summary>
        /// DPI to use when rendering PDF pages. Higher values are sharper but slower.
        /// </summary>
        public int RenderDpi { get; set; } = 150;

        /// <summary>
        /// Width in pixels for thumbnail images.
        /// </summary>
        public int ThumbnailWidth { get; set; } = 150;

        /// <summary>
        /// Height in pixels for thumbnail images.
        /// </summary>
        public int ThumbnailHeight { get; set; } = 225;
    }

    #endregion

    #region Roaming User Data (playlists, etc.) - stored in music folder

    /// <summary>
    /// User-created playlists containing song references.
    /// This data ROAMS with the music folder.
    /// </summary>
    public List<Playlist> Playlists { get; set; } = new();
    
    /// <summary>
    /// Name of the last selected playlist.
    /// This data ROAMS with the music folder.
    /// </summary>
    public string? LastSelectedPlaylist { get; set; }

    #endregion

    #region Local Machine Settings (window state, MRU, etc.) - LOCAL ONLY

    // These settings are automatically saved/restored but not shown in Options dialog.
    // They represent machine-specific application state.
    // Note: These do NOT roam - they are specific to each machine.

    // Main window position and size
    public double WindowWidth { get; set; } = 1200;
    public double WindowHeight { get; set; } = 800;
    public double WindowTop { get; set; } = 100;
    public double WindowLeft { get; set; } = 100;
    public bool WindowMaximized { get; set; } = true;

    // View settings (toggled via menu, not Options dialog)
    public bool Show2Pages { get; set; } = true;
    public bool IsFullScreen { get; set; } = false;

    // Last opened PDF (machine-specific path)
    public string? LastPDFOpen { get; set; }

    // Most Recently Used root folders (machine-specific paths)
    public List<string> RootFolderMRU { get; set; } = new();

    // Choose dialog settings
    public string ChooseBooksSort { get; set; } = "ByDate";
    public string ChooseQueryTab { get; set; } = "_Books";
    public double ChooseWindowWidth { get; set; } = 900;
    public double ChooseWindowHeight { get; set; } = 700;
    public double ChooseWindowTop { get; set; } = -1;
    public double ChooseWindowLeft { get; set; } = -1;
    public bool ChooseWindowMaximized { get; set; } = true;
    
    // MetaData editor dialog settings
    public double MetaDataWindowWidth { get; set; } = 1200;
    public double MetaDataWindowHeight { get; set; } = 700;
    public double MetaDataWindowTop { get; set; } = -1;
    public double MetaDataWindowLeft { get; set; } = -1;
    public bool MetaDataWindowMaximized { get; set; } = true;

    #endregion

    /// <summary>
    /// Load settings from disk, merging local and roaming settings.
    /// </summary>
    public static AppSettings Load()
    {
        var settings = new AppSettings();
        
        // First load local settings (machine-specific)
        try
        {
            var localPath = LocalSettingsPath;
            if (File.Exists(localPath))
            {
                var json = File.ReadAllText(localPath);
                var localSettings = JsonSerializer.Deserialize<LocalSettings>(json, JsonOptions);
                if (localSettings != null)
                {
                    // Copy local-only settings
                    settings.WindowWidth = localSettings.WindowWidth;
                    settings.WindowHeight = localSettings.WindowHeight;
                    settings.WindowTop = localSettings.WindowTop;
                    settings.WindowLeft = localSettings.WindowLeft;
                    settings.WindowMaximized = localSettings.WindowMaximized;
                    settings.Show2Pages = localSettings.Show2Pages;
                    settings.IsFullScreen = localSettings.IsFullScreen;
                    settings.LastPDFOpen = localSettings.LastPDFOpen;
                    settings.RootFolderMRU = localSettings.RootFolderMRU ?? new List<string>();
                    settings.ChooseBooksSort = localSettings.ChooseBooksSort ?? "ByDate";
                    settings.ChooseQueryTab = localSettings.ChooseQueryTab ?? "_Books";
                    settings.ChooseWindowWidth = localSettings.ChooseWindowWidth;
                    settings.ChooseWindowHeight = localSettings.ChooseWindowHeight;
                    settings.ChooseWindowTop = localSettings.ChooseWindowTop;
                    settings.ChooseWindowLeft = localSettings.ChooseWindowLeft;
                    settings.ChooseWindowMaximized = localSettings.ChooseWindowMaximized;
                    settings.MetaDataWindowWidth = localSettings.MetaDataWindowWidth;
                    settings.MetaDataWindowHeight = localSettings.MetaDataWindowHeight;
                    settings.MetaDataWindowTop = localSettings.MetaDataWindowTop;
                    settings.MetaDataWindowLeft = localSettings.MetaDataWindowLeft;
                    settings.MetaDataWindowMaximized = localSettings.MetaDataWindowMaximized;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Error loading local settings: {ex.Message}");
        }
        
        // Then load roaming settings from music folder (if set)
        settings.LoadRoamingFromMusicFolder();
        return settings;
    }
    
    /// <summary>
    /// Load roaming settings from the music root folder.
    /// </summary>
    private void LoadRoamingFromMusicFolder()
    {
        var roamingPath = RoamingSettingsPath;
        if (string.IsNullOrEmpty(roamingPath)) return;
        
        try
        {
            if (File.Exists(roamingPath))
            {
                var json = File.ReadAllText(roamingPath);
                var roamingSettings = JsonSerializer.Deserialize<RoamingSettings>(json, JsonOptions);
                if (roamingSettings != null)
                {
                    Playlists = roamingSettings.Playlists ?? new List<Playlist>();
                    LastSelectedPlaylist = roamingSettings.LastSelectedPlaylist;
                    if (roamingSettings.UserOptions != null)
                    {
                        UserOptions = roamingSettings.UserOptions;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Error loading roaming settings: {ex.Message}");
        }
    }

    /// <summary>
    /// Save settings to disk (both local and roaming files).
    /// </summary>
    public void Save()
    {
        SaveLocal();
        SaveRoaming();
    }
    
    /// <summary>
    /// Save only local (machine-specific) settings.
    /// </summary>
    public void SaveLocal()
    {
        try
        {
            var path = LocalSettingsPath;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var localSettings = new LocalSettings
            {
                WindowWidth = WindowWidth,
                WindowHeight = WindowHeight,
                WindowTop = WindowTop,
                WindowLeft = WindowLeft,
                WindowMaximized = WindowMaximized,
                Show2Pages = Show2Pages,
                IsFullScreen = IsFullScreen,
                LastPDFOpen = LastPDFOpen,
                RootFolderMRU = RootFolderMRU,
                ChooseBooksSort = ChooseBooksSort,
                ChooseQueryTab = ChooseQueryTab,
                ChooseWindowWidth = ChooseWindowWidth,
                ChooseWindowHeight = ChooseWindowHeight,
                ChooseWindowTop = ChooseWindowTop,
                ChooseWindowLeft = ChooseWindowLeft,
                ChooseWindowMaximized = ChooseWindowMaximized,
                MetaDataWindowWidth = MetaDataWindowWidth,
                MetaDataWindowHeight = MetaDataWindowHeight,
                MetaDataWindowTop = MetaDataWindowTop,
                MetaDataWindowLeft = MetaDataWindowLeft,
                MetaDataWindowMaximized = MetaDataWindowMaximized
            };

            var json = JsonSerializer.Serialize(localSettings, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Error saving local settings: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Save only roaming (user data) settings to the music folder.
    /// Does nothing if no music root folder has been set.
    /// </summary>
    public void SaveRoaming()
    {
        var path = RoamingSettingsPath;
        if (string.IsNullOrEmpty(path)) return;
        
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var roamingSettings = new RoamingSettings
            {
                UserOptions = UserOptions,
                Playlists = Playlists,
                LastSelectedPlaylist = LastSelectedPlaylist
            };

            var json = JsonSerializer.Serialize(roamingSettings, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Error saving roaming settings: {ex.Message}");
        }
    }

    /// <summary>
    /// Add a folder to the MRU list, moving it to the front if it already exists.
    /// </summary>
    /// <param name="folder">Folder path to add</param>
    /// <param name="maxItems">Maximum number of MRU items to keep</param>
    public void AddToMRU(string folder, int maxItems = 10)
    {
        if (string.IsNullOrEmpty(folder))
            return;

        // Remove if already exists
        RootFolderMRU.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));

        // Add to front
        RootFolderMRU.Insert(0, folder);

        // Trim to max size
        while (RootFolderMRU.Count > maxItems)
        {
            RootFolderMRU.RemoveAt(RootFolderMRU.Count - 1);
        }
    }

    /// <summary>
    /// Get the most recently used root folder, or null if none.
    /// </summary>
    public string? GetMostRecentFolder()
    {
        return RootFolderMRU.FirstOrDefault();
    }

    /// <summary>
    /// Reset user options to defaults (does not reset window positions/MRU).
    /// </summary>
    public void ResetUserOptions()
    {
        UserOptions = new UserOptionsSettings();
    }

    /// <summary>
    /// Reset all settings to defaults including window positions.
    /// </summary>
    public void ResetAll()
    {
        var defaults = new AppSettings();
        
        // Reset user options
        UserOptions = new UserOptionsSettings();
        
        // Reset window state
        WindowWidth = defaults.WindowWidth;
        WindowHeight = defaults.WindowHeight;
        WindowTop = defaults.WindowTop;
        WindowLeft = defaults.WindowLeft;
        WindowMaximized = defaults.WindowMaximized;
        Show2Pages = defaults.Show2Pages;
        IsFullScreen = defaults.IsFullScreen;
        LastPDFOpen = defaults.LastPDFOpen;
        RootFolderMRU.Clear();
        ChooseBooksSort = defaults.ChooseBooksSort;
        ChooseQueryTab = defaults.ChooseQueryTab;
        ChooseWindowWidth = defaults.ChooseWindowWidth;
        ChooseWindowHeight = defaults.ChooseWindowHeight;
        ChooseWindowTop = defaults.ChooseWindowTop;
        ChooseWindowLeft = defaults.ChooseWindowLeft;
        ChooseWindowMaximized = defaults.ChooseWindowMaximized;
        MetaDataWindowWidth = defaults.MetaDataWindowWidth;
        MetaDataWindowHeight = defaults.MetaDataWindowHeight;
        MetaDataWindowTop = defaults.MetaDataWindowTop;
        MetaDataWindowLeft = defaults.MetaDataWindowLeft;
        MetaDataWindowMaximized = defaults.MetaDataWindowMaximized;
        
        // Reset playlists
        Playlists.Clear();
        LastSelectedPlaylist = null;
    }

    /// <summary>
    /// Reset the singleton instance for testing purposes.
    /// Allows tests to use custom settings paths and fresh instance.
    /// </summary>
    /// <param name="testSettingsPath">Optional custom path for test settings file</param>
    public static void ResetForTesting(string? testSettingsPath = null)
    {
        lock (_lock)
        {
            _instance = null;
            _musicRootFolder = null;
            if (!string.IsNullOrEmpty(testSettingsPath))
            {
                _localSettingsPath = testSettingsPath;
                // For testing, use the directory containing the settings file as the music root
                _musicRootFolder = Path.GetDirectoryName(testSettingsPath) ?? Path.GetTempPath();
            }
            else
            {
                _localSettingsPath = null!;
            }
        }
    }
    
    /// <summary>
    /// Helper class for serializing only local settings
    /// </summary>
    private class LocalSettings
    {
        public double WindowWidth { get; set; } = 1200;
        public double WindowHeight { get; set; } = 800;
        public double WindowTop { get; set; } = 100;
        public double WindowLeft { get; set; } = 100;
        public bool WindowMaximized { get; set; } = true;
        public bool Show2Pages { get; set; } = true;
        public bool IsFullScreen { get; set; } = false;
        public string? LastPDFOpen { get; set; }
        public List<string> RootFolderMRU { get; set; } = new();
        public string ChooseBooksSort { get; set; } = "ByDate";
        public string ChooseQueryTab { get; set; } = "_Books";
        public double ChooseWindowWidth { get; set; } = 900;
        public double ChooseWindowHeight { get; set; } = 700;
        public double ChooseWindowTop { get; set; } = -1;
        public double ChooseWindowLeft { get; set; } = -1;
        public bool ChooseWindowMaximized { get; set; } = true;
        public double MetaDataWindowWidth { get; set; } = 1200;
        public double MetaDataWindowHeight { get; set; } = 700;
        public double MetaDataWindowTop { get; set; } = -1;
        public double MetaDataWindowLeft { get; set; } = -1;
        public bool MetaDataWindowMaximized { get; set; } = true;
    }
    
    /// <summary>
    /// Helper class for serializing only roaming settings
    /// </summary>
    private class RoamingSettings
    {
        public UserOptionsSettings UserOptions { get; set; } = new();
        public List<Playlist> Playlists { get; set; } = new();
        public string? LastSelectedPlaylist { get; set; }
    }
}
