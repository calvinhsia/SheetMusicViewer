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

        [TestInitialize]
        public void PlaylistTestInitialize()
        {
            // Create a unique test settings path for each test
            _testSettingsPath = Path.Combine(Path.GetTempPath(), $"PlaylistTests_{Guid.NewGuid()}.json");
            AppSettings.ResetForTesting(_testSettingsPath);
        }

        [TestCleanup]
        public void PlaylistTestCleanup()
        {
            // Clean up test settings file
            if (File.Exists(_testSettingsPath))
            {
                try
                {
                    File.Delete(_testSettingsPath);
                }
                catch { }
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
    }
}
