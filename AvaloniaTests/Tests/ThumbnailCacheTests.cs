using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SheetMusicLib;

namespace AvaloniaTests.Tests;

/// <summary>Unit tests for the thumbnail cache generation guard.</summary>
[TestClass]
[TestCategory("Unit")]
public class ThumbnailCacheTests
{
    [TestMethod]
    public async Task GetOrCreateThumbnailAsync_DiscardsResultStartedBeforeCacheCleared()
    {
        var metadata = new PdfMetaDataReadResult { FullPathFile = "test.pdf" };
        var gate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var pending = metadata.GetOrCreateThumbnailAsync(() => gate.Task);

        metadata.ClearThumbnailCache();
        gate.SetResult("stale");

        var result = await pending;

        Assert.AreEqual("stale", result);
        Assert.IsNull(metadata.ThumbnailCache,
            "a thumbnail started before ClearThumbnailCache must not be cached");
    }

    [TestMethod]
    public async Task GetOrCreateThumbnailAsync_CachesResultStartedAfterClear()
    {
        var metadata = new PdfMetaDataReadResult { FullPathFile = "test.pdf" };
        metadata.ClearThumbnailCache();

        var result = await metadata.GetOrCreateThumbnailAsync(() => Task.FromResult("fresh"));

        Assert.AreEqual("fresh", result);
        Assert.AreEqual("fresh", metadata.ThumbnailCache);
    }

    [TestMethod]
    public async Task GetOrCreateThumbnailAsync_ReturnsCachedValueWithoutCallingFactory()
    {
        var metadata = new PdfMetaDataReadResult { FullPathFile = "test.pdf", ThumbnailCache = "cached" };

        var result = await metadata.GetOrCreateThumbnailAsync(
            () => Task.FromResult("should not be used"));

        Assert.AreEqual("cached", result);
        Assert.AreEqual("cached", metadata.ThumbnailCache);
    }
}
