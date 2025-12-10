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
        /// Load all PDF metadata from a folder recursively
        /// </summary>
        /// <param name="rootMusicFolder">Root folder to scan</param>
        /// <param name="pdfDocumentProvider">Provider to get PDF page count</param>
        /// <param name="exceptionHandler">Optional exception handler</param>
        /// <returns>List of metadata results and list of folder names</returns>
        public static async Task<(List<PdfMetaDataReadResult>, List<string>)> LoadAllPdfMetaDataFromDiskAsync(
            string rootMusicFolder,
            IPdfDocumentProvider pdfDocumentProvider,
            IExceptionHandler exceptionHandler = null)
        {
            var lstPdfMetaFileData = new List<PdfMetaDataReadResult>();
            var lstFolders = new List<string>();

            if (string.IsNullOrEmpty(rootMusicFolder) || !Directory.Exists(rootMusicFolder))
            {
                return (lstPdfMetaFileData, lstFolders);
            }

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
        /// Read PDF metadata from a BMK file asynchronously using a page count provider for PDF files
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
            var bmkFile = Path.ChangeExtension(fullPathPdfFileOrSinglesFolder, "bmk");

            if (File.Exists(bmkFile))
            {
                try
                {
                    result = await ReadFromExistingBmkFileAsync(
                        fullPathPdfFileOrSinglesFolder,
                        bmkFile,
                        isSingles,
                        pdfDocumentProvider);
                }
                catch (Exception ex)
                {
                    exceptionHandler?.OnException($"Reading {bmkFile}", ex);
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
            string bmkFile,
            bool isSingles,
            IPdfDocumentProvider pdfDocumentProvider)
        {
            var serializer = new XmlSerializer(typeof(SerializablePdfMetaData));
            using var sr = new StreamReader(bmkFile);
            var serializedData = (SerializablePdfMetaData)serializer.Deserialize(sr);

            var result = new PdfMetaDataReadResult
            {
                FullPathFile = fullPathPdfFileOrSinglesFolder,
                IsSinglesFolder = isSingles,
                IsDirty = false,
                PageNumberOffset = serializedData.PageNumberOffset,
                LastPageNo = serializedData.LastPageNo,
                LastWriteTime = serializedData.dtLastWrite.Year < 1900
                    ? new FileInfo(bmkFile).LastWriteTime
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
    }

    /// <summary>
    /// Serializable version of PDF metadata for XML serialization
    /// This matches the existing BMK file format which uses root element "PdfMetaData"
    /// </summary>
    [Serializable]
    [XmlRoot("PdfMetaData")]
    public class SerializablePdfMetaData
    {
        public List<PdfVolumeInfoBase> lstVolInfo = new();
        public int LastPageNo;
        public DateTime dtLastWrite;
        public int PageNumberOffset;
        public string Notes;
        public List<InkStrokeClass> LstInkStrokes = new();
        public List<Favorite> Favorites = new();
        public List<TOCEntry> lstTocEntries = new();
    }
}
