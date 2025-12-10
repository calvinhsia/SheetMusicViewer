using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SheetMusicLib;

namespace Tests
{
    /// <summary>
    /// Tests for BMK file XML serialization/deserialization.
    /// These tests ensure that the SerializablePdfMetaData class correctly deserializes
    /// BMK files with the PdfVolumeInfo element name (not PdfVolumeInfoBase).
    /// 
    /// Root cause this prevents: Without [XmlArrayItem("PdfVolumeInfo")] on lstVolInfo,
    /// the serializer expects &lt;PdfVolumeInfoBase&gt; elements but BMK files use &lt;PdfVolumeInfo&gt;,
    /// causing lstVolInfo to be empty after deserialization.
    /// </summary>
    [TestClass]
    public class BmkXmlSerializationTests : TestBase
    {
        #region Core Deserialization Tests

        [TestMethod]
        [TestCategory("Unit")]
        public void TestBmkDeserialization_VolumeInfoListIsNotEmpty()
        {
            // This is the key test - without [XmlArrayItem("PdfVolumeInfo")],
            // lstVolInfo would be empty because the serializer expects <PdfVolumeInfoBase>
            var serializer = new XmlSerializer(typeof(SerializablePdfMetaData));
            using var reader = new StringReader(TestAssets.SampleMultiVolumeBmk);
            
            var result = (SerializablePdfMetaData)serializer.Deserialize(reader);

            Assert.IsNotNull(result.lstVolInfo, "lstVolInfo should not be null");
            Assert.AreEqual(4, result.lstVolInfo.Count, 
                "lstVolInfo should have 4 volumes. If 0, the [XmlArrayItem(\"PdfVolumeInfo\")] attribute is missing.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TestBmkDeserialization_VolumeInfoHasCorrectData()
        {
            var serializer = new XmlSerializer(typeof(SerializablePdfMetaData));
            using var reader = new StringReader(TestAssets.SampleMultiVolumeBmk);
            
            var result = (SerializablePdfMetaData)serializer.Deserialize(reader);

            // Volume 0
            Assert.AreEqual("59 Piano Solos You Like To Play.pdf", result.lstVolInfo[0].FileNameVolume);
            Assert.AreEqual(1, result.lstVolInfo[0].NPagesInThisVolume);
            Assert.AreEqual(0, result.lstVolInfo[0].Rotation);

            // Volume 1
            Assert.AreEqual("59 Piano Solos You Like to Play1.pdf", result.lstVolInfo[1].FileNameVolume);
            Assert.AreEqual(84, result.lstVolInfo[1].NPagesInThisVolume);
            Assert.AreEqual(2, result.lstVolInfo[1].Rotation);

            // Volume 2
            Assert.AreEqual("59 Piano Solos You Like to Play2.pdf", result.lstVolInfo[2].FileNameVolume);
            Assert.AreEqual(10, result.lstVolInfo[2].NPagesInThisVolume);
            Assert.AreEqual(2, result.lstVolInfo[2].Rotation);

            // Volume 3
            Assert.AreEqual("59 Piano Solos You Like to Play3.pdf", result.lstVolInfo[3].FileNameVolume);
            Assert.AreEqual(62, result.lstVolInfo[3].NPagesInThisVolume);
            Assert.AreEqual(2, result.lstVolInfo[3].Rotation);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TestBmkDeserialization_TotalPageCountIsCorrect()
        {
            var serializer = new XmlSerializer(typeof(SerializablePdfMetaData));
            using var reader = new StringReader(TestAssets.SampleMultiVolumeBmk);
            
            var result = (SerializablePdfMetaData)serializer.Deserialize(reader);

            var totalPages = result.lstVolInfo.Sum(v => v.NPagesInThisVolume);
            Assert.AreEqual(1 + 84 + 10 + 62, totalPages, "Total pages should be 157");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TestBmkDeserialization_SingleVolume()
        {
            var serializer = new XmlSerializer(typeof(SerializablePdfMetaData));
            using var reader = new StringReader(TestAssets.SampleSingleVolumeBmk);
            
            var result = (SerializablePdfMetaData)serializer.Deserialize(reader);

            Assert.AreEqual(1, result.lstVolInfo.Count);
            Assert.AreEqual("SheetMusicExcerpts.pdf", result.lstVolInfo[0].FileNameVolume);
            Assert.AreEqual(5, result.lstVolInfo[0].NPagesInThisVolume);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TestBmkDeserialization_TocEntriesAreDeserialized()
        {
            var serializer = new XmlSerializer(typeof(SerializablePdfMetaData));
            using var reader = new StringReader(TestAssets.SampleMultiVolumeBmk);
            
            var result = (SerializablePdfMetaData)serializer.Deserialize(reader);

            Assert.AreEqual(3, result.lstTocEntries.Count);
            Assert.AreEqual("Tango in D", result.lstTocEntries[0].SongName);
            Assert.AreEqual("ALBENIZ ISAAC", result.lstTocEntries[0].Composer);
            Assert.AreEqual(4, result.lstTocEntries[0].PageNo);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TestBmkDeserialization_MetadataFieldsAreDeserialized()
        {
            var serializer = new XmlSerializer(typeof(SerializablePdfMetaData));
            using var reader = new StringReader(TestAssets.SampleMultiVolumeBmk);
            
            var result = (SerializablePdfMetaData)serializer.Deserialize(reader);

            Assert.AreEqual(27, result.LastPageNo);
            Assert.AreEqual(0, result.PageNumberOffset);
            // Verify date/time was parsed (exact value depends on timezone handling)
            Assert.AreEqual(2024, result.dtLastWrite.Year);
            Assert.AreEqual(4, result.dtLastWrite.Month);
            Assert.AreEqual(27, result.dtLastWrite.Day);
        }

        #endregion

        #region Serialization Round-Trip Tests

        [TestMethod]
        [TestCategory("Unit")]
        public void TestBmkSerialization_RoundTrip_PreservesVolumeInfo()
        {
            // Create a new SerializablePdfMetaData
            var original = new SerializablePdfMetaData
            {
                LastPageNo = 42,
                PageNumberOffset = -5,
                dtLastWrite = DateTime.Now,
                lstVolInfo = new List<PdfVolumeInfoBase>
                {
                    new PdfVolumeInfoBase { FileNameVolume = "Book0.pdf", NPagesInThisVolume = 100, Rotation = 0 },
                    new PdfVolumeInfoBase { FileNameVolume = "Book1.pdf", NPagesInThisVolume = 50, Rotation = 2 }
                },
                lstTocEntries = new List<TOCEntry>
                {
                    new TOCEntry { SongName = "Test Song", Composer = "Test Composer", PageNo = 5 }
                }
            };

            // Serialize to string
            var serializer = new XmlSerializer(typeof(SerializablePdfMetaData));
            string xml;
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, original);
                xml = writer.ToString();
            }

            // Verify XML contains PdfVolumeInfo elements (not PdfVolumeInfoBase)
            Assert.IsTrue(xml.Contains("<PdfVolumeInfo>"), 
                "Serialized XML should contain <PdfVolumeInfo> elements due to [XmlArrayItem] attribute");
            Assert.IsFalse(xml.Contains("<PdfVolumeInfoBase>"), 
                "Serialized XML should NOT contain <PdfVolumeInfoBase> elements");

            // Deserialize back
            using var reader = new StringReader(xml);
            var result = (SerializablePdfMetaData)serializer.Deserialize(reader);

            // Verify round-trip
            Assert.AreEqual(2, result.lstVolInfo.Count);
            Assert.AreEqual("Book0.pdf", result.lstVolInfo[0].FileNameVolume);
            Assert.AreEqual(100, result.lstVolInfo[0].NPagesInThisVolume);
            Assert.AreEqual("Book1.pdf", result.lstVolInfo[1].FileNameVolume);
            Assert.AreEqual(50, result.lstVolInfo[1].NPagesInThisVolume);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TestBmkSerialization_XmlContainsCorrectElementNames()
        {
            var data = new SerializablePdfMetaData
            {
                lstVolInfo = new List<PdfVolumeInfoBase>
                {
                    new PdfVolumeInfoBase { FileNameVolume = "Test.pdf", NPagesInThisVolume = 10, Rotation = 0 }
                }
            };

            var serializer = new XmlSerializer(typeof(SerializablePdfMetaData));
            string xml;
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, data);
                xml = writer.ToString();
            }

            // Verify element names match BMK file format
            Assert.IsTrue(xml.Contains("<PdfVolumeInfo>"), "Should serialize as <PdfVolumeInfo>");
            Assert.IsTrue(xml.Contains("<NPages>10</NPages>"), "Should use <NPages> not <NPagesInThisVolume>");
            Assert.IsTrue(xml.Contains("<FileName>Test.pdf</FileName>"), "Should use <FileName> not <FileNameVolume>");
        }

        #endregion

        #region Edge Cases and Error Handling

        [TestMethod]
        [TestCategory("Unit")]
        public void TestBmkDeserialization_EmptyVolumeList()
        {
            var serializer = new XmlSerializer(typeof(SerializablePdfMetaData));
            using var reader = new StringReader(TestAssets.SampleEmptyVolumesBmk);
            
            var result = (SerializablePdfMetaData)serializer.Deserialize(reader);

            Assert.IsNotNull(result.lstVolInfo);
            Assert.AreEqual(0, result.lstVolInfo.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TestBmkDeserialization_MissingVolumeList()
        {
            var serializer = new XmlSerializer(typeof(SerializablePdfMetaData));
            using var reader = new StringReader(TestAssets.SampleNoVolumesBmk);
            
            var result = (SerializablePdfMetaData)serializer.Deserialize(reader);

            // lstVolInfo is initialized in the class, so it should be empty but not null
            Assert.IsNotNull(result.lstVolInfo);
            Assert.AreEqual(0, result.lstVolInfo.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TestBmkDeserialization_WithFavorites()
        {
            var serializer = new XmlSerializer(typeof(SerializablePdfMetaData));
            using var reader = new StringReader(TestAssets.SampleWithFavoritesBmk);
            
            var result = (SerializablePdfMetaData)serializer.Deserialize(reader);

            Assert.AreEqual(2, result.Favorites.Count);
            Assert.AreEqual(10, result.Favorites[0].Pageno);
            Assert.AreEqual("My Favorite Page", result.Favorites[0].FavoriteName);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TestBmkDeserialization_WithInkStrokes()
        {
            var serializer = new XmlSerializer(typeof(SerializablePdfMetaData));
            using var reader = new StringReader(TestAssets.SampleWithInkStrokesBmk);
            
            var result = (SerializablePdfMetaData)serializer.Deserialize(reader);

            Assert.AreEqual(1, result.LstInkStrokes.Count);
            Assert.AreEqual(5, result.LstInkStrokes[0].Pageno);
            // AQIDBA== is base64 for [1,2,3,4]
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, result.LstInkStrokes[0].StrokeData);
        }

        #endregion

        #region Full BMK File Sample Tests

        [TestMethod]
        [TestCategory("Unit")]
        public void TestBmkDeserialization_Full59PianoSolos_VolumeCount()
        {
            var serializer = new XmlSerializer(typeof(SerializablePdfMetaData));
            using var reader = new StringReader(TestAssets.Sample59PianoSolosFullBmk);
            
            var result = (SerializablePdfMetaData)serializer.Deserialize(reader);

            Assert.AreEqual(4, result.lstVolInfo.Count, 
                "59 Piano Solos should have 4 volumes");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TestBmkDeserialization_Full59PianoSolos_TocEntryCount()
        {
            var serializer = new XmlSerializer(typeof(SerializablePdfMetaData));
            using var reader = new StringReader(TestAssets.Sample59PianoSolosFullBmk);
            
            var result = (SerializablePdfMetaData)serializer.Deserialize(reader);

            Assert.AreEqual(59, result.lstTocEntries.Count, 
                "59 Piano Solos should have 59 TOC entries");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TestBmkDeserialization_Full59PianoSolos_TotalPages()
        {
            var serializer = new XmlSerializer(typeof(SerializablePdfMetaData));
            using var reader = new StringReader(TestAssets.Sample59PianoSolosFullBmk);
            
            var result = (SerializablePdfMetaData)serializer.Deserialize(reader);

            var totalPages = result.lstVolInfo.Sum(v => v.NPagesInThisVolume);
            Assert.AreEqual(157, totalPages, 
                "59 Piano Solos should have 157 total pages (1+84+10+62)");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TestBmkDeserialization_Full59PianoSolos_FirstAndLastSongs()
        {
            var serializer = new XmlSerializer(typeof(SerializablePdfMetaData));
            using var reader = new StringReader(TestAssets.Sample59PianoSolosFullBmk);
            
            var result = (SerializablePdfMetaData)serializer.Deserialize(reader);

            // First song
            Assert.AreEqual("Tango in D", result.lstTocEntries[0].SongName);
            Assert.AreEqual("ALBENIZ ISAAC", result.lstTocEntries[0].Composer);
            Assert.AreEqual(4, result.lstTocEntries[0].PageNo);

            // Last song
            var lastEntry = result.lstTocEntries[result.lstTocEntries.Count - 1];
            Assert.AreEqual("Tannhauser March", lastEntry.SongName);
            Assert.AreEqual("WAGNER, RICHARD", lastEntry.Composer);
            Assert.AreEqual(205, lastEntry.PageNo);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TestBmkDeserialization_Full59PianoSolos_SpecialCharactersInSongNames()
        {
            var serializer = new XmlSerializer(typeof(SerializablePdfMetaData));
            using var reader = new StringReader(TestAssets.Sample59PianoSolosFullBmk);
            
            var result = (SerializablePdfMetaData)serializer.Deserialize(reader);

            // Find songs with special characters (XML entities)
            var variationsEntry = result.lstTocEntries.FirstOrDefault(t => 
                t.SongName.Contains("Nel Cor Piu"));
            Assert.IsNotNull(variationsEntry, "Should find Beethoven's Variations");
            Assert.AreEqual("Variations on \"Nel Cor Piu non mi sento\"", variationsEntry.SongName);

            var minuteWaltz = result.lstTocEntries.FirstOrDefault(t => 
                t.SongName.Contains("Minute"));
            Assert.IsNotNull(minuteWaltz, "Should find Chopin's Minute Waltz");
            Assert.AreEqual("Waltz (\"Minute\") in Db. Op 64 NO 1.", minuteWaltz.SongName);
        }

        #endregion
    }
}
