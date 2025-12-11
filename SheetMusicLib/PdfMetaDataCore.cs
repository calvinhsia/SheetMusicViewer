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
        /// <returns>List of metadata results and list of folder names</returns>
        public static async Task<(List<PdfMetaDataReadResult>, List<string>)> LoadAllPdfMetaDataFromDiskAsync(
            string rootMusicFolder,
            IPdfDocumentProvider pdfDocumentProvider,
            IExceptionHandler exceptionHandler = null,
            bool useParallelLoading = true)
        {
            var lstPdfMetaFileData = new List<PdfMetaDataReadResult>();
            var lstFolders = new List<string>();

            if (string.IsNullOrEmpty(rootMusicFolder) || !Directory.Exists(rootMusicFolder))
            {
                return (lstPdfMetaFileData, lstFolders);
            }

            if (useParallelLoading)
            {
                return await LoadAllPdfMetaDataParallelAsync(rootMusicFolder, pdfDocumentProvider, exceptionHandler);
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
                                    var justfnameCurrent = Path.GetFileNameWithoutExtension(file).Trim().ToLower();
                                    if (justFnameVol0.Length < justfnameCurrent.Length &&
                                        justFnameVol0 == justfnameCurrent[..justFnameVol0.Length])
                                    {
                                        if (char.IsDigit(justfnameCurrent[justFnameVol0.Length..][0]))
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
            var bmkFile = Path.ChangeExtension(curPath, "bmk");
            var sortedListSingles = new SortedSet<string>();
            var sortedListDeletedSingles = new SortedSet<string>();

            if (File.Exists(bmkFile))
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
                Debug.WriteLine($"PdfMetaDataCore: No BMK file exists for Singles folder: {Path.GetFileName(curPath)}");
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
        /// Read PDF metadata from a BMK/JSON file asynchronously using a page count provider for PDF files
        /// </summary>
        /// <param name="fullPathPdfFileOrSinglesFolder">Full path to PDF file or singles folder</param>
        /// <param name="isSingles">Whether this is a singles folder</param>
        /// <param name="pdfDocumentProvider">Provider to get PDF page count</param>
        /// <param name="exceptionHandler">Optional exception handler</param>
        /// <returns>Metadata read result or null if failed</returns>
        public static async Task<PdfMetaDataReadResult> ReadPdfMetaDataAsync(
            string fullPathPdfFileOrSinglesFolder,
            bool isSingles,
            IPdfDocumentProvider pdfDocumentProvider,
            IExceptionHandler exceptionHandler = null)
        {
            PdfMetaDataReadResult result = null;

            // Prefer JSON over BMK (XML)
            var jsonFile = Path.ChangeExtension(fullPathPdfFileOrSinglesFolder, "json");
            var bmkFile = Path.ChangeExtension(fullPathPdfFileOrSinglesFolder, "bmk");

            var metadataFile = File.Exists(jsonFile) ? jsonFile : (File.Exists(bmkFile) ? bmkFile : null);

            if (metadataFile != null)
            {
                try
                {
                    result = await ReadFromExistingBmkFileAsync(
                        fullPathPdfFileOrSinglesFolder,
                        metadataFile,
                        isSingles,
                        pdfDocumentProvider);
                }
                catch (Exception ex)
                {
                    exceptionHandler?.OnException($"Reading {metadataFile}", ex);
                    // Don't delete the file - user might have valuable bookmarks/favorites
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

        private static async Task<PdfMetaDataReadResult> ReadFromExistingBmkFileAsync(
            string fullPathPdfFileOrSinglesFolder,
            string metadataFile,
            bool isSingles,
            IPdfDocumentProvider pdfDocumentProvider)
        {
            SerializablePdfMetaData serializedData = null;

            var fileContent = await File.ReadAllTextAsync(metadataFile);

            // Detect format by file extension or content
            if (metadataFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                fileContent.TrimStart().StartsWith("{"))
            {
                // JSON format
                serializedData = JsonSerializer.Deserialize<SerializablePdfMetaData>(fileContent, JsonOptions);
            }
            else
            {
                // XML format (legacy .bmk)
                var serializer = new XmlSerializer(typeof(SerializablePdfMetaData));
                using var sr = new StringReader(fileContent);
                serializedData = (SerializablePdfMetaData)serializer.Deserialize(sr);
            }

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
                result.TocEntries.Add(new TOCEntry()
                {
                    SongName = Path.GetFileNameWithoutExtension(result.FullPathFile)
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
            Debug.WriteLine($"PdfMetaDataCore: No BMK file exists, reading PDF: {Path.GetFileName(fullPathPdfFileOrSinglesFolder)}");
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
        /// Parallel loading implementation - Phase 1: scan and group files, Phase 2: parse BMK files in parallel
        /// </summary>
        private static async Task<(List<PdfMetaDataReadResult>, List<string>)> LoadAllPdfMetaDataParallelAsync(
            string rootMusicFolder,
            IPdfDocumentProvider pdfDocumentProvider,
            IExceptionHandler exceptionHandler)
        {
            var results = new ConcurrentBag<PdfMetaDataReadResult>();
            var folders = new ConcurrentDictionary<string, byte>();

            // Phase 1: Quick scan to find all Singles folders
            var bmkFiles = new List<string>();
            var singlesFolders = new List<string>();
            var pdfFilesWithoutBmk = new List<string>();

            // Helper to check if a path contains a 'hidden' folder segment
            static bool IsInHiddenFolder(string path)
            {
                return path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(segment => segment.Equals("hidden", StringComparison.OrdinalIgnoreCase));
            }

            // Helper to check if a path is inside a Singles folder
            static bool IsInSinglesFolder(string path)
            {
                return path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(segment => segment.EndsWith("singles", StringComparison.OrdinalIgnoreCase));
            }

            await Task.Run(() =>
            {
                try
                {
                    // First pass: identify all Singles folders
                    var allSinglesFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var bmkFile in Directory.EnumerateFiles(rootMusicFolder, "*.bmk", SearchOption.AllDirectories))
                    {
                        if (IsInHiddenFolder(bmkFile))
                            continue;

                        var singlesDir = Path.Combine(Path.GetDirectoryName(bmkFile) ?? "", Path.GetFileNameWithoutExtension(bmkFile));
                        if (Directory.Exists(singlesDir) && singlesDir.EndsWith("singles", StringComparison.OrdinalIgnoreCase))
                        {
                            allSinglesFolders.Add(singlesDir);
                            singlesFolders.Add(singlesDir);
                        }
                    }

                    // Group BMK files by directory for continuation detection
                    var bmksByDirectory = new Dictionary<string, List<string>>();
                    foreach (var bmkFile in Directory.EnumerateFiles(rootMusicFolder, "*.bmk", SearchOption.AllDirectories))
                    {
                        if (IsInHiddenFolder(bmkFile))
                            continue;

                        // Skip BMK files inside Singles folders
                        if (IsInSinglesFolder(bmkFile))
                            continue;

                        // Skip Singles folder BMK files (already handled above)
                        var singlesDir = Path.Combine(Path.GetDirectoryName(bmkFile) ?? "", Path.GetFileNameWithoutExtension(bmkFile));
                        if (Directory.Exists(singlesDir) && singlesDir.EndsWith("singles", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var dir = Path.GetDirectoryName(bmkFile) ?? "";
                        if (!bmksByDirectory.TryGetValue(dir, out var bmkList))
                        {
                            bmkList = new List<string>();
                            bmksByDirectory[dir] = bmkList;
                        }
                        bmkList.Add(bmkFile);
                    }

                    // For each directory, detect which BMK files are base volumes vs continuations
                    foreach (var kvp in bmksByDirectory)
                    {
                        var sortedBmks = kvp.Value.OrderBy(f => f.ToLower()).ToList();
                        string currentBaseName = null;

                        foreach (var bmkFile in sortedBmks)
                        {
                            var isContinuation = false;
                            var justFnameCurrent = Path.GetFileNameWithoutExtension(bmkFile).Trim().ToLower();

                            if (currentBaseName != null)
                            {
                                // Check if current file is a continuation of the base file
                                if (currentBaseName.Length < justFnameCurrent.Length &&
                                    currentBaseName == justFnameCurrent[..currentBaseName.Length])
                                {
                                    // Check if the extension starts with a digit
                                    if (char.IsDigit(justFnameCurrent[currentBaseName.Length..][0]))
                                    {
                                        isContinuation = true;
                                    }
                                }
                            }

                            if (!isContinuation)
                            {
                                // This is a new base file
                                bmkFiles.Add(bmkFile);

                                // Calculate the base name for continuation detection
                                var baseName = justFnameCurrent;
                                var lastChar = baseName.LastOrDefault();
                                if ("01".Contains(lastChar))
                                {
                                    baseName = baseName[..^1];
                                }
                                currentBaseName = baseName;
                            }
                            // If it's a continuation BMK, skip it - volume info is in the base BMK
                        }
                    }

                    // Find PDFs without BMK files (new PDFs) - group by directory for proper continuation detection
                    // Also need to check if a PDF is a continuation of a file that HAS a BMK
                    var pdfsByDirectory = new Dictionary<string, List<string>>();

                    // Build a set of all base names from BMK files for quick lookup
                    var bmkBaseNames = new Dictionary<string, HashSet<string>>(); // dir -> set of base names
                    foreach (var bmkFile in bmkFiles)
                    {
                        var dir = Path.GetDirectoryName(bmkFile);
                        if (!bmkBaseNames.TryGetValue(dir, out var baseNames))
                        {
                            baseNames = new HashSet<string>();
                            bmkBaseNames[dir] = baseNames;
                        }
                        var baseName = Path.GetFileNameWithoutExtension(bmkFile).Trim().ToLower();
                        var lastChar = baseName.LastOrDefault();
                        if ("01".Contains(lastChar))
                        {
                            baseName = baseName[..^1];
                        }
                        baseNames.Add(baseName);
                    }

                    foreach (var pdfFile in Directory.EnumerateFiles(rootMusicFolder, "*.pdf", SearchOption.AllDirectories))
                    {
                        // Skip files in 'hidden' folders
                        if (IsInHiddenFolder(pdfFile))
                            continue;

                        // Skip files in 'Singles' folders - they're handled by LoadSinglesFolderAsync
                        if (IsInSinglesFolder(pdfFile))
                            continue;

                        var fileName = Path.GetFileName(pdfFile);
                        if (fileName.StartsWith("._") || pdfFile.Contains("__MACOSX"))
                            continue;

                        var bmkFile = Path.ChangeExtension(pdfFile, "bmk");
                        if (!File.Exists(bmkFile))
                        {
                            // Check if this PDF is a continuation of a file that HAS a BMK
                            var dir = Path.GetDirectoryName(pdfFile) ?? "";
                            var pdfBaseName = Path.GetFileNameWithoutExtension(pdfFile).Trim().ToLower();

                            // Check if this matches any BMK base name (meaning it's a continuation)
                            bool isContinuationOfBmk = false;
                            if (bmkBaseNames.TryGetValue(dir, out var dirBmkBaseNames))
                            {
                                foreach (var bmkBaseName in dirBmkBaseNames)
                                {
                                    if (bmkBaseName.Length < pdfBaseName.Length &&
                                        pdfBaseName.StartsWith(bmkBaseName) &&
                                        char.IsDigit(pdfBaseName[bmkBaseName.Length]))
                                    {
                                        isContinuationOfBmk = true;
                                        break;
                                    }
                                }
                            }

                            if (!isContinuationOfBmk)
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

                    // For each directory, detect which PDFs are base volumes vs continuations
                    foreach (var kvp in pdfsByDirectory)
                    {
                        var sortedPdfs = kvp.Value.OrderBy(f => f.ToLower()).ToList();
                        string currentBaseFile = null;
                        string currentBaseName = null;

                        foreach (var pdfFile in sortedPdfs)
                        {
                            var isContinuation = false;

                            if (currentBaseFile != null && currentBaseName != null)
                            {
                                var justfnameCurrent = Path.GetFileNameWithoutExtension(pdfFile).Trim().ToLower();

                                // Check if current file is a continuation of the base file
                                if (currentBaseName.Length < justfnameCurrent.Length &&
                                    currentBaseName == justfnameCurrent[..currentBaseName.Length])
                                {
                                    // Check if the extension starts with a digit
                                    if (char.IsDigit(justfnameCurrent[currentBaseName.Length..][0]))
                                    {
                                        isContinuation = true;
                                    }
                                }
                            }

                            if (!isContinuation)
                            {
                                // This is a new base file
                                pdfFilesWithoutBmk.Add(pdfFile);

                                // Calculate the base name for continuation detection
                                var justFnameVol0 = Path.GetFileNameWithoutExtension(pdfFile).Trim().ToLower();
                                var lastcharVol0 = justFnameVol0.LastOrDefault();
                                if ("01".Contains(lastcharVol0))
                                {
                                    justFnameVol0 = justFnameVol0[..^1];
                                }
                                currentBaseFile = pdfFile;
                                currentBaseName = justFnameVol0;
                            }
                            // If it's a continuation, we skip it - it will be handled when processing the base file
                        }
                    }
                }
                catch (Exception ex)
                {
                    exceptionHandler?.OnException($"Scanning {rootMusicFolder}", ex);
                }
            });

            Debug.WriteLine($"PdfMetaDataCore Parallel: Found {bmkFiles.Count} BMK files, {singlesFolders.Count} singles folders, {pdfFilesWithoutBmk.Count} new PDFs");

            // Phase 2: Parse BMK files in parallel
            var bmkTasks = bmkFiles.Select(async bmkFile =>
            {
                try
                {
                    var pdfFile = Path.ChangeExtension(bmkFile, "pdf");
                    // Try to find the actual PDF (might have different casing or be volume 0)
                    if (!File.Exists(pdfFile))
                    {
                        var dir = Path.GetDirectoryName(bmkFile);
                        var baseName = Path.GetFileNameWithoutExtension(bmkFile);
                        var candidates = Directory.EnumerateFiles(dir!, $"{baseName}*.pdf").OrderBy(f => f).ToList();
                        if (candidates.Count > 0)
                        {
                            pdfFile = candidates[0];
                        }
                        else
                        {
                            // No matching PDF found - this is an orphaned BMK file, skip it
                            // The sequential implementation only processes PDFs and their BMKs,
                            // so orphaned BMKs without PDFs are never included
                            Debug.WriteLine($"PdfMetaDataCore Parallel: Skipping orphaned BMK (no PDF): {bmkFile}");
                            return;
                        }
                    }

                    var metadata = await ReadPdfMetaDataAsync(pdfFile, false, pdfDocumentProvider, exceptionHandler);
                    if (metadata != null)
                    {
                        // Skip entries with 0 total pages (cloud-only files that couldn't be read)
                        var totalPages = metadata.VolumeInfoList.Sum(v => v.NPagesInThisVolume);
                        if (totalPages == 0)
                        {
                            Debug.WriteLine($"PdfMetaDataCore Parallel: Skipping entry with 0 pages: {pdfFile}");
                            return;
                        }

                        results.Add(metadata);

                        var dir = Path.GetDirectoryName(pdfFile);
                        if (dir != null && dir.Length > rootMusicFolder.Length)
                        {
                            folders.TryAdd(dir[(rootMusicFolder.Length + 1)..], 0);
                        }
                    }
                }
                catch (Exception ex)
                {
                    exceptionHandler?.OnException($"Reading BMK {bmkFile}", ex);
                }
            });

            // Parse singles folders in parallel
            var singlesTasks = singlesFolders.Select(async singlesFolder =>
            {
                try
                {
                    var metadata = await LoadSinglesFolderAsync(singlesFolder, pdfDocumentProvider, exceptionHandler);
                    if (metadata != null)
                    {
                        results.Add(metadata);
                    }
                }
                catch (Exception ex)
                {
                    exceptionHandler?.OnException($"Reading singles folder {singlesFolder}", ex);
                }
            });

            // Process new PDFs without BMK files in parallel
            var newPdfTasks = pdfFilesWithoutBmk.Select(async pdfFile =>
            {
                try
                {
                    var metadata = await CreateNewMetaDataAsync(pdfFile, false, pdfDocumentProvider);
                    if (metadata != null)
                    {
                        // Add default TOC entry for small PDFs
                        if (metadata.TocEntries.Count == 0 &&
                            metadata.VolumeInfoList.Sum(v => v.NPagesInThisVolume) < 11)
                        {
                            metadata.TocEntries.Add(new TOCEntry
                            {
                                SongName = Path.GetFileNameWithoutExtension(pdfFile)
                            });
                        }
                        results.Add(metadata);

                        var dir = Path.GetDirectoryName(pdfFile);
                        if (dir != null && dir.Length > rootMusicFolder.Length)
                        {
                            folders.TryAdd(dir[(rootMusicFolder.Length + 1)..], 0);
                        }
                    }
                }
                catch (Exception ex)
                {
                    exceptionHandler?.OnException($"Creating metadata for {pdfFile}", ex);
                }
            });

            // Wait for all parallel tasks
            await Task.WhenAll(bmkTasks.Concat(singlesTasks).Concat(newPdfTasks));

            Debug.WriteLine($"PdfMetaDataCore Parallel: Loaded {results.Count} metadata entries");

            return (results.ToList(), folders.Keys.ToList());
        }
    }

    /// <summary>
    /// Serializable version of PDF metadata for XML/JSON serialization
    /// This matches the existing BMK file format which uses root element "PdfMetaData"
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
