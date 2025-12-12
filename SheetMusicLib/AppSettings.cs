using System.Text.Json;
using System.Text.Json.Serialization;

namespace SheetMusicLib;

/// <summary>
/// Cross-platform application settings using JSON file storage.
/// Works on Windows, macOS, and Linux.
/// </summary>
public class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static string _settingsPath;
    private static AppSettings _instance;
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
    /// </summary>
    /// <param name="appName">Application name for the settings folder</param>
    public static void Initialize(string appName = "SheetMusicViewer")
    {
        var appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
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

    // Window position and size
    public double WindowWidth { get; set; } = 1200;
    public double WindowHeight { get; set; } = 800;
    public double WindowTop { get; set; } = 100;
    public double WindowLeft { get; set; } = 100;

    // View settings
    public bool Show2Pages { get; set; } = true;
    public bool IsFullScreen { get; set; } = false;

    // Last opened PDF
    public string LastPDFOpen { get; set; }

    // Most Recently Used root folders
    public List<string> RootFolderMRU { get; set; } = new();

    // Choose dialog settings
    public string ChooseBooksSort { get; set; } = "ByDate";
    public string ChooseQueryTab { get; set; } = "_Books";

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
            System.Diagnostics.Debug.WriteLine($"Error loading settings: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"Error saving settings: {ex.Message}");
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
    public string GetMostRecentFolder()
    {
        return RootFolderMRU.FirstOrDefault();
    }

    /// <summary>
    /// Reset all settings to defaults.
    /// </summary>
    public void Reset()
    {
        WindowWidth = 1200;
        WindowHeight = 800;
        WindowTop = 100;
        WindowLeft = 100;
        Show2Pages = true;
        IsFullScreen = false;
        LastPDFOpen = null;
        RootFolderMRU.Clear();
        ChooseBooksSort = "ByDate";
        ChooseQueryTab = "_Books";
    }
}
