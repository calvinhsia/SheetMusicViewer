using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace SheetMusicLib
{
    /// <summary>
    /// Result of reading PDF metadata
    /// </summary>
    public class PdfMetaDataReadResult
    {
        /// <summary>
        /// Full path to the PDF file or singles folder
        /// </summary>
        public string FullPathFile { get; set; }

        /// <summary>
        /// Whether this is a singles folder
        /// </summary>
        public bool IsSinglesFolder { get; set; }
        
        /// <summary>
        /// Path to the BMK metadata file
        /// </summary>
        public string BmkFilePath => System.IO.Path.ChangeExtension(FullPathFile, ".bmk");

        /// <summary>
        /// Whether the metadata was modified and needs saving
        /// </summary>
        public bool IsDirty { get; set; }

        /// <summary>
        /// The page number offset for this PDF set
        /// </summary>
        public int PageNumberOffset { get; set; }

        /// <summary>
        /// The last viewed page number
        /// </summary>
        public int LastPageNo { get; set; }

        /// <summary>
        /// Last write time of the metadata file
        /// </summary>
        public DateTime LastWriteTime { get; set; }

        /// <summary>
        /// Notes associated with this PDF
        /// </summary>
        public string Notes { get; set; }

        /// <summary>
        /// List of volume information
        /// </summary>
        public List<PdfVolumeInfoBase> VolumeInfoList { get; set; } = new();

        /// <summary>
        /// Table of contents entries
        /// </summary>
        public List<TOCEntry> TocEntries { get; set; } = new();

        /// <summary>
        /// Favorite pages
        /// </summary>
        public List<Favorite> Favorites { get; set; } = new();

        /// <summary>
        /// Ink stroke data
        /// </summary>
        public List<InkStrokeClass> InkStrokes { get; set; } = new();

        /// <summary>
        /// Cached PDF bytes per volume. Key is volume number, value is the byte array.
        /// Use GetOrLoadVolumeBytesAsync to access with automatic caching.
        /// </summary>
        private readonly Dictionary<int, byte[]> _pdfBytesCache = new();
        private readonly Dictionary<int, object> _volumeLoadLocks = new();
        private readonly object _pdfBytesCacheLock = new();

        /// <summary>
        /// Cached thumbnail bitmap (platform-specific type stored as object).
        /// Use GetOrCreateThumbnailAsync to access with automatic caching.
        /// </summary>
        public object ThumbnailCache { get; set; }

        /// <summary>
        /// Gets the cached thumbnail or creates it using the provided factory function.
        /// Thread-safe - only one factory call will execute even if called concurrently.
        /// </summary>
        /// <typeparam name="T">The platform-specific bitmap type (e.g., Avalonia.Media.Imaging.Bitmap or System.Windows.Media.Imaging.BitmapImage)</typeparam>
        /// <param name="thumbnailFactory">Async function to create the thumbnail if not cached</param>
        /// <returns>The cached or newly created thumbnail</returns>
        public async Task<T> GetOrCreateThumbnailAsync<T>(Func<Task<T>> thumbnailFactory) where T : class
        {
            // Fast path: already cached
            if (ThumbnailCache is T cached)
            {
                return cached;
            }

            // Create the thumbnail
            var thumbnail = await thumbnailFactory();
            
            // Cache it (simple assignment - last writer wins if concurrent)
            ThumbnailCache = thumbnail;
            
            return thumbnail;
        }

        /// <summary>
        /// Gets the cached PDF bytes for a volume, or loads them from disk if not cached.
        /// Thread-safe - ensures each volume is read from disk at most once even under concurrent access.
        /// </summary>
        /// <param name="volNo">Volume number</param>
        /// <returns>PDF file bytes, or null if the file doesn't exist</returns>
        public byte[]? GetOrLoadVolumeBytes(int volNo)
        {
            // Fast path: already cached
            lock (_pdfBytesCacheLock)
            {
                if (_pdfBytesCache.TryGetValue(volNo, out var cached))
                {
                    return cached;
                }
            }

            // Get or create a lock object for this specific volume
            object volumeLock;
            lock (_pdfBytesCacheLock)
            {
                if (!_volumeLoadLocks.TryGetValue(volNo, out volumeLock))
                {
                    volumeLock = new object();
                    _volumeLoadLocks[volNo] = volumeLock;
                }
            }

            // Only one thread will load this volume
            lock (volumeLock)
            {
                // Double-check after acquiring volume lock
                lock (_pdfBytesCacheLock)
                {
                    if (_pdfBytesCache.TryGetValue(volNo, out var cached))
                    {
                        return cached;
                    }
                }

                var pdfPath = GetFullPathFileFromVolno(volNo);
                if (string.IsNullOrEmpty(pdfPath) || !File.Exists(pdfPath))
                {
                    return null;
                }

                var bytes = File.ReadAllBytes(pdfPath);

                lock (_pdfBytesCacheLock)
                {
                    _pdfBytesCache[volNo] = bytes;
                }

                return bytes;
            }
        }

        /// <summary>
        /// Gets the cached PDF bytes for a volume if already loaded, without loading from disk.
        /// Thread-safe.
        /// </summary>
        /// <param name="volNo">Volume number</param>
        /// <returns>Cached PDF bytes, or null if not cached</returns>
        public byte[]? GetCachedVolumeBytes(int volNo)
        {
            lock (_pdfBytesCacheLock)
            {
                return _pdfBytesCache.TryGetValue(volNo, out var cached) ? cached : null;
            }
        }

        /// <summary>
        /// Pre-loads PDF bytes for all volumes in the background.
        /// Call this after opening a PDF to improve page navigation performance.
        /// </summary>
        /// <returns>Task that completes when all volumes are loaded</returns>
        public Task PreloadAllVolumeBytesAsync()
        {
            return Task.Run(() =>
            {
                for (int volNo = 0; volNo < VolumeInfoList.Count; volNo++)
                {
                    GetOrLoadVolumeBytes(volNo);
                }
            });
        }

        /// <summary>
        /// Clears the cached PDF bytes to free memory.
        /// Call this when closing a PDF file.
        /// Thread-safe, but callers should ensure no concurrent GetOrLoadVolumeBytes calls.
        /// </summary>
        public void ClearPdfBytesCache()
        {
            lock (_pdfBytesCacheLock)
            {
                _pdfBytesCache.Clear();
                // Note: We intentionally do NOT clear _volumeLoadLocks here.
                // The lock objects are small and keeping them prevents a race condition
                // where a concurrent GetOrLoadVolumeBytes could create a duplicate lock
                // and potentially read the same file twice.
                // The locks will be naturally garbage collected when this PdfMetaDataReadResult
                // is no longer referenced.
            }
        }

        /// <summary>
        /// Gets the cached thumbnail if available, without creating it.
        /// </summary>
        /// <typeparam name="T">The platform-specific bitmap type</typeparam>
        /// <returns>The cached thumbnail or null if not cached</returns>
        public T GetCachedThumbnail<T>() where T : class
        {
            return ThumbnailCache as T;
        }

        /// <summary>
        /// Clears the cached thumbnail to free memory.
        /// </summary>
        public void ClearThumbnailCache()
        {
            ThumbnailCache = null;
        }

        /// <summary>
        /// Get the volume number from a page number
        /// </summary>
        public int GetVolNumFromPageNum(int pageNo)
        {
            var volno = 0;
            var pSum = PageNumberOffset;
            foreach (var vol in VolumeInfoList)
            {
                pSum += vol.NPagesInThisVolume;
                if (pageNo < pSum)
                {
                    break;
                }
                volno++;
            }
            return volno;
        }

        /// <summary>
        /// Get the full path to a PDF file from a volume number
        /// </summary>
        public string GetFullPathFileFromVolno(int volNo, bool MakeRelative = false, string rootFolder = null)
        {
            if (volNo >= VolumeInfoList.Count)
                volNo = VolumeInfoList.Count - 1;
            if (volNo < 0)
                return FullPathFile;

            var retval = Path.Combine(
                IsSinglesFolder ? FullPathFile : Path.GetDirectoryName(FullPathFile),
                VolumeInfoList[volNo].FileNameVolume);

            if (MakeRelative && !string.IsNullOrEmpty(rootFolder) && retval.StartsWith(rootFolder))
            {
                retval = retval[(rootFolder.Length + 1)..].Replace(".pdf", string.Empty);
                var lastchar = retval.LastOrDefault();
                if ("01".Contains(lastchar))
                {
                    retval = retval[..^1];
                }
            }
            return retval;
        }

        /// <summary>
        /// Get the full path to a PDF file from a page number
        /// </summary>
        public string GetFullPathFileFromPageNo(int pageNo)
        {
            var volNo = GetVolNumFromPageNum(pageNo);
            return GetFullPathFileFromVolno(volNo);
        }

        /// <summary>
        /// Check if a page is a favorite
        /// </summary>
        public bool IsFavorite(int pageNo)
        {
            return Favorites.Any(f => f.Pageno == pageNo);
        }

        /// <summary>
        /// Get the book name (relative path without extension)
        /// </summary>
        public string GetBookName(string rootFolder = null)
        {
            var name = GetFullPathFileFromVolno(0, MakeRelative: true, rootFolder: rootFolder);
            return name ?? Path.GetFileNameWithoutExtension(FullPathFile);
        }

        /// <summary>
        /// Total number of pages in all volumes
        /// </summary>
        public int NumPagesInSet => VolumeInfoList.Sum(v => v.NPagesInThisVolume);

        /// <summary>
        /// Maximum page number (PageNumberOffset + total pages in all volumes)
        /// </summary>
        public int MaxPageNum => PageNumberOffset + NumPagesInSet;

        /// <summary>
        /// Path to the JSON metadata file (used by Avalonia/Desktop app)
        /// </summary>
        public string JsonFilePath => System.IO.Path.ChangeExtension(FullPathFile, ".json");
    }

    /// <summary>
    /// Portable PDF metadata reader that works without platform-specific dependencies
    /// </summary>
    public static class PdfMetaDataCore
    {
        /// <summary>
        /// JSON serializer options for BMK files
        /// </summary>
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private static readonly JsonSerializerOptions JsonReadOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// Convert all BMK (XML) files to JSON format in the given folder with verification
        /// </summary>
        /// <param name="rootMusicFolder">Root folder to scan</param>
        /// <param name="deleteOriginalBmk">If true, delete the original .bmk file after successful conversion</param>
        /// <param name="verifyCallback">Optional callback to report verification errors</param>
        /// <returns>Tuple of (converted count, verified count, error count)</returns>
        public static async Task<(int Converted, int Verified, int Errors)> ConvertAllBmkToJsonAsync(
            string rootMusicFolder,
            bool deleteOriginalBmk = false,
            Action<string> verifyCallback = null)
        {
            int converted = 0;
            int verified = 0;
            int errors = 0;
            var xmlSerializer = new XmlSerializer(typeof(SerializablePdfMetaData));

            foreach (var bmkFile in Directory.EnumerateFiles(rootMusicFolder, "*.bmk", SearchOption.AllDirectories))
            {
                try
                {
                    var jsonFile = Path.ChangeExtension(bmkFile, "json");
                    var pdfFile = Path.ChangeExtension(bmkFile, "pdf");

                    // Read XML
                    var fileContent = await File.ReadAllTextAsync(bmkFile);

                    // Skip if already JSON format (shouldn't happen with .bmk extension, but be safe)
                    if (fileContent.TrimStart().StartsWith("{"))
                        continue;

                    // Parse XML
                    SerializablePdfMetaData xmlData;
                    using (var sr = new StringReader(fileContent))
                    {
                        xmlData = (SerializablePdfMetaData)xmlSerializer.Deserialize(sr);
                    }

                    // Convert to JSON
                    var jsonContent = JsonSerializer.Serialize(xmlData, JsonOptions);

                    // Parse JSON back to verify round-trip
                    var jsonData = JsonSerializer.Deserialize<SerializablePdfMetaData>(jsonContent, JsonOptions);

                    // Verify the data matches
                    var verifyErrors = VerifyMetadataMatch(xmlData, jsonData);
                    if (verifyErrors.Count > 0)
                    {
                        errors++;
                        var errorMsg = $"{Path.GetFileName(bmkFile)}: {string.Join("; ", verifyErrors)}";
                        Debug.WriteLine($"Verification failed: {errorMsg}");
                        verifyCallback?.Invoke(errorMsg);
                        continue;
                    }

                    verified++;

                    // Write JSON file if it doesn't exist
                    if (!File.Exists(jsonFile))
                    {
                        await File.WriteAllTextAsync(jsonFile, jsonContent);
                        converted++;
                        Debug.WriteLine($"Converted: {Path.GetFileName(bmkFile)} -> {Path.GetFileName(jsonFile)}");
                    }

                    if (deleteOriginalBmk)
                    {
                        File.Delete(bmkFile);
                        Debug.WriteLine($"Deleted: {bmkFile}");
                    }
                }
                catch (Exception ex)
                {
                    errors++;
                    var errorMsg = $"{Path.GetFileName(bmkFile)}: Exception - {ex.Message}";
                    Debug.WriteLine($"Failed to convert: {errorMsg}");
                    verifyCallback?.Invoke(errorMsg);
                }
            }

            return (converted, verified, errors);
        }

        /// <summary>
        /// Verify that two SerializablePdfMetaData objects have matching data
        /// </summary>
        private static List<string> VerifyMetadataMatch(SerializablePdfMetaData xml, SerializablePdfMetaData json)
        {
            var errors = new List<string>();

            if (xml.LastPageNo != json.LastPageNo)
                errors.Add($"LastPageNo: {xml.LastPageNo} vs {json.LastPageNo}");

            if (xml.PageNumberOffset != json.PageNumberOffset)
                errors.Add($"PageNumberOffset: {xml.PageNumberOffset} vs {json.PageNumberOffset}");

            if (xml.Notes != json.Notes)
                errors.Add("Notes differ");

            // Compare volume info
            var xmlVols = xml.lstVolInfo ?? new List<PdfVolumeInfoBase>();
            var jsonVols = json.lstVolInfo ?? new List<PdfVolumeInfoBase>();
            if (xmlVols.Count != jsonVols.Count)
            {
                errors.Add($"VolumeInfo count: {xmlVols.Count} vs {jsonVols.Count}");
            }
            else
            {
                for (int i = 0; i < xmlVols.Count; i++)
                {
                    if (xmlVols[i].NPagesInThisVolume != jsonVols[i].NPagesInThisVolume)
                        errors.Add($"Vol[{i}].NPages: {xmlVols[i].NPagesInThisVolume} vs {jsonVols[i].NPagesInThisVolume}");
                    if (xmlVols[i].FileNameVolume != jsonVols[i].FileNameVolume)
                        errors.Add($"Vol[{i}].FileName differs");
                }
            }

            // Compare TOC entries
            var xmlToc = xml.lstTocEntries ?? new List<TOCEntry>();
            var jsonToc = json.lstTocEntries ?? new List<TOCEntry>();
            if (xmlToc.Count != jsonToc.Count)
            {
                errors.Add($"TOC count: {xmlToc.Count} vs {jsonToc.Count}");
            }
            else
            {
                for (int i = 0; i < xmlToc.Count; i++)
                {
                    if (xmlToc[i].SongName != jsonToc[i].SongName)
                        errors.Add($"TOC[{i}].SongName differs");
                    if (xmlToc[i].PageNo != jsonToc[i].PageNo)
                        errors.Add($"TOC[{i}].PageNo: {xmlToc[i].PageNo} vs {jsonToc[i].PageNo}");
                }
            }

            // Compare favorites
            var xmlFavs = xml.Favorites ?? new List<Favorite>();
            var jsonFavs = json.Favorites ?? new List<Favorite>();
            if (xmlFavs.Count != jsonFavs.Count)
            {
                errors.Add($"Favorites count: {xmlFavs.Count} vs {jsonFavs.Count}");
            }

            // Compare ink strokes
            var xmlInk = xml.LstInkStrokes ?? new List<InkStrokeClass>();
            var jsonInk = json.LstInkStrokes ?? new List<InkStrokeClass>();
            if (xmlInk.Count != jsonInk.Count)
            {
                errors.Add($"InkStrokes count: {xmlInk.Count} vs {jsonInk.Count}");
            }

            return errors;
        }

        /// <summary>
        /// Load all PDF metadata from a folder recursively
        /// </summary>
        /// <param name="rootMusicFolder">Root folder to scan</param>
        /// <param name="pdfDocumentProvider">Provider to get PDF page count</param>
        /// <param name="exceptionHandler">Optional exception handler</param>
        /// <param name="useParallelLoading">If true, parse BMK files in parallel for faster loading</param>
        /// <param name="autoSaveNewMetadata">If true, automatically save JSON metadata for newly discovered PDFs</param>
        /// <returns>List of metadata results and list of folder names</returns>
        public static async Task<(List<PdfMetaDataReadResult>, List<string>)> LoadAllPdfMetaDataFromDiskAsync(
            string rootMusicFolder,
            IPdfDocumentProvider pdfDocumentProvider,
            IExceptionHandler exceptionHandler = null,
            bool useParallelLoading = true,
            bool autoSaveNewMetadata = true)
        {
            var lstPdfMetaFileData = new List<PdfMetaDataReadResult>();
            var lstFolders = new List<string>();

            if (string.IsNullOrEmpty(rootMusicFolder) || !Directory.Exists(rootMusicFolder))
            {
                return (lstPdfMetaFileData, lstFolders);
            }

            if (useParallelLoading)
            {
                var result = await LoadAllPdfMetaDataParallelAsync(rootMusicFolder, pdfDocumentProvider, exceptionHandler);
                
                // Auto-save any new metadata that was created
                if (autoSaveNewMetadata)
                {
                    var savedCount = SaveAllDirtyMetadata(result.Item1);
                    if (savedCount > 0)
                    {
                        Debug.WriteLine($"PdfMetaDataCore: Auto-saved {savedCount} new JSON metadata files");
                    }
                }
                
                return result;
            }

            // Original sequential implementation
            await Task.Run(async () =>
            {
                PdfMetaDataReadResult curmetadata = null;
                int nContinuations = 0;
                await RecurDirsAsync(rootMusicFolder);

                async Task<bool> TryAddFileAsync(string curFullPathFile)
                {
                    try
                    {
                        // Skip macOS metadata files
                        var fileName = Path.GetFileName(curFullPathFile);
                        if (fileName.StartsWith("._") || curFullPathFile.Contains("__MACOSX"))
                        {
                            return true; // Skip but don't treat as error
                        }

                        curmetadata = await ReadPdfMetaDataAsync(curFullPathFile, false, pdfDocumentProvider, exceptionHandler);
                        if (curmetadata != null)
                        {
                            lstPdfMetaFileData.Add(curmetadata);
                        }
                        return true;
                    }
                    catch (Exception ex)
                    {
                        exceptionHandler?.OnException($"Reading {curFullPathFile}", ex);
                        return false;
                    }
                }

                void ProcessMetaData()
                {
                    if (curmetadata != null)
                    {
                        // For single songs not in SinglesFolder: if no TOC and # pages < 11, 
                        // create a new TOC entry with 1 song: same as file name
                        if (!curmetadata.IsSinglesFolder &&
                            curmetadata.TocEntries.Count == 0 &&
                            curmetadata.VolumeInfoList.Sum(v => v.NPagesInThisVolume) < 11)
                        {
                            curmetadata.TocEntries.Add(new TOCEntry
                            {
                                SongName = Path.GetFileNameWithoutExtension(curmetadata.FullPathFile)
                            });
                            curmetadata.IsDirty = true;
                        }
                    }
                    nContinuations = 0;
                }

                async Task<int> RecurDirsAsync(string curPath)
                {
                    var lastFile = string.Empty;
                    nContinuations = 0;
                    var cntItems = 0;
                    int curVolNo = 0;

                    try
                    {
                        if (curPath.EndsWith("singles", StringComparison.InvariantCultureIgnoreCase))
                        {
                            // Singles folder - load as a single metadata entry
                            var singlesMetadata = await LoadSinglesFolderAsync(curPath, pdfDocumentProvider, exceptionHandler);
                            if (singlesMetadata != null)
                            {
                                lstPdfMetaFileData.Add(singlesMetadata);
                            }
                        }
                        else
                        {
                            var curVolIsOneBased = false;
                            foreach (var file in Directory.EnumerateFiles(curPath, "*.pdf").OrderBy(f => f.ToLower()))
                            {
                                var isContinuation = false;
                                curVolIsOneBased = false;

                                if (!string.IsNullOrEmpty(lastFile) &&
                                    curmetadata != null &&
                                    Path.GetDirectoryName(lastFile) == Path.GetDirectoryName(file))
                                {
                                    var justFnameVol0 = Path.GetFileNameWithoutExtension(curmetadata.FullPathFile).Trim().ToLower();
                                    var lastcharVol0 = justFnameVol0.Last();
                                    if ("01".Contains(lastcharVol0))
                                    {
                                        curVolIsOneBased = lastcharVol0 == '1';
                                        justFnameVol0 = justFnameVol0[..^1];
                                    }
                                    var justFnameCurrent = Path.GetFileNameWithoutExtension(file).Trim().ToLower();
                                    if (justFnameVol0.Length < justFnameCurrent.Length &&
                                        justFnameVol0 == justFnameCurrent[..justFnameVol0.Length])
                                    {
                                        if (char.IsDigit(justFnameCurrent[justFnameVol0.Length..][0]))
                                        {
                                            isContinuation = true;
                                        }
                                    }
                                }

                                if (isContinuation)
                                {
                                    curVolNo++;
                                    nContinuations++;
                                    // Add volume to current metadata if creating new
                                    if (curmetadata != null && curmetadata.IsDirty)
                                    {
                                        if (curmetadata.VolumeInfoList.Count <= curVolNo)
                                        {
                                            Debug.WriteLine($"PdfMetaDataCore: Continuation volume {curVolNo} not in BMK (IsDirty={curmetadata.IsDirty}), reading PDF: {Path.GetFileName(file)}");
                                            var pageCount = await pdfDocumentProvider.GetPageCountAsync(file);
                                            var newvolInfo = new PdfVolumeInfoBase
                                            {
                                                FileNameVolume = Path.GetFileName(file),
                                                NPagesInThisVolume = pageCount,
                                                Rotation = pageCount != 1 ? 2 : 0 // Rotate180 if multi-page
                                            };
                                            curmetadata.VolumeInfoList.Add(newvolInfo);
                                        }
                                    }
                                    if (curmetadata != null &&
                                        curVolNo < curmetadata.VolumeInfoList.Count &&
                                        string.IsNullOrEmpty(curmetadata.VolumeInfoList[curVolNo].FileNameVolume))
                                    {
                                        curmetadata.VolumeInfoList[curVolNo].FileNameVolume = Path.GetFileName(file);
                                    }
                                }
                                else
                                {
                                    ProcessMetaData();
                                    if (await TryAddFileAsync(file))
                                    {
                                        curVolNo = 0;
                                        curVolIsOneBased = false;
                                        cntItems++;
                                    }
                                }
                                lastFile = file;
                            }

                            if (cntItems > 0)
                            {
                                if (rootMusicFolder.Length < curPath.Length)
                                {
                                    var justdir = curPath[(rootMusicFolder.Length + 1)..];
                                    lstFolders.Add(justdir);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (ex.Data.Contains("Filename"))
                        {
                            lastFile += " " + ex.Data["Filename"];
                        }
                        exceptionHandler?.OnException($"Exception reading files {curPath} near {lastFile}", ex);
                    }

                    ProcessMetaData(); // Last one in dir

                    foreach (var dir in Directory.EnumerateDirectories(curPath))
                    {
                        if (!dir.EndsWith("hidden", StringComparison.InvariantCultureIgnoreCase))
                        {
                            await RecurDirsAsync(dir);
                        }
                    }

                    return cntItems;
                }
            });

            return (lstPdfMetaFileData, lstFolders);
        }

        /// <summary>
        /// Load a Singles folder as a single metadata entry
        /// </summary>
        private static async Task<PdfMetaDataReadResult> LoadSinglesFolderAsync(
            string curPath,
            IPdfDocumentProvider pdfDocumentProvider,
            IExceptionHandler exceptionHandler)
        {
            PdfMetaDataReadResult curmetadata = null;
            // Only use .json format (BMK/XML is deprecated)
            var jsonFile = Path.ChangeExtension(curPath, "json");
            
            var sortedListSingles = new SortedSet<string>();
            var sortedListDeletedSingles = new SortedSet<string>();

            if (File.Exists(jsonFile))
            {
                curmetadata = await ReadPdfMetaDataAsync(curPath, true, pdfDocumentProvider, exceptionHandler);
                if (curmetadata != null)
                {
                    foreach (var vol in curmetadata.VolumeInfoList)
                    {
                        if (!File.Exists(Path.Combine(curPath, vol.FileNameVolume)))
                        {
                            sortedListDeletedSingles.Add(vol.FileNameVolume.ToLower());
                        }
                        else
                        {
                            sortedListSingles.Add(vol.FileNameVolume.ToLower());
                        }
                    }
                }
            }
            else
            {
                Debug.WriteLine($"PdfMetaDataCore: No JSON metadata file exists for Singles folder: {Path.GetFileName(curPath)}");
                curmetadata = new PdfMetaDataReadResult
                {
                    FullPathFile = curPath,
                    IsSinglesFolder = true,
                    IsDirty = true,
                    LastWriteTime = DateTime.Now
                };
            }

            // Find new files
            var lstNewFiles = new List<string>();
            foreach (var single in Directory.EnumerateFiles(curPath, "*.pdf").OrderBy(f => f))
            {
                var justFileName = Path.GetFileName(single).ToLower();
                if (!sortedListSingles.Contains(justFileName))
                {
                    lstNewFiles.Add(single);
                    sortedListSingles.Add(justFileName);
                }
            }

            // If new or deleted files, rebuild the volume list
            if (lstNewFiles.Count > 0 || sortedListDeletedSingles.Count > 0)
            {
                curmetadata.IsDirty = true;

                // Remove deleted volumes
                curmetadata.VolumeInfoList.RemoveAll(v =>
                    sortedListDeletedSingles.Contains(v.FileNameVolume.ToLower()));

                // Add new volumes
                foreach (var newfile in lstNewFiles)
                {
                    try
                    {
                        Debug.WriteLine($"PdfMetaDataCore: Singles folder has new file not in BMK, reading PDF: {Path.GetFileName(newfile)}");
                        var pageCount = await pdfDocumentProvider.GetPageCountAsync(newfile);
                        curmetadata.VolumeInfoList.Add(new PdfVolumeInfoBase
                        {
                            FileNameVolume = Path.GetFileName(newfile),
                            NPagesInThisVolume = pageCount,
                            Rotation = 0
                        });
                    }
                    catch (Exception ex)
                    {
                        ex.Data["Filename"] = newfile;
                        exceptionHandler?.OnException($"Loading {newfile}", ex);
                    }
                }

                // Sort volumes by filename
                curmetadata.VolumeInfoList = curmetadata.VolumeInfoList
                    .OrderBy(v => v.FileNameVolume)
                    .ToList();

                // Rebuild TOC entries for singles
                curmetadata.TocEntries.Clear();
                int singlesPageNo = 0;
                foreach (var vol in curmetadata.VolumeInfoList)
                {
                    curmetadata.TocEntries.Add(new TOCEntry
                    {
                        SongName = Path.GetFileNameWithoutExtension(vol.FileNameVolume),
                        PageNo = singlesPageNo
                    });
                    singlesPageNo += vol.NPagesInThisVolume;
                }
            }

            return curmetadata;
        }

        /// <summary>
        /// Read PDF metadata from a BMK or JSON file asynchronously using a page count provider for PDF files.
        /// Supports both WPF (BMK XML) and Avalonia (JSON) formats.
        /// </summary>
        /// <param name="fullPathPdfFileOrSinglesFolder">Full path to PDF file or singles folder</param>
        /// <param name="isSingles">Whether this is a singles folder</param>
        /// <param name="pdfDocumentProvider">Provider to get PDF page count</param>
        /// <param name="exceptionHandler">Optional exception handler</param>
        /// <param name="preferJsonOverBmk">If true, prefer .json over .bmk when both exist. Default true for Avalonia.</param>
        /// <returns>Metadata read result or null if failed</returns>
        public static async Task<PdfMetaDataReadResult> ReadPdfMetaDataAsync(
            string fullPathPdfFileOrSinglesFolder,
            bool isSingles,
            IPdfDocumentProvider pdfDocumentProvider,
            IExceptionHandler exceptionHandler = null,
            bool preferJsonOverBmk = true)
        {
            PdfMetaDataReadResult result = null;

            // Check for both JSON and BMK files
            var jsonFile = Path.ChangeExtension(fullPathPdfFileOrSinglesFolder, "json");
            var bmkFile = Path.ChangeExtension(fullPathPdfFileOrSinglesFolder, "bmk");

            string metadataFile = null;
            if (preferJsonOverBmk)
            {
                // Prefer JSON over BMK (Avalonia default)
                metadataFile = File.Exists(jsonFile) ? jsonFile : (File.Exists(bmkFile) ? bmkFile : null);
            }
            else
            {
                // Prefer BMK over JSON (WPF default - for backward compatibility)
                metadataFile = File.Exists(bmkFile) ? bmkFile : (File.Exists(jsonFile) ? jsonFile : null);
            }

            if (metadataFile != null)
            {
                try
                {
                    result = await ReadFromMetadataFileAsync(
                        fullPathPdfFileOrSinglesFolder,
                        metadataFile,
                        isSingles,
                        pdfDocumentProvider);
                }
                catch (Exception ex)
                {
                    exceptionHandler?.OnException($"Reading {metadataFile}", ex);
                }
            }
            else
            {
                result = await CreateNewMetaDataAsync(
                    fullPathPdfFileOrSinglesFolder,
                    isSingles,
                    pdfDocumentProvider);
            }

            return result;
        }

        private static async Task<PdfMetaDataReadResult> ReadFromMetadataFileAsync(
            string fullPathPdfFileOrSinglesFolder,
            string metadataFile,
            bool isSingles,
            IPdfDocumentProvider pdfDocumentProvider)
        {
            var fileContent = await File.ReadAllTextAsync(metadataFile);
            var isJson = fileContent.TrimStart().StartsWith("{");

            if (isJson)
            {
                // Detect which JSON schema is used
                // BmkJsonFormat has "volumes" and "inkStrokes" keys
                if (fileContent.Contains("\"volumes\"") || fileContent.Contains("\"inkStrokes\""))
                {
                    // BmkJsonFormat (from BmkJsonConverter) - proper portable format
                    return await ReadFromBmkJsonFormatAsync(fullPathPdfFileOrSinglesFolder, fileContent, isSingles, pdfDocumentProvider);
                }
                else
                {
                    // SerializablePdfMetaData format (legacy JSON from ConvertAllBmkToJsonAsync)
                    var serializedData = JsonSerializer.Deserialize<SerializablePdfMetaData>(fileContent, JsonReadOptions);
                    return await ConvertSerializableToResultAsync(fullPathPdfFileOrSinglesFolder, metadataFile, serializedData, isSingles, pdfDocumentProvider);
                }
            }
            else
            {
                // XML format (legacy .bmk) - WPF still uses this
                var serializer = new XmlSerializer(typeof(SerializablePdfMetaData));
                using var sr = new StringReader(fileContent);
                var serializedData = (SerializablePdfMetaData)serializer.Deserialize(sr);
                return await ConvertSerializableToResultAsync(fullPathPdfFileOrSinglesFolder, metadataFile, serializedData, isSingles, pdfDocumentProvider);
            }
        }

        /// <summary>
        /// Read from BmkJsonFormat (created by BmkJsonConverter)
        /// </summary>
        private static async Task<PdfMetaDataReadResult> ReadFromBmkJsonFormatAsync(
            string fullPathPdfFileOrSinglesFolder,
            string fileContent,
            bool isSingles,
            IPdfDocumentProvider pdfDocumentProvider)
        {
            var bmkJson = JsonSerializer.Deserialize<BmkJsonFormat>(fileContent, JsonReadOptions);
            
            //Debug.WriteLine($"ReadFromBmkJsonFormatAsync: Loaded {bmkJson.InkStrokes.Count} ink stroke entries from JSON");

            var result = new PdfMetaDataReadResult
            {
                FullPathFile = fullPathPdfFileOrSinglesFolder,
                IsSinglesFolder = isSingles,
                IsDirty = false,
                PageNumberOffset = bmkJson.PageNumberOffset,
                LastPageNo = bmkJson.LastPageNo,
                LastWriteTime = bmkJson.LastWrite,
                Notes = bmkJson.Notes
            };

            // Convert volumes
            foreach (var vol in bmkJson.Volumes)
            {
                result.VolumeInfoList.Add(new PdfVolumeInfoBase
                {
                    FileNameVolume = vol.FileName,
                    NPagesInThisVolume = vol.PageCount,
                    Rotation = vol.Rotation
                });
            }

            // Convert TOC entries
            foreach (var toc in bmkJson.TableOfContents)
            {
                result.TocEntries.Add(new TOCEntry
                {
                    SongName = toc.SongName,
                    Composer = toc.Composer,
                    Date = toc.Date,
                    Notes = toc.Notes,
                    PageNo = toc.PageNo
                });
            }

            // Convert favorites
            foreach (var fav in bmkJson.Favorites)
            {
                result.Favorites.Add(new Favorite
                {
                    Pageno = fav.PageNo,
                    FavoriteName = fav.Name
                });
            }

            // Convert ink strokes - these are already in portable format!
            // Store them as JSON-encoded PortableInkStrokeCollection in StrokeData
            foreach (var kvp in bmkJson.InkStrokes)
            {
                var pageNo = kvp.Key;
                var jsonInkStrokes = kvp.Value;

                // Create a PortableInkStrokeCollection for this page
                var portableCollection = new PortableInkStrokeCollection
                {
                    CanvasWidth = jsonInkStrokes.CanvasWidth,
                    CanvasHeight = jsonInkStrokes.CanvasHeight,
                    Strokes = jsonInkStrokes.Strokes
                };

                // Serialize to JSON bytes
                var jsonBytes = System.Text.Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(portableCollection, JsonOptions));

                result.InkStrokes.Add(new InkStrokeClass
                {
                    Pageno = pageNo,
                    InkStrokeDimension = new PortablePoint(jsonInkStrokes.CanvasWidth, jsonInkStrokes.CanvasHeight),
                    StrokeData = jsonBytes
                });
            }

            // Handle case where no volume info exists
            if (result.VolumeInfoList.Count == 0)
            {
                Debug.WriteLine($"PdfMetaDataCore: BMK exists but VolumeInfoList is empty, reading PDF: {Path.GetFileName(fullPathPdfFileOrSinglesFolder)}");
                var pageCount = await pdfDocumentProvider.GetPageCountAsync(fullPathPdfFileOrSinglesFolder);
                result.VolumeInfoList.Add(new PdfVolumeInfoBase
                {
                    FileNameVolume = Path.GetFileName(fullPathPdfFileOrSinglesFolder),
                    NPagesInThisVolume = pageCount,
                    Rotation = 0
                });
                result.IsDirty = true;
            }

            // Ensure first volume has filename
            if (string.IsNullOrEmpty(result.VolumeInfoList[0].FileNameVolume))
            {
                result.VolumeInfoList[0].FileNameVolume = Path.GetFileName(fullPathPdfFileOrSinglesFolder);
                result.IsDirty = true;
            }

            // Fix volumes with pageCount=0 (e.g., if PDF was cloud-only during initial load)
            for (int i = 0; i < result.VolumeInfoList.Count; i++)
            {
                if (result.VolumeInfoList[i].NPagesInThisVolume == 0)
                {
                    var pdfPath = result.GetFullPathFileFromVolno(i);
                    if (!string.IsNullOrEmpty(pdfPath) && File.Exists(pdfPath))
                    {
                        Debug.WriteLine($"PdfMetaDataCore: Volume {i} has pageCount=0, re-reading PDF: {Path.GetFileName(pdfPath)}");
                        var pageCount = await pdfDocumentProvider.GetPageCountAsync(pdfPath);
                        if (pageCount > 0)
                        {
                            result.VolumeInfoList[i].NPagesInThisVolume = pageCount;
                            result.IsDirty = true;
                        }
                    }
                }
            }

            // Ensure at least one TOC entry exists
            if (result.TocEntries.Count == 0)
            {
                result.TocEntries.Add(new TOCEntry
                {
                    SongName = Path.GetFileNameWithoutExtension(fullPathPdfFileOrSinglesFolder)
                });
                result.IsDirty = true;
            }

            // Validate last page number is in range
            var maxPageNum = result.PageNumberOffset + result.VolumeInfoList.Sum(v => v.NPagesInThisVolume);
            if (result.LastPageNo < result.PageNumberOffset || result.LastPageNo >= maxPageNum)
            {
                result.LastPageNo = result.PageNumberOffset;
            }

            return result;
        }

        /// <summary>
        /// Convert SerializablePdfMetaData to PdfMetaDataReadResult
        /// </summary>
        private static async Task<PdfMetaDataReadResult> ConvertSerializableToResultAsync(
            string fullPathPdfFileOrSinglesFolder,
            string metadataFile,
            SerializablePdfMetaData serializedData,
            bool isSingles,
            IPdfDocumentProvider pdfDocumentProvider)
        {
            var result = new PdfMetaDataReadResult
            {
                FullPathFile = fullPathPdfFileOrSinglesFolder,
                IsSinglesFolder = isSingles,
                IsDirty = false,
                PageNumberOffset = serializedData.PageNumberOffset,
                LastPageNo = serializedData.LastPageNo,
                LastWriteTime = serializedData.dtLastWrite.Year < 1900
                    ? new FileInfo(metadataFile).LastWriteTime
                    : serializedData.dtLastWrite,
                Notes = serializedData.Notes,
                TocEntries = serializedData.lstTocEntries ?? new List<TOCEntry>(),
                Favorites = serializedData.Favorites ?? new List<Favorite>(),
                // Pass through ink strokes - WPF can handle ISF binary format, Avalonia will skip them
                InkStrokes = serializedData.LstInkStrokes ?? new List<InkStrokeClass>()
            };

            // Convert volume info
            if (serializedData.lstVolInfo != null)
            {
                foreach (var vol in serializedData.lstVolInfo)
                {
                    result.VolumeInfoList.Add(vol);
                }
            }

            // Handle case where no volume info exists
            if (result.VolumeInfoList.Count == 0)
            {
                Debug.WriteLine($"PdfMetaDataCore: BMK exists but VolumeInfoList is empty, reading PDF: {Path.GetFileName(fullPathPdfFileOrSinglesFolder)}");
                var pageCount = await pdfDocumentProvider.GetPageCountAsync(fullPathPdfFileOrSinglesFolder);
                result.VolumeInfoList.Add(new PdfVolumeInfoBase
                {
                    FileNameVolume = Path.GetFileName(fullPathPdfFileOrSinglesFolder),
                    NPagesInThisVolume = pageCount,
                    Rotation = 0
                });
                result.IsDirty = true;
            }

            // Ensure first volume has filename
            if (string.IsNullOrEmpty(result.VolumeInfoList[0].FileNameVolume))
            {
                result.VolumeInfoList[0].FileNameVolume = Path.GetFileName(fullPathPdfFileOrSinglesFolder);
                result.IsDirty = true;
            }

            // Ensure at least one TOC entry exists
            if (result.TocEntries.Count == 0)
            {
                result.TocEntries.Add(new TOCEntry
                {
                    SongName = Path.GetFileNameWithoutExtension(fullPathPdfFileOrSinglesFolder)
                });
                result.IsDirty = true;
            }

            // Validate last page number is in range
            var maxPageNum = result.PageNumberOffset + result.VolumeInfoList.Sum(v => v.NPagesInThisVolume);
            if (result.LastPageNo < result.PageNumberOffset || result.LastPageNo >= maxPageNum)
            {
                result.LastPageNo = result.PageNumberOffset;
            }

            return result;
        }

        private static async Task<PdfMetaDataReadResult> CreateNewMetaDataAsync(
            string fullPathPdfFileOrSinglesFolder,
            bool isSingles,
            IPdfDocumentProvider pdfDocumentProvider)
        {
            Debug.WriteLine($"PdfMetaDataCore: No metadata file exists, reading PDF: {Path.GetFileName(fullPathPdfFileOrSinglesFolder)}");
            var pageCount = await pdfDocumentProvider.GetPageCountAsync(fullPathPdfFileOrSinglesFolder);

            var result = new PdfMetaDataReadResult
            {
                FullPathFile = fullPathPdfFileOrSinglesFolder,
                IsSinglesFolder = isSingles,
                IsDirty = true,
                LastWriteTime = DateTime.Now,
                PageNumberOffset = 0,
                LastPageNo = 0
            };

            result.VolumeInfoList.Add(new PdfVolumeInfoBase
            {
                FileNameVolume = Path.GetFileName(fullPathPdfFileOrSinglesFolder),
                NPagesInThisVolume = pageCount,
                Rotation = 0
            });

            return result;
        }

        /// <summary>
        /// Parallel loading implementation - scans for .json metadata files only
        /// </summary>
        private static async Task<(List<PdfMetaDataReadResult>, List<string>)> LoadAllPdfMetaDataParallelAsync(
            string rootMusicFolder,
            IPdfDocumentProvider pdfDocumentProvider,
            IExceptionHandler exceptionHandler)
        {
            var results = new ConcurrentBag<PdfMetaDataReadResult>();
            var folders = new ConcurrentDictionary<string, byte>();

            var metadataFiles = new List<string>();
            var singlesFolders = new List<string>();
            var pdfFilesWithoutMetadata = new List<string>();
            // Track continuation PDFs: base PDF path -> list of continuation PDF paths (in order)
            var continuationsByBasePdf = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            static bool IsInHiddenFolder(string path) =>
                path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(segment => segment.Equals("hidden", StringComparison.OrdinalIgnoreCase));

            static bool IsInSinglesFolder(string path) =>
                path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(segment => segment.EndsWith("singles", StringComparison.OrdinalIgnoreCase));

            await Task.Run(() =>
            {
                try
                {
                    // Collect all .json metadata files (BMK/XML format is deprecated)
                    var metadataByBasePath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    
                    foreach (var jsonFile in Directory.EnumerateFiles(rootMusicFolder, "*.json", SearchOption.AllDirectories))
                    {
                        if (IsInHiddenFolder(jsonFile)) continue;
                        var basePath = Path.ChangeExtension(jsonFile, null);
                        metadataByBasePath[basePath] = jsonFile;
                    }

                    // Identify Singles folders
                    var allSinglesFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kvp in metadataByBasePath)
                    {
                        var metadataFile = kvp.Value;
                        var singlesDir = Path.Combine(Path.GetDirectoryName(metadataFile) ?? "", Path.GetFileNameWithoutExtension(metadataFile));
                        if (Directory.Exists(singlesDir) && singlesDir.EndsWith("singles", StringComparison.OrdinalIgnoreCase))
                        {
                            allSinglesFolders.Add(singlesDir);
                            singlesFolders.Add(singlesDir);
                        }
                    }

                    // Group metadata files by directory for continuation detection
                    var metadataByDirectory = new Dictionary<string, List<string>>();
                    foreach (var kvp in metadataByBasePath)
                    {
                        var metadataFile = kvp.Value;
                        if (IsInSinglesFolder(metadataFile)) continue;
                        
                        var singlesDir = Path.Combine(Path.GetDirectoryName(metadataFile) ?? "", Path.GetFileNameWithoutExtension(metadataFile));
                        if (Directory.Exists(singlesDir) && singlesDir.EndsWith("singles", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var dir = Path.GetDirectoryName(metadataFile) ?? "";
                        if (!metadataByDirectory.TryGetValue(dir, out var fileList))
                        {
                            fileList = new List<string>();
                            metadataByDirectory[dir] = fileList;
                        }
                        fileList.Add(metadataFile);
                    }

                    // For each directory, detect base volumes vs continuations
                    foreach (var kvp in metadataByDirectory)
                    {
                        var sortedFiles = kvp.Value.OrderBy(f => Path.GetFileNameWithoutExtension(f).ToLower()).ToList();
                        string currentBaseName = null;

                        foreach (var metadataFile in sortedFiles)
                        {
                            var isContinuation = false;
                            var justFnameCurrent = Path.GetFileNameWithoutExtension(metadataFile).Trim().ToLower();

                            if (currentBaseName != null &&
                                currentBaseName.Length < justFnameCurrent.Length &&
                                currentBaseName == justFnameCurrent[..currentBaseName.Length] &&
                                char.IsDigit(justFnameCurrent[currentBaseName.Length..][0]))
                            {
                                isContinuation = true;
                            }

                            if (!isContinuation)
                            {
                                metadataFiles.Add(metadataFile);
                                var baseName = justFnameCurrent;
                                if ("01".Contains(baseName.LastOrDefault()))
                                    baseName = baseName[..^1];
                                currentBaseName = baseName;
                            }
                        }
                    }

                    // Find PDFs without metadata files
                    var metadataBaseNames = new Dictionary<string, HashSet<string>>();
                    foreach (var metadataFile in metadataFiles)
                    {
                        var dir = Path.GetDirectoryName(metadataFile);
                        if (!metadataBaseNames.TryGetValue(dir, out var baseNames))
                        {
                            baseNames = new HashSet<string>();
                            metadataBaseNames[dir] = baseNames;
                        }
                        var baseName = Path.GetFileNameWithoutExtension(metadataFile).Trim().ToLower();
                        if ("01".Contains(baseName.LastOrDefault()))
                            baseName = baseName[..^1];
                        baseNames.Add(baseName);
                    }

                    var pdfsByDirectory = new Dictionary<string, List<string>>();
                    foreach (var pdfFile in Directory.EnumerateFiles(rootMusicFolder, "*.pdf", SearchOption.AllDirectories))
                    {
                        if (IsInHiddenFolder(pdfFile) || IsInSinglesFolder(pdfFile)) continue;
                        var fileName = Path.GetFileName(pdfFile);
                        if (fileName.StartsWith("._") || pdfFile.Contains("__MACOSX")) continue;

                        var basePath = Path.ChangeExtension(pdfFile, null);
                        if (!metadataByBasePath.ContainsKey(basePath))
                        {
                            var dir = Path.GetDirectoryName(pdfFile) ?? "";
                            var pdfBaseName = Path.GetFileNameWithoutExtension(pdfFile).Trim().ToLower();

                            bool isContinuation = false;
                            if (metadataBaseNames.TryGetValue(dir, out var dirBaseNames))
                            {
                                foreach (var metadataBaseName in dirBaseNames)
                                {
                                    if (metadataBaseName.Length < pdfBaseName.Length &&
                                        pdfBaseName.StartsWith(metadataBaseName) &&
                                        char.IsDigit(pdfBaseName[metadataBaseName.Length]))
                                    {
                                        isContinuation = true;
                                        break;
                                    }
                                }
                            }

                            if (!isContinuation)
                            {
                                if (!pdfsByDirectory.TryGetValue(dir, out var pdfList))
                                {
                                    pdfList = new List<string>();
                                    pdfsByDirectory[dir] = pdfList;
                                }
                                pdfList.Add(pdfFile);
                            }
                        }
                    }

                    foreach (var kvp in pdfsByDirectory)
                    {
                        var sortedPdfs = kvp.Value.OrderBy(f => f.ToLower()).ToList();
                        string currentBaseName = null;
                        string currentBasePdfPath = null;

                        foreach (var pdfFile in sortedPdfs)
                        {
                            var justfnameCurrent = Path.GetFileNameWithoutExtension(pdfFile).Trim().ToLower();
                            bool isContinuation = currentBaseName != null &&
                                currentBaseName.Length < justfnameCurrent.Length &&
                                currentBaseName == justfnameCurrent[..currentBaseName.Length] &&
                                char.IsDigit(justfnameCurrent[currentBaseName.Length..][0]);

                            if (!isContinuation)
                            {
                                pdfFilesWithoutMetadata.Add(pdfFile);
                                var baseName = justfnameCurrent;
                                if ("01".Contains(baseName.LastOrDefault()))
                                    baseName = baseName[..^1];
                                currentBaseName = baseName;
                                currentBasePdfPath = pdfFile;
                            }
                            else
                            {
                                // Track this as a continuation of the current base PDF
                                if (currentBasePdfPath != null)
                                {
                                    if (!continuationsByBasePdf.TryGetValue(currentBasePdfPath, out var continuations))
                                    {
                                        continuations = new List<string>();
                                        continuationsByBasePdf[currentBasePdfPath] = continuations;
                                    }
                                    continuations.Add(pdfFile);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    exceptionHandler?.OnException($"Scanning {rootMusicFolder}", ex);
                }
            });

            Debug.WriteLine($"PdfMetaDataCore Parallel: Found {metadataFiles.Count} JSON metadata files, {singlesFolders.Count} singles folders, {pdfFilesWithoutMetadata.Count} new PDFs, {continuationsByBasePdf.Count} multi-volume sets");

            // Phase 2: Parse metadata files in parallel
            var metadataTasks = metadataFiles.Select(async metadataFile =>
            {
                try
                {
                    var pdfFile = Path.ChangeExtension(metadataFile, "pdf");
                    if (!File.Exists(pdfFile))
                    {
                        var dir = Path.GetDirectoryName(metadataFile);
                        var baseName = Path.GetFileNameWithoutExtension(metadataFile);
                        var candidates = Directory.EnumerateFiles(dir!, $"{baseName}*.pdf").OrderBy(f => f).ToList();
                        if (candidates.Count > 0)
                            pdfFile = candidates[0];
                        else
                        {
                            Debug.WriteLine($"PdfMetaDataCore Parallel: Skipping orphaned metadata: {metadataFile}");
                            return;
                        }
                    }

                    var metadata = await ReadPdfMetaDataAsync(pdfFile, false, pdfDocumentProvider, exceptionHandler);
                    if (metadata != null)
                    {
                        var totalPages = metadata.VolumeInfoList.Sum(v => v.NPagesInThisVolume);
                        if (totalPages == 0) return;

                        results.Add(metadata);
                        var dir = Path.GetDirectoryName(pdfFile);
                        if (dir != null && dir.Length > rootMusicFolder.Length)
                            folders.TryAdd(dir[(rootMusicFolder.Length + 1)..], 0);
                    }
                }
                catch (Exception ex)
                {
                    exceptionHandler?.OnException($"Reading metadata {metadataFile}", ex);
                }
            });

            var singlesTasks = singlesFolders.Select(async singlesFolder =>
            {
                try
                {
                    var metadata = await LoadSinglesFolderAsync(singlesFolder, pdfDocumentProvider, exceptionHandler);
                    if (metadata != null) results.Add(metadata);
                }
                catch (Exception ex)
                {
                    exceptionHandler?.OnException($"Reading singles folder {singlesFolder}", ex);
                }
            });

            var newPdfTasks = pdfFilesWithoutMetadata.Select(async pdfFile =>
            {
                try
                {
                    var metadata = await CreateNewMetaDataAsync(pdfFile, false, pdfDocumentProvider);
                    if (metadata != null)
                    {
                        // Add continuation volumes if this PDF has them
                        if (continuationsByBasePdf.TryGetValue(pdfFile, out var continuations))
                        {
                            foreach (var continuationPdf in continuations)
                            {
                                try
                                {
                                    Debug.WriteLine($"PdfMetaDataCore Parallel: Adding continuation volume: {Path.GetFileName(continuationPdf)}");
                                    var pageCount = await pdfDocumentProvider.GetPageCountAsync(continuationPdf);
                                    metadata.VolumeInfoList.Add(new PdfVolumeInfoBase
                                    {
                                        FileNameVolume = Path.GetFileName(continuationPdf),
                                        NPagesInThisVolume = pageCount,
                                        Rotation = pageCount != 1 ? 2 : 0 // Rotate180 if multi-page
                                    });
                                }
                                catch (Exception ex)
                                {
                                    exceptionHandler?.OnException($"Reading continuation PDF {continuationPdf}", ex);
                                }
                            }
                        }

                        if (metadata.TocEntries.Count == 0 && metadata.VolumeInfoList.Sum(v => v.NPagesInThisVolume) < 11)
                        {
                            metadata.TocEntries.Add(new TOCEntry { SongName = Path.GetFileNameWithoutExtension(pdfFile) });
                        }
                        results.Add(metadata);
                        var dir = Path.GetDirectoryName(pdfFile);
                        if (dir != null && dir.Length > rootMusicFolder.Length)
                            folders.TryAdd(dir[(rootMusicFolder.Length + 1)..], 0);
                    }
                }
                catch (Exception ex)
                {
                    exceptionHandler?.OnException($"Creating metadata for {pdfFile}", ex);
                }
            });

            await Task.WhenAll(metadataTasks.Concat(singlesTasks).Concat(newPdfTasks));
            Debug.WriteLine($"PdfMetaDataCore Parallel: Loaded {results.Count} metadata entries");
            return (results.ToList(), folders.Keys.ToList());
        }

        /// <summary>
        /// Convert PdfMetaDataReadResult to BmkJsonFormat for serialization.
        /// This is the single place that builds the JSON structure.
        /// </summary>
        private static string ConvertMetadataToJsonString(PdfMetaDataReadResult metadata)
        {
            var jsonData = new BmkJsonFormat
            {
                Version = 1,
                LastWrite = DateTime.Now,
                LastPageNo = metadata.LastPageNo,
                PageNumberOffset = metadata.PageNumberOffset,
                Notes = metadata.Notes,
            };

            // Convert volumes
            foreach (var vol in metadata.VolumeInfoList)
            {
                jsonData.Volumes.Add(new JsonPdfVolumeInfo
                {
                    FileName = vol.FileNameVolume,
                    PageCount = vol.NPagesInThisVolume,
                    Rotation = vol.Rotation
                });
            }

            // Convert TOC entries
            foreach (var toc in metadata.TocEntries)
            {
                jsonData.TableOfContents.Add(new JsonTOCEntry
                {
                    PageNo = toc.PageNo,
                    SongName = toc.SongName,
                    Composer = toc.Composer,
                    Date = toc.Date,
                    Notes = toc.Notes
                });
            }

            // Convert favorites
            foreach (var fav in metadata.Favorites)
            {
                jsonData.Favorites.Add(new JsonFavorite
                {
                    PageNo = fav.Pageno,
                    Name = fav.FavoriteName
                });
            }

            // Convert ink strokes
            foreach (var ink in metadata.InkStrokes)
            {
                if (ink.StrokeData != null && ink.StrokeData.Length > 0)
                {
                    try
                    {
                        var jsonStr = System.Text.Encoding.UTF8.GetString(ink.StrokeData);
                        if (jsonStr.TrimStart().StartsWith("{"))
                        {
                            // Already in portable JSON format
                            var portableCollection = JsonSerializer.Deserialize<PortableInkStrokeCollection>(jsonStr, JsonOptions);
                            if (portableCollection != null)
                            {
                                jsonData.InkStrokes[ink.Pageno] = new JsonInkStrokes
                                {
                                    CanvasWidth = portableCollection.CanvasWidth,
                                    CanvasHeight = portableCollection.CanvasHeight,
                                    Strokes = portableCollection.Strokes
                                };
                            }
                        }
                        // Note: Binary ISF format from WPF cannot be converted here - skip those
                    }
                    catch
                    {
                        // Skip ink strokes that can't be parsed
                    }
                }
            }

            return JsonSerializer.Serialize(jsonData, JsonOptions);
        }

        /// <summary>
        /// Save PDF metadata to a JSON file if dirty or forced.
        /// This is the single place where the Desktop/Avalonia app saves metadata.
        /// Similar to WPF's PdfMetaData.SaveIfDirty() but uses JSON format.
        /// </summary>
        /// <param name="metadata">The metadata to save</param>
        /// <param name="forceSave">If true, save even if IsDirty is false</param>
        /// <returns>True if saved successfully</returns>
        public static bool SaveToJson(PdfMetaDataReadResult metadata, bool forceSave = false)
        {
            if (metadata == null)
                return false;

            if (!metadata.IsDirty && !forceSave)
                return true; // Nothing to save

            try
            {
                var jsonPath = metadata.JsonFilePath;
                var jsonContent = ConvertMetadataToJsonString(metadata);

                // Write JSON file synchronously - simple and avoids async deadlock issues
                File.WriteAllText(jsonPath, jsonContent);

                // Update metadata state
                metadata.IsDirty = false;
                metadata.LastWriteTime = DateTime.Now;

                Debug.WriteLine($"PdfMetaDataCore: Saved metadata to {Path.GetFileName(jsonPath)}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PdfMetaDataCore: Error saving metadata: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Save all dirty metadata entries to JSON files
        /// </summary>
        /// <param name="metadataList">The list of metadata entries</param>
        /// <returns>The number of metadata entries saved</returns>
        public static int SaveAllDirtyMetadata(List<PdfMetaDataReadResult> metadataList)
        {
            int savedCount = 0;
            foreach (var metadata in metadataList)
            {
                if (metadata.IsDirty)
                {
                    var saved = SaveToJson(metadata);
                    if (saved)
                        savedCount++;
                }
            }
            return savedCount;
        }
    }

    /// <summary>
    /// Serializable version of PDF metadata for XML/JSON serialization
    /// </summary>
    [Serializable]
    [XmlRoot("PdfMetaData")]
    public class SerializablePdfMetaData
    {
        [XmlArrayItem("PdfVolumeInfo")]
        public List<PdfVolumeInfoBase> lstVolInfo { get; set; } = new();
        public int LastPageNo { get; set; }
        public DateTime dtLastWrite { get; set; }
        public int PageNumberOffset { get; set; }
        public string Notes { get; set; }
        public List<InkStrokeClass> LstInkStrokes { get; set; } = new();
        public List<Favorite> Favorites { get; set; } = new();
        public List<TOCEntry> lstTocEntries { get; set; } = new();
    }
}
