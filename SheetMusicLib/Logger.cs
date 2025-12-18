using System.Diagnostics;
using System.Text;

namespace SheetMusicLib;

/// <summary>
/// Cross-platform logger that writes errors and exceptions to a file in LocalAppData.
/// Thread-safe and designed for use in desktop applications.
/// </summary>
public static class Logger
{
    private static readonly object _lock = new();
    private static string? _logFilePath;
    private static string _appName = "SheetMusicViewer";
    private static string _version = "Unknown";
    private static int _maxLogFileSizeBytes = 1024 * 1024; // 1 MB default
    private static int _maxLogFiles = 3; // Keep up to 3 rotated log files

    /// <summary>
    /// Initialize the logger with application information.
    /// Call this once at application startup.
    /// </summary>
    /// <param name="appName">Application name for the log folder and file</param>
    /// <param name="version">Version string to include in log entries</param>
    /// <param name="maxLogFileSizeBytes">Maximum log file size before rotation (default 1MB)</param>
    public static void Initialize(string appName, string version, int maxLogFileSizeBytes = 1024 * 1024)
    {
        _appName = appName;
        _version = version;
        _maxLogFileSizeBytes = maxLogFileSizeBytes;
        _logFilePath = null; // Reset to recalculate path
    }

    /// <summary>
    /// Gets the path to the log file.
    /// </summary>
    public static string LogFilePath
    {
        get
        {
            if (string.IsNullOrEmpty(_logFilePath))
            {
                var appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var logFolder = Path.Combine(appDataFolder, _appName, "Logs");
                
                // Ensure directory exists
                if (!Directory.Exists(logFolder))
                {
                    try
                    {
                        Directory.CreateDirectory(logFolder);
                    }
                    catch
                    {
                        // Fall back to temp directory if we can't create the log folder
                        logFolder = Path.GetTempPath();
                    }
                }
                
                _logFilePath = Path.Combine(logFolder, $"{_appName}.log");
            }
            return _logFilePath;
        }
    }

    /// <summary>
    /// Log an informational message.
    /// </summary>
    public static void LogInfo(string message)
    {
        WriteLog("INFO", message);
    }

    /// <summary>
    /// Log a warning message.
    /// </summary>
    public static void LogWarning(string message)
    {
        WriteLog("WARN", message);
    }

    /// <summary>
    /// Log an error message.
    /// </summary>
    public static void LogError(string message)
    {
        WriteLog("ERROR", message);
    }

    /// <summary>
    /// Log an exception with context message.
    /// </summary>
    /// <param name="context">Context describing what operation failed</param>
    /// <param name="exception">The exception that occurred</param>
    public static void LogException(string context, Exception exception)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{context}");
        sb.AppendLine($"  Exception: {exception.GetType().Name}: {exception.Message}");
        
        if (exception.StackTrace != null)
        {
            // Include abbreviated stack trace (first few frames)
            var stackLines = exception.StackTrace.Split('\n');
            var linesToInclude = Math.Min(stackLines.Length, 5);
            for (int i = 0; i < linesToInclude; i++)
            {
                sb.AppendLine($"  {stackLines[i].Trim()}");
            }
            if (stackLines.Length > 5)
            {
                sb.AppendLine($"  ... ({stackLines.Length - 5} more frames)");
            }
        }
        
        // Include inner exception if present
        if (exception.InnerException != null)
        {
            sb.AppendLine($"  Inner: {exception.InnerException.GetType().Name}: {exception.InnerException.Message}");
        }
        
        WriteLog("ERROR", sb.ToString());
    }

    /// <summary>
    /// Log an exception with context message (abbreviated version for user display).
    /// Returns a user-friendly error message.
    /// </summary>
    public static string LogExceptionAndGetUserMessage(string context, Exception exception)
    {
        LogException(context, exception);
        return $"{context}: {exception.Message}";
    }

    private static void WriteLog(string level, string message)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var machineName = Environment.MachineName;
            var logEntry = $"{timestamp} [{level}] v{_version} {machineName} | {message}";

            // Also write to trace for debugging
            Trace.WriteLine(logEntry);

            lock (_lock)
            {
                // Check if rotation is needed
                RotateLogIfNeeded();
                
                // Append to log file
                File.AppendAllText(LogFilePath, logEntry + Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            // If we can't write to the log file, at least output to trace
            Trace.WriteLine($"Logger.WriteLog failed: {ex.Message}");
            Trace.WriteLine($"Original message: [{level}] {message}");
        }
    }

    private static void RotateLogIfNeeded()
    {
        try
        {
            var logPath = LogFilePath;
            if (!File.Exists(logPath))
                return;

            var fileInfo = new FileInfo(logPath);
            if (fileInfo.Length < _maxLogFileSizeBytes)
                return;

            // Rotate existing log files
            for (int i = _maxLogFiles - 1; i >= 1; i--)
            {
                var oldPath = $"{logPath}.{i}";
                var newPath = $"{logPath}.{i + 1}";
                
                if (File.Exists(newPath))
                {
                    File.Delete(newPath);
                }
                if (File.Exists(oldPath))
                {
                    File.Move(oldPath, newPath);
                }
            }

            // Rotate current log to .1
            var rotatedPath = $"{logPath}.1";
            if (File.Exists(rotatedPath))
            {
                File.Delete(rotatedPath);
            }
            File.Move(logPath, rotatedPath);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Log rotation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Delete all log files. Useful for testing or cleanup.
    /// </summary>
    public static void ClearLogs()
    {
        lock (_lock)
        {
            try
            {
                var logPath = LogFilePath;
                if (File.Exists(logPath))
                {
                    File.Delete(logPath);
                }

                for (int i = 1; i <= _maxLogFiles; i++)
                {
                    var rotatedPath = $"{logPath}.{i}";
                    if (File.Exists(rotatedPath))
                    {
                        File.Delete(rotatedPath);
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"ClearLogs failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Read the contents of the current log file.
    /// Returns empty string if file doesn't exist or can't be read.
    /// </summary>
    public static string ReadLog()
    {
        try
        {
            var logPath = LogFilePath;
            if (File.Exists(logPath))
            {
                return File.ReadAllText(logPath);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"ReadLog failed: {ex.Message}");
        }
        return string.Empty;
    }

    /// <summary>
    /// Opens the log file location in the system file explorer.
    /// </summary>
    public static void OpenLogFolder()
    {
        try
        {
            var folder = Path.GetDirectoryName(LogFilePath);
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"OpenLogFolder failed: {ex.Message}");
        }
    }
}
