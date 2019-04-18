using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace WpfPdfViewer
{
    internal class PageCacheEntry
    {
        internal CancellationTokenSource cts = new CancellationTokenSource();
        public int pageNo;
        public Task<BitmapImage> task;
        public int age; // just a growing int
        public override string ToString()
        {
            return $"{pageNo} age={age} IsComp={task.IsCompleted}";
        }
    }

    internal class PageCache
    {
        int currentCacheAge;
        readonly Dictionary<int, PageCacheEntry> dictCache = new Dictionary<int, PageCacheEntry>(); // for either show2pages, pageno ->grid. results in dupes if even, then odd number on show2pages


        private PdfViewerWindow pdfViewerWindow;

        public PageCache(PdfViewerWindow pdfViewerWindow)
        {
            this.pdfViewerWindow = pdfViewerWindow;
        }

        /// <summary>
        /// add an item to cache. If already there, return it, else create new one
        /// </summary>
        /// <param name="PageNo"></param>
        /// <returns></returns>
        public PageCacheEntry TryAddCacheEntry(int PageNo)
        {
            PageCacheEntry cacheEntry = null;
            if (PageNo >= pdfViewerWindow.currentPdfMetaData.PageNumberOffset && PageNo < pdfViewerWindow.MaxPageNumber)
            {
                if (!dictCache.TryGetValue(PageNo, out cacheEntry) ||
                    cacheEntry.cts.IsCancellationRequested ||
                    cacheEntry.task.IsCanceled)
                {
                    cacheEntry = new PageCacheEntry()
                    {
                        pageNo = PageNo,
                        age = currentCacheAge++
                    };
                    cacheEntry.task = CalculateBitMapImageForPageAsync(cacheEntry);

                    int cacheSize = 50;
                    if (dictCache.Count > cacheSize)
                    {
                        var lst = dictCache.Values.OrderBy(s => s.age).Take(dictCache.Count - cacheSize);
                        foreach (var entry in lst)
                        {
                            dictCache.Remove(entry.pageNo);
                        }
                    }
                    dictCache[PageNo] = cacheEntry;
                }
            }
            return cacheEntry;
        }
        internal async Task<BitmapImage> CalculateBitMapImageForPageAsync(PageCacheEntry cacheEntry)
        {
            //if (cacheEntry.pageNo == currentPdfMetaData.PageNumberOffset && currentPdfMetaData.bitmapImageCache != null)
            //{
            //    return currentPdfMetaData.bitmapImageCache;
            //}
            var bmi = new BitmapImage();
            cacheEntry.cts.Token.ThrowIfCancellationRequested();
            var (pdfDoc, pdfPgno) = await pdfViewerWindow.currentPdfMetaData.GetPdfDocumentForPageno(cacheEntry.pageNo);
            if (pdfDoc != null && pdfPgno >= 0 && pdfPgno < pdfDoc.PageCount)
            {
                using (var pdfPage = pdfDoc.GetPage((uint)(pdfPgno)))
                {
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
                        var strmLength = strm.Size;
                        cacheEntry.cts.Token.ThrowIfCancellationRequested();
                        bmi.BeginInit();
                        bmi.StreamSource = strm.AsStream();
                        bmi.Rotation = (Rotation)pdfViewerWindow.currentPdfMetaData.GetRotation(cacheEntry.pageNo);
                        bmi.CacheOption = BitmapCacheOption.OnLoad;
                        bmi.EndInit();
                    }
                }
            }
            return bmi;
        }
        public void ClearCache()
        {
            currentCacheAge = 0;
            dictCache.Clear();
        }


        internal void PurgeIfNecessary(int pageNo)
        {
            var numPendingTasks = dictCache.Values.Where(v => !v.task.IsCompleted).Count();
            // The user could have held down rt-arrow, advancing thru doc faster than we can render
            // so we check for any prior tasks that have not completed and cancel them.
            var lstToDelete = new List<int>();
            foreach (var entry in dictCache.Values.Where(v => !v.task.IsCompleted))
            {
                if (entry.pageNo != pageNo && currentCacheAge - entry.age > 5) // don't del the task getting the current pgno
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
        }
    }
}
