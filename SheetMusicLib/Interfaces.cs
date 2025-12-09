namespace SheetMusicLib
{
    /// <summary>
    /// Interface for platform-specific thumbnail/image caching
    /// Implemented by WPF, Avalonia, or other UI frameworks
    /// </summary>
    public interface IThumbnailCache
    {
        /// <summary>
        /// Gets whether a cached thumbnail exists
        /// </summary>
        bool HasCachedThumbnail { get; }

        /// <summary>
        /// Clear the cached thumbnail to free memory
        /// </summary>
        void ClearThumbnailCache();
    }

    /// <summary>
    /// Interface for platform-specific PDF document operations
    /// Implemented by WPF (Windows.Data.Pdf), Avalonia (PdfiumViewer), etc.
    /// </summary>
    public interface IPdfDocumentProvider
    {
        /// <summary>
        /// Get the page count for a PDF file
        /// </summary>
        Task<int> GetPageCountAsync(string pdfFilePath);
    }

    /// <summary>
    /// Interface for exception/logging callbacks
    /// Allows platform-specific logging without UI dependencies
    /// </summary>
    public interface IExceptionHandler
    {
        /// <summary>
        /// Handle an exception with context message
        /// </summary>
        void OnException(string context, Exception ex);
    }
}
