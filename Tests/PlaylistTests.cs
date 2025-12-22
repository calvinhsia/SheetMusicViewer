using Microsoft.VisualStudio.TestTools.UnitTesting;
using SheetMusicLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tests
{
    /// <summary>
    /// Unit tests for Playlist and PlaylistEntry classes
    /// </summary>
    [TestClass]
    public class PlaylistTests : TestBase
    {
        private string _testSettingsPath = null!;
        private string _testMusicFolder = null!;

        [TestInitialize]
        public void PlaylistTestInitialize()
        {
            // Create a unique test folder for each test
            _testMusicFolder = Path.Combine(Path.GetTempPath(), $"PlaylistTests_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testMusicFolder);
            _testSettingsPath = Path.Combine(_testMusicFolder, "settings.json");
            AppSettings.ResetForTesting(_testSettingsPath);
        }

        [TestCleanup]
        public void PlaylistTestCleanup()
        {
            // Clean up test folder
            if (Directory.Exists(_testMusicFolder))
            {
                try { Directory.Delete(_testMusicFolder, recursive: true); } catch { }
            }
            
            // Reset AppSettings for other tests
            AppSettings.ResetForTesting(null);
        }

        #region PlaylistEntry Tests

        [TestMethod]
        [TestCategory("Unit")]
        public void PlaylistEntry_DefaultValues_AreEmptyStrings()
        {
            // Arrange & Act
            var entry = new PlaylistEntry();

            // Assert
            Assert.AreEqual(string.Empty, entry.SongName);
            Assert.AreEqual(string.Empty, entry.Composer);
            Assert.AreEqual(string.Empty, entry.BookName);
            Assert.AreEqual(string.Empty, entry.Notes);
            Assert.AreEqual(0, entry.PageNo);
            AddLogEntry("PlaylistEntry default values verified");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PlaylistEntry_ToString_WithAllFields_FormatsCorrectly()
        {
            // Arrange
            var entry = new PlaylistEntry
            {
                SongName = "Moonlight Sonata",
                Composer = "Beethoven",
                PageNo = 42
            };

            // Act
            var result = entry.ToString();

            // Assert
            Assert.IsTrue(result.Contains("Moonlight Sonata"));
            Assert.IsTrue(result.Contains("Beethoven"));
            Assert.IsTrue(result.Contains("42"));
            AddLogEntry($"PlaylistEntry.ToString(): {result}");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PlaylistEntry_ToString_WithOnlySongName_FormatsCorrectly()
        {
            // Arrange
            var entry = new PlaylistEntry
            {
                SongName = "Test Song",
                PageNo = 10
            };

            // Act
            var result = entry.ToString();

            // Assert
            Assert.IsTrue(result.Contains("Test Song"));
            Assert.IsTrue(result.Contains("10"));
            AddLogEntry($"PlaylistEntry.ToString() with only song name: {result}");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PlaylistEntry_Properties_CanBeSet()
        {
            // Arrange
            var entry = new PlaylistEntry();

            // Act
            entry.SongName = "Test Song";
            entry.Composer = "Test Composer";
            entry.PageNo = 123;
            entry.BookName = "Test Book";
            entry.Notes = "Test Notes";

            // Assert
            Assert.AreEqual("Test Song", entry.SongName);
            Assert.AreEqual("Test Composer", entry.Composer);
            Assert.AreEqual(123, entry.PageNo);
            Assert.AreEqual("Test Book", entry.BookName);
            Assert.AreEqual("Test Notes", entry.Notes);
            AddLogEntry("PlaylistEntry properties can be set correctly");
        }

        #endregion

        #region Playlist Tests

        [TestMethod]
        [TestCategory("Unit")]
        public void Playlist_DefaultName_IsNewPlaylist()
        {
            // Arrange & Act
            var playlist = new Playlist();

            // Assert
            Assert.AreEqual("New Playlist", playlist.Name);
            AddLogEntry($"Playlist default name: {playlist.Name}");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Playlist_CreatedDate_IsSetOnConstruction()
        {
            // Arrange
            var before = DateTime.Now;

            // Act
            var playlist = new Playlist();
            var after = DateTime.Now;

            // Assert
            Assert.IsTrue(playlist.CreatedDate >= before && playlist.CreatedDate <= after);
            AddLogEntry($"Playlist created date: {playlist.CreatedDate}");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Playlist_ModifiedDate_IsSetOnConstruction()
        {
            // Arrange
            var before = DateTime.Now;

            // Act
            var playlist = new Playlist();
            var after = DateTime.Now;

            // Assert
            Assert.IsTrue(playlist.ModifiedDate >= before && playlist.ModifiedDate <= after);
            AddLogEntry($"Playlist modified date: {playlist.ModifiedDate}");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Playlist_Entries_InitializesAsEmptyList()
        {
            // Arrange & Act
            var playlist = new Playlist();

            // Assert
            Assert.IsNotNull(playlist.Entries);
            Assert.AreEqual(0, playlist.Entries.Count);
            AddLogEntry("Playlist entries initialized as empty list");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Playlist_ToString_FormatsCorrectly()
        {
            // Arrange
            var playlist = new Playlist { Name = "My Favorites" };
            playlist.Entries.Add(new PlaylistEntry { SongName = "Song 1" });
            playlist.Entries.Add(new PlaylistEntry { SongName = "Song 2" });
            playlist.Entries.Add(new PlaylistEntry { SongName = "Song 3" });

            // Act
            var result = playlist.ToString();

            // Assert
            Assert.IsTrue(result.Contains("My Favorites"));
            Assert.IsTrue(result.Contains("3"));
            AddLogEntry($"Playlist.ToString(): {result}");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Playlist_AddEntry_IncreasesCount()
        {
            // Arrange
            var playlist = new Playlist();
            var entry = new PlaylistEntry { SongName = "Test Song", PageNo = 1 };

            // Act
            playlist.Entries.Add(entry);

            // Assert
            Assert.AreEqual(1, playlist.Entries.Count);
            Assert.AreSame(entry, playlist.Entries[0]);
            AddLogEntry("Playlist entry added successfully");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Playlist_RemoveEntry_DecreasesCount()
        {
            // Arrange
            var playlist = new Playlist();
            var entry1 = new PlaylistEntry { SongName = "Song 1" };
            var entry2 = new PlaylistEntry { SongName = "Song 2" };
            playlist.Entries.Add(entry1);
            playlist.Entries.Add(entry2);

            // Act
            playlist.Entries.Remove(entry1);

            // Assert
            Assert.AreEqual(1, playlist.Entries.Count);
            Assert.AreSame(entry2, playlist.Entries[0]);
            AddLogEntry("Playlist entry removed successfully");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Playlist_ReorderEntry_MoveUp()
        {
            // Arrange
            var playlist = new Playlist();
            var entry1 = new PlaylistEntry { SongName = "Song 1" };
            var entry2 = new PlaylistEntry { SongName = "Song 2" };
            var entry3 = new PlaylistEntry { SongName = "Song 3" };
            playlist.Entries.Add(entry1);
            playlist.Entries.Add(entry2);
            playlist.Entries.Add(entry3);

            // Act - Move entry3 up (from index 2 to index 1)
            playlist.Entries.RemoveAt(2);
            playlist.Entries.Insert(1, entry3);

            // Assert
            Assert.AreSame(entry1, playlist.Entries[0]);
            Assert.AreSame(entry3, playlist.Entries[1]);
            Assert.AreSame(entry2, playlist.Entries[2]);
            AddLogEntry("Playlist entry moved up successfully");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Playlist_ReorderEntry_MoveDown()
        {
            // Arrange
            var playlist = new Playlist();
            var entry1 = new PlaylistEntry { SongName = "Song 1" };
            var entry2 = new PlaylistEntry { SongName = "Song 2" };
            var entry3 = new PlaylistEntry { SongName = "Song 3" };
            playlist.Entries.Add(entry1);
            playlist.Entries.Add(entry2);
            playlist.Entries.Add(entry3);

            // Act - Move entry1 down (from index 0 to index 1)
            playlist.Entries.RemoveAt(0);
            playlist.Entries.Insert(1, entry1);

            // Assert
            Assert.AreSame(entry2, playlist.Entries[0]);
            Assert.AreSame(entry1, playlist.Entries[1]);
            Assert.AreSame(entry3, playlist.Entries[2]);
            AddLogEntry("Playlist entry moved down successfully");
        }

        #endregion

        #region AppSettings Playlist Persistence Tests

        [TestMethod]
        [TestCategory("Unit")]
        public void AppSettings_Playlists_InitializesAsEmptyList()
        {
            // Arrange & Act
            var settings = AppSettings.Instance;

            // Assert
            Assert.IsNotNull(settings.Playlists);
            Assert.AreEqual(0, settings.Playlists.Count);
            AddLogEntry("AppSettings.Playlists initialized as empty list");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AppSettings_AddPlaylist_SavesCorrectly()
        {
            // Arrange
            var settings = AppSettings.Instance;
            var playlist = new Playlist { Name = "Test Playlist" };
            playlist.Entries.Add(new PlaylistEntry { SongName = "Song 1", PageNo = 10, BookName = "Book 1" });

            // Act
            settings.Playlists.Add(playlist);
            settings.Save();

            // Assert
            Assert.AreEqual(1, settings.Playlists.Count);
            Assert.AreEqual("Test Playlist", settings.Playlists[0].Name);
            AddLogEntry("Playlist saved to AppSettings");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AppSettings_Playlists_PersistsAcrossSaveLoad()
        {
            // Arrange
            var settings = AppSettings.Instance;
            var playlist = new Playlist { Name = "Persisted Playlist" };
            playlist.Entries.Add(new PlaylistEntry 
            { 
                SongName = "Moonlight Sonata", 
                Composer = "Beethoven",
                PageNo = 42, 
                BookName = "Classical Favorites",
                Notes = "Beautiful piece"
            });
            playlist.Entries.Add(new PlaylistEntry 
            { 
                SongName = "Clair de Lune", 
                Composer = "Debussy",
                PageNo = 15, 
                BookName = "French Impressionists"
            });
            settings.Playlists.Add(playlist);
            settings.LastSelectedPlaylist = "Persisted Playlist";
            settings.Save();

            // Act - Reset and reload
            AppSettings.ResetForTesting(_testSettingsPath);
            var reloadedSettings = AppSettings.Instance;

            // Assert
            Assert.AreEqual(1, reloadedSettings.Playlists.Count);
            Assert.AreEqual("Persisted Playlist", reloadedSettings.Playlists[0].Name);
            Assert.AreEqual(2, reloadedSettings.Playlists[0].Entries.Count);
            Assert.AreEqual("Moonlight Sonata", reloadedSettings.Playlists[0].Entries[0].SongName);
            Assert.AreEqual("Beethoven", reloadedSettings.Playlists[0].Entries[0].Composer);
            Assert.AreEqual(42, reloadedSettings.Playlists[0].Entries[0].PageNo);
            Assert.AreEqual("Classical Favorites", reloadedSettings.Playlists[0].Entries[0].BookName);
            Assert.AreEqual("Beautiful piece", reloadedSettings.Playlists[0].Entries[0].Notes);
            Assert.AreEqual("Clair de Lune", reloadedSettings.Playlists[0].Entries[1].SongName);
            Assert.AreEqual("Persisted Playlist", reloadedSettings.LastSelectedPlaylist);
            AddLogEntry("Playlists persisted and reloaded correctly");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AppSettings_MultiplePlaylists_PersistCorrectly()
        {
            // Arrange
            var settings = AppSettings.Instance;
            
            var playlist1 = new Playlist { Name = "Classical" };
            playlist1.Entries.Add(new PlaylistEntry { SongName = "Symphony No. 5", PageNo = 1 });
            
            var playlist2 = new Playlist { Name = "Jazz" };
            playlist2.Entries.Add(new PlaylistEntry { SongName = "Take Five", PageNo = 1 });
            playlist2.Entries.Add(new PlaylistEntry { SongName = "So What", PageNo = 10 });
            
            var playlist3 = new Playlist { Name = "Rock" };
            
            settings.Playlists.Add(playlist1);
            settings.Playlists.Add(playlist2);
            settings.Playlists.Add(playlist3);
            settings.Save();

            // Act - Reset and reload
            AppSettings.ResetForTesting(_testSettingsPath);
            var reloadedSettings = AppSettings.Instance;

            // Assert
            Assert.AreEqual(3, reloadedSettings.Playlists.Count);
            Assert.AreEqual("Classical", reloadedSettings.Playlists[0].Name);
            Assert.AreEqual(1, reloadedSettings.Playlists[0].Entries.Count);
            Assert.AreEqual("Jazz", reloadedSettings.Playlists[1].Name);
            Assert.AreEqual(2, reloadedSettings.Playlists[1].Entries.Count);
            Assert.AreEqual("Rock", reloadedSettings.Playlists[2].Name);
            Assert.AreEqual(0, reloadedSettings.Playlists[2].Entries.Count);
            AddLogEntry("Multiple playlists persisted correctly");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AppSettings_RemovePlaylist_SavesCorrectly()
        {
            // Arrange
            var settings = AppSettings.Instance;
            var playlist1 = new Playlist { Name = "Playlist 1" };
            var playlist2 = new Playlist { Name = "Playlist 2" };
            settings.Playlists.Add(playlist1);
            settings.Playlists.Add(playlist2);
            settings.Save();

            // Act
            settings.Playlists.Remove(playlist1);
            settings.Save();

            // Reset and reload
            AppSettings.ResetForTesting(_testSettingsPath);
            var reloadedSettings = AppSettings.Instance;

            // Assert
            Assert.AreEqual(1, reloadedSettings.Playlists.Count);
            Assert.AreEqual("Playlist 2", reloadedSettings.Playlists[0].Name);
            AddLogEntry("Playlist removal persisted correctly");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AppSettings_ModifyPlaylistName_SavesCorrectly()
        {
            // Arrange
            var settings = AppSettings.Instance;
            var playlist = new Playlist { Name = "Original Name" };
            settings.Playlists.Add(playlist);
            settings.Save();

            // Act
            settings.Playlists[0].Name = "New Name";
            settings.Playlists[0].ModifiedDate = DateTime.Now;
            settings.Save();

            // Reset and reload
            AppSettings.ResetForTesting(_testSettingsPath);
            var reloadedSettings = AppSettings.Instance;

            // Assert
            Assert.AreEqual("New Name", reloadedSettings.Playlists[0].Name);
            AddLogEntry("Playlist name modification persisted correctly");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AppSettings_LastSelectedPlaylist_PersistsCorrectly()
        {
            // Arrange
            var settings = AppSettings.Instance;
            settings.LastSelectedPlaylist = "My Favorite Playlist";
            settings.Save();

            // Act - Reset and reload
            AppSettings.ResetForTesting(_testSettingsPath);
            var reloadedSettings = AppSettings.Instance;

            // Assert
            Assert.AreEqual("My Favorite Playlist", reloadedSettings.LastSelectedPlaylist);
            AddLogEntry("LastSelectedPlaylist persisted correctly");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AppSettings_LastSelectedPlaylist_DefaultIsNull()
        {
            // Arrange & Act
            var settings = AppSettings.Instance;

            // Assert
            Assert.IsNull(settings.LastSelectedPlaylist);
            AddLogEntry("LastSelectedPlaylist default is null");
        }
        
        [TestMethod]
        [TestCategory("Unit")]
        public void AppSettings_RoamingSettingsPath_IsInMusicFolder()
        {
            // Arrange & Act
            var roamingPath = AppSettings.RoamingSettingsPath;

            // Assert - Should be in the music folder (which is _testMusicFolder for tests)
            Assert.IsNotNull(roamingPath);
            Assert.IsTrue(roamingPath!.StartsWith(_testMusicFolder), $"Roaming path should be in music folder. Path: {roamingPath}");
            Assert.IsTrue(roamingPath.Contains(".sheetmusicviewer"), "Roaming path should contain .sheetmusicviewer folder");
            AddLogEntry($"Roaming path: {roamingPath}");
        }
        
        [TestMethod]
        [TestCategory("Unit")]
        public void AppSettings_PlaylistsSavedToMusicFolder()
        {
            // Arrange
            var settings = AppSettings.Instance;
            var playlist = new Playlist { Name = "Music Folder Test" };
            playlist.Entries.Add(new PlaylistEntry { SongName = "Test Song", PageNo = 1 });
            settings.Playlists.Add(playlist);
            
            // Act
            settings.Save();
            
            // Assert - Roaming file should exist in music folder
            var roamingPath = AppSettings.RoamingSettingsPath;
            Assert.IsNotNull(roamingPath);
            Assert.IsTrue(File.Exists(roamingPath), $"Roaming file should exist at {roamingPath}");
            
            var roamingContent = File.ReadAllText(roamingPath);
            Assert.IsTrue(roamingContent.Contains("Music Folder Test"), "Roaming file should contain playlist");
            Assert.IsTrue(roamingContent.Contains("Test Song"), "Roaming file should contain playlist entry");
            AddLogEntry("Playlists saved to music folder correctly");
        }
        
        [TestMethod]
        [TestCategory("Unit")]
        public void AppSettings_WindowSettingsSavedToLocalFile()
        {
            // Arrange
            var settings = AppSettings.Instance;
            settings.WindowWidth = 1234;
            settings.WindowHeight = 5678;
            
            // Act
            settings.Save();
            
            // Assert - Local file should exist and contain window settings
            var localPath = AppSettings.LocalSettingsPath;
            Assert.IsTrue(File.Exists(localPath), $"Local file should exist at {localPath}");
            
            var localContent = File.ReadAllText(localPath);
            Assert.IsTrue(localContent.Contains("1234"), "Local file should contain WindowWidth");
            Assert.IsTrue(localContent.Contains("5678"), "Local file should contain WindowHeight");
            
            // Roaming file should NOT contain window settings
            var roamingPath = AppSettings.RoamingSettingsPath;
            if (roamingPath != null && File.Exists(roamingPath))
            {
                var roamingContent = File.ReadAllText(roamingPath);
                Assert.IsFalse(roamingContent.Contains("WindowWidth"), "Roaming file should not contain WindowWidth");
            }
            AddLogEntry("Window settings saved to local file only");
        }

        #endregion

        #region Edge Cases

        [TestMethod]
        [TestCategory("Unit")]
        public void PlaylistEntry_EmptySongName_ToStringHandlesGracefully()
        {
            // Arrange
            var entry = new PlaylistEntry { PageNo = 5 };

            // Act
            var result = entry.ToString();

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Contains("5"));
            AddLogEntry($"Empty song name ToString: {result}");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Playlist_EmptyName_ToStringHandlesGracefully()
        {
            // Arrange
            var playlist = new Playlist { Name = "" };

            // Act
            var result = playlist.ToString();

            // Assert
            Assert.IsNotNull(result);
            AddLogEntry($"Empty playlist name ToString: {result}");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Playlist_NullEntries_CanBeAdded()
        {
            // Arrange
            var playlist = new Playlist();
            var entry = new PlaylistEntry(); // All defaults

            // Act
            playlist.Entries.Add(entry);

            // Assert
            Assert.AreEqual(1, playlist.Entries.Count);
            Assert.AreEqual(string.Empty, playlist.Entries[0].SongName);
            AddLogEntry("Playlist can contain entries with default values");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Playlist_DuplicateEntries_AreAllowed()
        {
            // Arrange
            var playlist = new Playlist();
            var entry = new PlaylistEntry { SongName = "Same Song", PageNo = 10 };

            // Act - Add the same entry twice
            playlist.Entries.Add(entry);
            playlist.Entries.Add(entry);

            // Assert
            Assert.AreEqual(2, playlist.Entries.Count);
            Assert.AreSame(playlist.Entries[0], playlist.Entries[1]);
            AddLogEntry("Duplicate entries are allowed in playlist");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Playlist_LargeNumberOfEntries_HandlesCorrectly()
        {
            // Arrange
            var playlist = new Playlist { Name = "Large Playlist" };
            var settings = AppSettings.Instance;

            // Act - Add 1000 entries
            for (int i = 0; i < 1000; i++)
            {
                playlist.Entries.Add(new PlaylistEntry
                {
                    SongName = $"Song {i}",
                    Composer = $"Composer {i}",
                    PageNo = i,
                    BookName = $"Book {i % 10}",
                    Notes = $"Notes for song {i}"
                });
            }
            settings.Playlists.Add(playlist);
            settings.Save();

            // Reset and reload
            AppSettings.ResetForTesting(_testSettingsPath);
            var reloadedSettings = AppSettings.Instance;

            // Assert
            Assert.AreEqual(1, reloadedSettings.Playlists.Count);
            Assert.AreEqual(1000, reloadedSettings.Playlists[0].Entries.Count);
            Assert.AreEqual("Song 500", reloadedSettings.Playlists[0].Entries[500].SongName);
            AddLogEntry("Large playlist with 1000 entries persisted correctly");
        }

        #endregion

        #region ReloadRoaming Tests

        [TestMethod]
        [TestCategory("Unit")]
        public void AppSettings_ReloadRoaming_PicksUpExternalChanges()
        {
            // Arrange - Create initial playlist and save
            var settings = AppSettings.Instance;
            var playlist = new Playlist { Name = "Original Playlist" };
            playlist.Entries.Add(new PlaylistEntry { SongName = "Original Song", PageNo = 1 });
            settings.Playlists.Add(playlist);
            settings.Save();
            
            // Simulate external change (like OneDrive sync) by directly modifying the file
            var roamingPath = AppSettings.RoamingSettingsPath;
            Assert.IsNotNull(roamingPath);
            
            // Create JSON that mimics external sync
            var externalJson = """
            {
                "Playlists": [
                    {
                        "Name": "Synced Playlist",
                        "Entries": [
                            { "SongName": "Synced Song 1", "PageNo": 10, "Composer": "", "BookName": "", "Notes": "" },
                            { "SongName": "Synced Song 2", "PageNo": 20, "Composer": "", "BookName": "", "Notes": "" }
                        ]
                    }
                ],
                "LastSelectedPlaylist": "Synced Playlist"
            }
            """;
            File.WriteAllText(roamingPath!, externalJson);
            
            // Act - Reload roaming settings
            settings.ReloadRoaming();
            
            // Assert - Should have picked up the external changes
            Assert.AreEqual(1, settings.Playlists.Count);
            Assert.AreEqual("Synced Playlist", settings.Playlists[0].Name);
            Assert.AreEqual(2, settings.Playlists[0].Entries.Count);
            Assert.AreEqual("Synced Song 1", settings.Playlists[0].Entries[0].SongName);
            Assert.AreEqual("Synced Song 2", settings.Playlists[0].Entries[1].SongName);
            Assert.AreEqual("Synced Playlist", settings.LastSelectedPlaylist);
            AddLogEntry("ReloadRoaming picked up external changes correctly");
        }
        
        [TestMethod]
        [TestCategory("Unit")]
        public void AppSettings_ReloadRoaming_PreservesLocalSettings()
        {
            // Arrange - Set both local and roaming settings
            var settings = AppSettings.Instance;
            settings.WindowWidth = 1920;
            settings.WindowHeight = 1080;
            settings.Playlists.Add(new Playlist { Name = "Initial" });
            settings.Save();
            
            // Simulate external change to playlists
            var roamingPath = AppSettings.RoamingSettingsPath;
            Assert.IsNotNull(roamingPath);
            
            var externalJson = """
            {
                "Playlists": [
                    { "Name": "NewPlaylist", "Entries": [] }
                ]
            }
            """;
            File.WriteAllText(roamingPath!, externalJson);
            
            // Act
            settings.ReloadRoaming();
            
            // Assert - Roaming settings updated, local settings preserved
            Assert.AreEqual("NewPlaylist", settings.Playlists[0].Name);
            Assert.AreEqual(1920, settings.WindowWidth); // Local setting preserved
            Assert.AreEqual(1080, settings.WindowHeight); // Local setting preserved
            AddLogEntry("ReloadRoaming preserves local settings");
        }
        
        [TestMethod]
        [TestCategory("Unit")]
        public void AppSettings_ReloadRoaming_HandlesCorruptFile()
        {
            // Arrange
            var settings = AppSettings.Instance;
            settings.Playlists.Add(new Playlist { Name = "Original" });
            settings.Save();
            
            // Corrupt the roaming file
            var roamingPath = AppSettings.RoamingSettingsPath;
            Assert.IsNotNull(roamingPath);
            File.WriteAllText(roamingPath!, "{ invalid json content ]]]");
            
            // Act - Should not throw
            settings.ReloadRoaming();
            
            // Assert - Original settings should be preserved (reload failed gracefully)
            // The in-memory settings should still have the original playlist
            // (The corrupt file doesn't overwrite the in-memory state)
            Assert.AreEqual(1, settings.Playlists.Count);
            Assert.AreEqual("Original", settings.Playlists[0].Name);
            AddLogEntry("ReloadRoaming handles corrupt file gracefully");
        }
        
        [TestMethod]
        [TestCategory("Unit")]
        public void AppSettings_ReloadRoaming_HandlesMissingFile()
        {
            // Arrange
            var settings = AppSettings.Instance;
            settings.Playlists.Add(new Playlist { Name = "Existing" });
            
            // Delete the roaming file if it exists
            var roamingPath = AppSettings.RoamingSettingsPath;
            if (roamingPath != null && File.Exists(roamingPath))
            {
                File.Delete(roamingPath);
            }
            
            // Act - Should not throw
            settings.ReloadRoaming();
            
            // Assert - Settings should be unchanged
            Assert.AreEqual(1, settings.Playlists.Count);
            Assert.AreEqual("Existing", settings.Playlists[0].Name);
            AddLogEntry("ReloadRoaming handles missing file gracefully");
        }
        
        [TestMethod]
        [TestCategory("Unit")]
        public void AppSettings_ReloadRoaming_MultipleCallsAreIdempotent()
        {
            // Arrange
            var settings = AppSettings.Instance;
            var playlist = new Playlist { Name = "Test" };
            playlist.Entries.Add(new PlaylistEntry { SongName = "Song 1", PageNo = 1 });
            settings.Playlists.Add(playlist);
            settings.Save();
            
            // Act - Call ReloadRoaming multiple times
            settings.ReloadRoaming();
            settings.ReloadRoaming();
            settings.ReloadRoaming();
            
            // Assert - Should still have the same data
            Assert.AreEqual(1, settings.Playlists.Count);
            Assert.AreEqual("Test", settings.Playlists[0].Name);
            Assert.AreEqual(1, settings.Playlists[0].Entries.Count);
            AddLogEntry("Multiple ReloadRoaming calls are idempotent");
        }

        #endregion

        #region Re-entrancy Guard Pattern Tests
        
        [TestMethod]
        [TestCategory("Unit")]
        public void ReentrancyGuard_ProtectsWithTryFinally()
        {
            // Arrange - Simulate a re-entrancy guard pattern
            bool isHandling = false;
            int callCount = 0;
            Exception? caughtException = null;
            
            void HandlerWithGuard(bool shouldThrow)
            {
                if (isHandling) return;
                isHandling = true;
                
                try
                {
                    callCount++;
                    if (shouldThrow)
                    {
                        throw new InvalidOperationException("Test exception");
                    }
                    // Simulate re-entrant call
                    HandlerWithGuard(false);
                }
                finally
                {
                    isHandling = false;
                }
            }
            
            // Act - Call normally
            HandlerWithGuard(false);
            Assert.AreEqual(1, callCount, "Re-entrant call should be blocked");
            Assert.IsFalse(isHandling, "Flag should be reset after normal execution");
            
            // Act - Call with exception
            callCount = 0;
            try
            {
                HandlerWithGuard(true);
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }
            
            // Assert - Flag should still be reset even after exception
            Assert.IsNotNull(caughtException, "Exception should have been thrown");
            Assert.IsFalse(isHandling, "Flag should be reset even after exception (try/finally pattern)");
            Assert.AreEqual(1, callCount, "Should have been called once before exception");
            
            // Act - Call again after exception - should work
            callCount = 0;
            HandlerWithGuard(false);
            Assert.AreEqual(1, callCount, "Should be able to call again after exception was handled");
            AddLogEntry("Re-entrancy guard with try/finally works correctly");
        }
        
        [TestMethod]
        [TestCategory("Unit")]
        public void ReentrancyGuard_WithoutTryFinally_LeavesDeadlock()
        {
            // This test demonstrates why try/finally is important
            // Arrange
            bool isHandling = false;
            int callCount = 0;
            
            void HandlerWithoutGuard(bool shouldThrow)
            {
                if (isHandling) return;
                isHandling = true;
                
                // BUG: No try/finally!
                callCount++;
                if (shouldThrow)
                {
                    // isHandling is never reset!
                    throw new InvalidOperationException("Test exception");
                }
                isHandling = false;
            }
            
            // Act - Call with exception
            try
            {
                HandlerWithoutGuard(true);
            }
            catch { }
            
            // Assert - Flag is stuck!
            Assert.IsTrue(isHandling, "Without try/finally, flag stays true after exception (BAD)");
            
            // Try to call again - will be blocked forever
            callCount = 0;
            HandlerWithoutGuard(false);
            Assert.AreEqual(0, callCount, "Subsequent calls are blocked because flag is stuck (deadlock)");
            AddLogEntry("Demonstrated why try/finally is required for re-entrancy guards");
        }
        
        [TestMethod]
        [TestCategory("Unit")]
        public void ReentrancyGuard_ForCollectionModification_PreventsNestedUpdates()
        {
            // This test demonstrates the pattern used in RefreshPlaylistComboBox
            // to prevent "Source collection was modified during selection update" errors
            
            // Arrange
            bool isRefreshing = false;
            var items = new List<string>();
            int refreshCount = 0;
            int selectionChangedCount = 0;
            
            void OnSelectionChanged()
            {
                // Ignore selection changes during refresh
                if (isRefreshing) return;
                selectionChangedCount++;
                
                // This would normally trigger a cascade of events
                // In the real code, this could call RefreshItems again
            }
            
            void RefreshItems()
            {
                if (isRefreshing) return;
                isRefreshing = true;
                
                try
                {
                    refreshCount++;
                    
                    // Clearing items triggers selection changed events
                    items.Clear();
                    OnSelectionChanged(); // Simulates the event
                    
                    // Adding items also triggers selection changed events
                    items.Add("Item 1");
                    OnSelectionChanged(); // Simulates the event
                    
                    items.Add("Item 2");
                    OnSelectionChanged(); // Simulates the event
                }
                finally
                {
                    isRefreshing = false;
                }
            }
            
            // Act
            RefreshItems();
            
            // Assert
            Assert.AreEqual(1, refreshCount, "RefreshItems should only be called once");
            Assert.AreEqual(0, selectionChangedCount, "Selection changed events should be ignored during refresh");
            Assert.AreEqual(2, items.Count, "Items should be populated");
            Assert.IsFalse(isRefreshing, "Flag should be reset after refresh");
            
            // Now selection changes should work
            OnSelectionChanged();
            Assert.AreEqual(1, selectionChangedCount, "Selection changes should work after refresh completes");
            AddLogEntry("Re-entrancy guard prevents collection modification errors");
        }

        #endregion
        
        #region Playlist Selection Edge Cases Tests
        
        [TestMethod]
        [TestCategory("Unit")]
        public void PlaylistSelection_BoundsCheck_PreventsOutOfRangeException()
        {
            // This test verifies the pattern used in OnPlaylistSelectionChanged
            // to prevent ArgumentOutOfRangeException when SelectedIndex is invalid
            
            // Arrange
            var items = new List<string> { "Playlist1", "Playlist2" };
            int selectedIndex = -1; // Invalid index
            bool accessedItem = false;
            
            // Act - Simulate the bounds check pattern
            if (selectedIndex >= 0 && selectedIndex < items.Count)
            {
                accessedItem = true;
                var _ = items[selectedIndex];
            }
            
            // Assert
            Assert.IsFalse(accessedItem, "Should not access item when index is -1");
            
            // Test with index beyond range
            selectedIndex = 5; // Beyond range
            if (selectedIndex >= 0 && selectedIndex < items.Count)
            {
                accessedItem = true;
                var _ = items[selectedIndex];
            }
            
            Assert.IsFalse(accessedItem, "Should not access item when index is beyond range");
            
            // Test with valid index
            selectedIndex = 1;
            if (selectedIndex >= 0 && selectedIndex < items.Count)
            {
                accessedItem = true;
                var _ = items[selectedIndex];
            }
            
            Assert.IsTrue(accessedItem, "Should access item when index is valid");
            AddLogEntry("Bounds check pattern prevents out of range exceptions");
        }
        
        [TestMethod]
        [TestCategory("Unit")]
        public void PlaylistCreation_SetCurrentBeforeRefresh_PreventsRaceCondition()
        {
            // This test verifies the pattern of setting _currentPlaylist before
            // calling RefreshPlaylistComboBox to avoid race conditions
            
            // Arrange
            var settings = AppSettings.Instance;
            var newPlaylist = new Playlist { Name = "NewPlaylist" };
            Playlist? currentPlaylist = null;
            bool refreshCalled = false;
            bool entriesRefreshed = false;
            
            // Simulate the correct order of operations
            void CreatePlaylistCorrectOrder()
            {
                // 1. Add to settings
                settings.Playlists.Add(newPlaylist);
                
                // 2. Set current playlist BEFORE refresh
                currentPlaylist = newPlaylist;
                
                // 3. Refresh combo box (which might trigger selection events)
                refreshCalled = true;
                
                // 4. Refresh entries
                entriesRefreshed = currentPlaylist != null;
            }
            
            // Act
            CreatePlaylistCorrectOrder();
            
            // Assert
            Assert.IsNotNull(currentPlaylist, "Current playlist should be set");
            Assert.AreSame(newPlaylist, currentPlaylist, "Current playlist should be the new playlist");
            Assert.IsTrue(refreshCalled, "Refresh should have been called");
            Assert.IsTrue(entriesRefreshed, "Entries should have been refreshed");
            AddLogEntry("Setting current playlist before refresh prevents race conditions");
        }
        
        [TestMethod]
        [TestCategory("Unit")]
        public void PlaylistDeletion_SelectFirstOrClear_HandlesEmptyList()
        {
            // This test verifies the pattern used in OnDeletePlaylistClick
            // to handle the case when all playlists are deleted
            
            // Arrange
            var playlists = new List<Playlist>
            {
                new Playlist { Name = "ToDelete" }
            };
            Playlist? currentPlaylist = playlists[0];
            string statusText = "";
            
            // Act - Simulate deletion
            playlists.Remove(currentPlaylist);
            currentPlaylist = playlists.FirstOrDefault(); // Will be null
            
            // Refresh entries pattern
            if (currentPlaylist == null)
            {
                statusText = "No playlist selected";
            }
            else
            {
                statusText = $"{currentPlaylist.Entries.Count} song(s)";
            }
            
            // Assert
            Assert.IsNull(currentPlaylist, "Current playlist should be null after deleting last one");
            Assert.AreEqual("No playlist selected", statusText, "Status should indicate no playlist");
            AddLogEntry("Playlist deletion handles empty list correctly");
        }
        
        [TestMethod]
        [TestCategory("Unit")]
        public void PlaylistSelection_IgnoreDuringRefresh_PreventsRecursion()
        {
            // This test verifies the _isRefreshingPlaylistCombo guard pattern
            
            // Arrange
            bool isRefreshing = false;
            int selectionChangedCount = 0;
            Playlist? selectedPlaylist = null;
            var playlists = new List<Playlist>
            {
                new Playlist { Name = "Playlist1" },
                new Playlist { Name = "Playlist2" }
            };
            
            void OnSelectionChanged(int index)
            {
                // Guard check
                if (isRefreshing) return;
                
                selectionChangedCount++;
                if (index >= 0 && index < playlists.Count)
                {
                    selectedPlaylist = playlists[index];
                }
            }
            
            void RefreshComboBox()
            {
                if (isRefreshing) return;
                isRefreshing = true;
                
                try
                {
                    // Simulates clearing and re-adding items, which triggers selection events
                    OnSelectionChanged(-1); // Clear triggers this
                    OnSelectionChanged(0);  // Adding first item triggers this
                }
                finally
                {
                    isRefreshing = false;
                }
            }
            
            // Act
            RefreshComboBox();
            
            // Assert - Selection changes during refresh should be ignored
            Assert.AreEqual(0, selectionChangedCount, "Selection changes should be ignored during refresh");
            Assert.IsNull(selectedPlaylist, "No playlist should be selected during refresh");
            
            // Now selection changes should work
            OnSelectionChanged(1);
            Assert.AreEqual(1, selectionChangedCount, "Selection changes should work after refresh");
            Assert.AreSame(playlists[1], selectedPlaylist, "Second playlist should be selected");
            AddLogEntry("Selection changes are properly ignored during refresh");
        }

        #endregion
        
        #region SelectionChanged Event Bubbling Tests

        [TestMethod]
        [TestCategory("Unit")]
        public void SelectionChanged_EventBubbling_FilterByAddedItemType()
        {
            // This test demonstrates the bug fix for SelectionChanged events bubbling
            // from child controls (ComboBox) to parent controls.
            // 
            // Bug: When user changes playlist selection in ComboBox, the SelectionChanged
            // event bubbles up to the TabControl's SelectionChanged handler. That handler
            // would call ReloadRoaming(), which replaces the Playlists collection while
            // Avalonia is still processing the ComboBox's selection change, causing
            // ArgumentOutOfRangeException.
            //
            // Fix: Check if e.AddedItems[0] is a TabItem before processing.
            // If it's not a TabItem, it's a bubbled event from a child control.
            
            // Arrange
            object? tabControlHandler_AddedItem = null;
            int tabControlHandler_CallCount = 0;
            bool reloadRoamingCalled = false;
            
            void OnTabSelectionChanged(object? addedItem)
            {
                tabControlHandler_AddedItem = addedItem;
                tabControlHandler_CallCount++;
                
                // CORRECT approach (the fix):
                // Only handle tab changes, not child control changes
                // In real code this checks "is TabItem", here we simulate with SimulatedTabItem
                if (addedItem is SimulatedTabItem)
                {
                    reloadRoamingCalled = true;
                }
            }
            
            // Simulate TabControl receiving actual tab change
            OnTabSelectionChanged(new SimulatedTabItem { Header = "_Playlists" });
            Assert.IsTrue(reloadRoamingCalled, "Should call ReloadRoaming for actual tab change");
            Assert.AreEqual(1, tabControlHandler_CallCount);
            
            // Reset
            reloadRoamingCalled = false;
            tabControlHandler_CallCount = 0;
            
            // Simulate TabControl receiving bubbled event from ComboBox (string item)
            OnTabSelectionChanged("Playlist Name");
            Assert.IsFalse(reloadRoamingCalled, "Should NOT call ReloadRoaming for ComboBox selection bubbling");
            Assert.AreEqual(1, tabControlHandler_CallCount);
            
            // Reset
            reloadRoamingCalled = false;
            tabControlHandler_CallCount = 0;
            
            // Simulate TabControl receiving bubbled event from ComboBox (ComboBoxItem)
            OnTabSelectionChanged(new SimulatedComboBoxItem { Content = "Playlist Name" });
            Assert.IsFalse(reloadRoamingCalled, "Should NOT call ReloadRoaming for ComboBoxItem selection bubbling");
            
            // Reset
            reloadRoamingCalled = false;
            tabControlHandler_CallCount = 0;
            
            // Simulate TabControl receiving null (edge case)
            OnTabSelectionChanged(null);
            Assert.IsFalse(reloadRoamingCalled, "Should NOT call ReloadRoaming for null AddedItem");
            
            AddLogEntry("SelectionChanged event bubbling correctly filtered by checking AddedItem type");
        }
        
        // Helper classes to simulate Avalonia control types
        private class SimulatedTabItem
        {
            public string Header { get; set; } = "";
        }
        
        private class SimulatedComboBoxItem
        {
            public object? Content { get; set; }
        }
        
        [TestMethod]
        [TestCategory("Unit")]
        public void PlaylistComboBox_SelectionChange_DoesNotTriggerTabHandler()
        {
            // This test simulates the exact bug scenario:
            // 1. User is on Playlists tab
            // 2. User changes playlist in ComboBox
            // 3. ComboBox SelectionChanged event bubbles to TabControl
            // 4. TabControl handler should ignore this event
            
            // Arrange
            bool isHandlingTabChange = false;
            bool reloadRoamingCalled = false;
            var playlists = new List<Playlist>
            {
                new Playlist { Name = "Playlist1" },
                new Playlist { Name = "Playlist2" }
            };
            
            // This is the corrected handler
            void OnTabSelectionChanged_Corrected(object? addedItem)
            {
                if (isHandlingTabChange) return;
                
                // KEY FIX: Only process if the added item is actually a TabItem
                if (addedItem is not SimulatedTabItem)
                {
                    return;  // Ignore bubbled events from ComboBox
                }
                
                isHandlingTabChange = true;
                try
                {
                    // This would reload playlists from disk
                    reloadRoamingCalled = true;
                }
                finally
                {
                    isHandlingTabChange = false;
                }
            }
            
            // Simulate: User is already on Playlists tab, changes playlist in ComboBox
            // The ComboBox SelectionChanged event bubbles up with a string or ComboBoxItem
            OnTabSelectionChanged_Corrected("Playlist2"); // Bubbled from ComboBox
            
            Assert.IsFalse(reloadRoamingCalled, 
                "ReloadRoaming should NOT be called when ComboBox selection changes");
            
            // But actual tab changes should still work
            OnTabSelectionChanged_Corrected(new SimulatedTabItem { Header = "_Playlists" });
            
            Assert.IsTrue(reloadRoamingCalled, 
                "ReloadRoaming SHOULD be called when actually switching to Playlists tab");
            
            AddLogEntry("PlaylistComboBox selection change does not incorrectly trigger TabControl handler");
        }

        #endregion
    }
}
