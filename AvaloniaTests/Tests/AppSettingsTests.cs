using Microsoft.VisualStudio.TestTools.UnitTesting;
using SheetMusicLib;
using SheetMusicViewer.Desktop;
using System;
using System.IO;
using System.Linq;

namespace AvaloniaTests.Tests;

/// <summary>
/// Unit tests for AppSettings - application settings persistence.
/// </summary>
[TestClass]
public class AppSettingsTests : TestBase
{
    private string _originalSettingsPath;
    private string _testSettingsPath;

    [TestInitialize]
    public override void TestInitialize()
    {
        base.TestInitialize();
        
        // Create a temp settings file path for testing
        _testSettingsPath = Path.Combine(Path.GetTempPath(), $"AppSettingsTest_{Guid.NewGuid():N}.json");
        
        // Store original path (if we need to restore it)
        _originalSettingsPath = AppSettings.SettingsPath;
    }

    [TestCleanup]
    public override void TestCleanup()
    {
        base.TestCleanup();
        
        // Clean up test settings file
        SafeDeleteFile(_testSettingsPath);
        
        // Reset the singleton for other tests
        AppSettings.ResetForTesting();
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AppSettings_DefaultValues_AreCorrect()
    {
        // Arrange - Create fresh settings with test path
        AppSettings.ResetForTesting(_testSettingsPath);
        
        // Act
        var settings = AppSettings.Instance;

        // Assert - verify default values (matching AppSettings.cs defaults)
        Assert.IsTrue(settings.Show2Pages, "Show2Pages should default to true");
        Assert.IsFalse(settings.IsFullScreen, "IsFullScreen should default to false");
        Assert.IsTrue(settings.WindowMaximized, "WindowMaximized should default to true");
        Assert.IsNotNull(settings.RootFolderMRU, "RootFolderMRU should not be null");
        Assert.AreEqual(0, settings.RootFolderMRU.Count, "RootFolderMRU should be empty by default");
        Assert.IsTrue(string.IsNullOrEmpty(settings.LastPDFOpen), "LastPDFOpen should be empty by default");
        
        LogMessage("Default values verified");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AppSettings_SaveAndLoad_PreservesValues()
    {
        // Arrange
        AppSettings.ResetForTesting(_testSettingsPath);
        var settings = AppSettings.Instance;
        
        settings.Show2Pages = false;
        settings.IsFullScreen = true;
        settings.WindowMaximized = true;
        settings.WindowWidth = 1920;
        settings.WindowHeight = 1080;
        settings.WindowLeft = 100;
        settings.WindowTop = 50;
        settings.LastPDFOpen = @"C:\Music\Test.pdf";
        settings.AddToMRU(@"C:\Music\Folder1");
        settings.AddToMRU(@"C:\Music\Folder2");

        // Act - Save and reload
        settings.Save();
        AppSettings.ResetForTesting(_testSettingsPath); // Force reload
        var reloaded = AppSettings.Instance;

        // Assert
        Assert.AreEqual(false, reloaded.Show2Pages, "Show2Pages should be preserved");
        Assert.AreEqual(true, reloaded.IsFullScreen, "IsFullScreen should be preserved");
        Assert.AreEqual(true, reloaded.WindowMaximized, "WindowMaximized should be preserved");
        Assert.AreEqual(1920, reloaded.WindowWidth, "WindowWidth should be preserved");
        Assert.AreEqual(1080, reloaded.WindowHeight, "WindowHeight should be preserved");
        Assert.AreEqual(100, reloaded.WindowLeft, "WindowLeft should be preserved");
        Assert.AreEqual(50, reloaded.WindowTop, "WindowTop should be preserved");
        Assert.AreEqual(@"C:\Music\Test.pdf", reloaded.LastPDFOpen, "LastPDFOpen should be preserved");
        Assert.AreEqual(2, reloaded.RootFolderMRU.Count, "MRU should have 2 entries");
        
        // Most recent should be first
        Assert.AreEqual(@"C:\Music\Folder2", reloaded.RootFolderMRU[0], "Most recent MRU should be first");
        Assert.AreEqual(@"C:\Music\Folder1", reloaded.RootFolderMRU[1], "Older MRU should be second");
        
        LogMessage("Save and load round-trip verified");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AppSettings_AddToMRU_MovesExistingToTop()
    {
        // Arrange
        AppSettings.ResetForTesting(_testSettingsPath);
        var settings = AppSettings.Instance;
        
        settings.AddToMRU(@"C:\Folder1");
        settings.AddToMRU(@"C:\Folder2");
        settings.AddToMRU(@"C:\Folder3");
        
        // Verify initial order
        Assert.AreEqual(@"C:\Folder3", settings.RootFolderMRU[0]);
        Assert.AreEqual(@"C:\Folder2", settings.RootFolderMRU[1]);
        Assert.AreEqual(@"C:\Folder1", settings.RootFolderMRU[2]);

        // Act - Add existing folder (should move to top)
        settings.AddToMRU(@"C:\Folder1");

        // Assert
        Assert.AreEqual(3, settings.RootFolderMRU.Count, "Should still have 3 entries");
        Assert.AreEqual(@"C:\Folder1", settings.RootFolderMRU[0], "Folder1 should now be first");
        Assert.AreEqual(@"C:\Folder3", settings.RootFolderMRU[1], "Folder3 should be second");
        Assert.AreEqual(@"C:\Folder2", settings.RootFolderMRU[2], "Folder2 should be third");
        
        LogMessage("MRU reordering verified");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AppSettings_AddToMRU_LimitsSize()
    {
        // Arrange
        AppSettings.ResetForTesting(_testSettingsPath);
        var settings = AppSettings.Instance;
        
        // Add more than the max MRU size (typically 10)
        for (int i = 0; i < 15; i++)
        {
            settings.AddToMRU($@"C:\Folder{i}");
        }

        // Assert - should be limited to max size
        Assert.IsTrue(settings.RootFolderMRU.Count <= 10, $"MRU should be limited to 10, got {settings.RootFolderMRU.Count}");
        
        // Most recent should be first
        Assert.AreEqual(@"C:\Folder14", settings.RootFolderMRU[0], "Most recent should be first");
        
        LogMessage($"MRU size limited to {settings.RootFolderMRU.Count}");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AppSettings_Singleton_ReturnsSameInstance()
    {
        // Arrange
        AppSettings.ResetForTesting(_testSettingsPath);

        // Act
        var instance1 = AppSettings.Instance;
        var instance2 = AppSettings.Instance;

        // Assert
        Assert.AreSame(instance1, instance2, "Instance should return same object");
        
        LogMessage("Singleton pattern verified");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AppSettings_CorruptedFile_LoadsDefaults()
    {
        // Arrange - Create a corrupted settings file
        File.WriteAllText(_testSettingsPath, "{ this is not valid json }}}");
        
        // Act
        AppSettings.ResetForTesting(_testSettingsPath);
        var settings = AppSettings.Instance;

        // Assert - should have default values
        Assert.IsTrue(settings.Show2Pages, "Should use default value for Show2Pages");
        Assert.AreEqual(0, settings.RootFolderMRU.Count, "Should have empty MRU");
        
        LogMessage("Corrupted file handling verified");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AppSettings_MissingFile_LoadsDefaults()
    {
        // Arrange - Ensure file doesn't exist
        if (File.Exists(_testSettingsPath))
        {
            File.Delete(_testSettingsPath);
        }

        // Act
        AppSettings.ResetForTesting(_testSettingsPath);
        var settings = AppSettings.Instance;

        // Assert - should have default values
        Assert.IsTrue(settings.Show2Pages, "Should use default value for Show2Pages");
        Assert.IsFalse(settings.IsFullScreen, "Should use default value for IsFullScreen");
        
        LogMessage("Missing file handling verified");
    }
}
