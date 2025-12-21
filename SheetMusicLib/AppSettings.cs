using System.Text.Json;
using System.Text.Json.Serialization;

namespace SheetMusicLib;

/// <summary>
/// Cross-platform application settings using JSON file storage.
/// Works on Windows, macOS, and Linux.
/// Uses LocalApplicationData (not Roaming) because settings contain machine-specific
/// data like window positions, screen sizes, and file paths.
/// </summary>
public class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static string _settingsPath = null!;
    private static AppSettings? _instance;
    private static readonly object _lock = new();

    /// <summary>
    /// Gets the singleton instance of AppSettings.
    /// Call Initialize() first to set up the settings path.
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
    /// Uses LocalApplicationData because settings are machine-specific
    /// (window positions, file paths, screen sizes).
    /// </summary>
    /// <param name="appName">Application name for the settings folder</param>
    public static void Initialize(string appName = "SheetMusicViewer")
    {
        var appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _settingsPath = Path.Combine(appDataFolder, appName, "settings.json");
    }

    /// <summary>
    /// Gets the path where settings are stored.
    /// </summary>
    public static string SettingsPath
    {
        get
        {
            if (string.IsNullOrEmpty(_settingsPath))
            {
                Initialize();
            }
            return _settingsPath;
        }
    }

    #region User-Configurable Options (shown in Options dialog)

    /// <summary>
    /// User-configurable options that appear in the Options dialog.
    /// These are settings the user explicitly chooses to change.
    /// Access via: AppSettings.Instance.UserOptions.PropertyName
    /// </summary>
    public UserOptionsSettings UserOptions { get; set; } = new();

    /// <summary>
    /// Settings that appear in the Options dialog.
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

    #region Internal Persistence (window state, MRU, last opened files)

    // These settings are automatically saved/restored but not shown in Options dialog.
    // They represent application state rather than user preferences.

    // Main window position and size
    public double WindowWidth { get; set; } = 1200;
    public double WindowHeight { get; set; } = 800;
    public double WindowTop { get; set; } = 100;
    public double WindowLeft { get; set; } = 100;
    public bool WindowMaximized { get; set; } = true;

    // View settings (toggled via menu, not Options dialog)
    public bool Show2Pages { get; set; } = true;
    public bool IsFullScreen { get; set; } = false;

    // Last opened PDF
    public string? LastPDFOpen { get; set; }

    // Most Recently Used root folders
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
    /// Load settings from disk, or create new settings if file doesn't exist.
    /// </summary>
    public static AppSettings Load()
    {
        try
        {
            var path = SettingsPath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings != null)
                {
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Error loading settings: {ex.Message}");
        }
        return new AppSettings();
    }

    /// <summary>
    /// Save settings to disk.
    /// </summary>
    public void Save()
    {
        try
        {
            var path = SettingsPath;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Error saving settings: {ex.Message}");
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
    }

    /// <summary>
    /// Reset the singleton instance for testing purposes.
    /// Allows tests to use a custom settings path and fresh instance.
    /// </summary>
    /// <param name="testSettingsPath">Optional custom path for test settings file</param>
    public static void ResetForTesting(string? testSettingsPath = null)
    {
        lock (_lock)
        {
            _instance = null;
            if (!string.IsNullOrEmpty(testSettingsPath))
            {
                _settingsPath = testSettingsPath;
            }
            else
            {
                _settingsPath = null!; // Will use default path
            }
        }
    }
}
