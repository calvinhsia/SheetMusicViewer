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
        /// <summary>
        /// Sample BMK file content from a real multi-volume PDF set.
        /// This is based on "59 Piano Solos You Like To Play.bmk" which has 4 volumes.
        /// </summary>
        private const string SampleMultiVolumeBmk = @"<?xml version=""1.0"" encoding=""utf-8""?>
<PdfMetaData xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"">
 <lstVolInfo>
  <PdfVolumeInfo>
   <NPages>1</NPages>
   <Rotation>0</Rotation>
   <FileName>59 Piano Solos You Like To Play.pdf</FileName>
  </PdfVolumeInfo>
  <PdfVolumeInfo>
   <NPages>84</NPages>
   <Rotation>2</Rotation>
   <FileName>59 Piano Solos You Like to Play1.pdf</FileName>
  </PdfVolumeInfo>
  <PdfVolumeInfo>
   <NPages>10</NPages>
   <Rotation>2</Rotation>
   <FileName>59 Piano Solos You Like to Play2.pdf</FileName>
  </PdfVolumeInfo>
  <PdfVolumeInfo>
   <NPages>62</NPages>
   <Rotation>2</Rotation>
   <FileName>59 Piano Solos You Like to Play3.pdf</FileName>
  </PdfVolumeInfo>
 </lstVolInfo>
 <LastPageNo>27</LastPageNo>
 <dtLastWrite>2024-04-27T15:40:35.066011-07:00</dtLastWrite>
 <PageNumberOffset>0</PageNumberOffset>
 <LstInkStrokes />
 <Favorites />
 <lstTocEntries>
  <TOCEntry>
   <SongName>Tango in D</SongName>
   <Composer>ALBENIZ ISAAC</Composer>
   <PageNo>4</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Solfeggietto</SongName>
   <Composer>BACH C P E</Composer>
   <PageNo>6</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Prelude in C (Well -Tempered Clavier, Book 1)</SongName>
   <Composer>BACH, JS</Composer>
   <PageNo>10</PageNo>
  </TOCEntry>
 </lstTocEntries>
</PdfMetaData>";

        /// <summary>
        /// Sample BMK file with a single volume
        /// </summary>
        private const string SampleSingleVolumeBmk = @"<?xml version=""1.0"" encoding=""utf-8""?>
<PdfMetaData xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"">
 <lstVolInfo>
  <PdfVolumeInfo>
   <NPages>5</NPages>
   <Rotation>0</Rotation>
   <FileName>SheetMusicExcerpts.pdf</FileName>
  </PdfVolumeInfo>
 </lstVolInfo>
 <LastPageNo>1</LastPageNo>
 <dtLastWrite>2025-08-28T19:47:41.1699803-07:00</dtLastWrite>
 <PageNumberOffset>0</PageNumberOffset>
 <LstInkStrokes />
 <Favorites />
 <lstTocEntries>
  <TOCEntry>
   <SongName>SheetMusicExcerpts</SongName>
   <PageNo>0</PageNo>
  </TOCEntry>
 </lstTocEntries>
</PdfMetaData>";

        #region Core Deserialization Tests

        [TestMethod]
        [TestCategory("Unit")]
        public void TestBmkDeserialization_VolumeInfoListIsNotEmpty()
        {
            // This is the key test - without [XmlArrayItem("PdfVolumeInfo")],
            // lstVolInfo would be empty because the serializer expects <PdfVolumeInfoBase>
            var serializer = new XmlSerializer(typeof(SerializablePdfMetaData));
            using var reader = new StringReader(SampleMultiVolumeBmk);
            
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
            using var reader = new StringReader(SampleMultiVolumeBmk);
            
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
            using var reader = new StringReader(SampleMultiVolumeBmk);
            
            var result = (SerializablePdfMetaData)serializer.Deserialize(reader);

            var totalPages = result.lstVolInfo.Sum(v => v.NPagesInThisVolume);
            Assert.AreEqual(1 + 84 + 10 + 62, totalPages, "Total pages should be 157");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TestBmkDeserialization_SingleVolume()
        {
            var serializer = new XmlSerializer(typeof(SerializablePdfMetaData));
            using var reader = new StringReader(SampleSingleVolumeBmk);
            
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
            using var reader = new StringReader(SampleMultiVolumeBmk);
            
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
            using var reader = new StringReader(SampleMultiVolumeBmk);
            
            var result = (SerializablePdfMetaData)serializer.Deserialize(reader);

            Assert.AreEqual(27, result.LastPageNo);
            Assert.AreEqual(0, result.PageNumberOffset);
            Assert.AreEqual(new DateTime(2024, 4, 27, 15, 40, 35, 66, DateTimeKind.Unspecified), 
                result.dtLastWrite.Date.AddHours(result.dtLastWrite.Hour)
                    .AddMinutes(result.dtLastWrite.Minute)
                    .AddSeconds(result.dtLastWrite.Second)
                    .AddMilliseconds(result.dtLastWrite.Millisecond));
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
            const string bmkWithEmptyVolumes = @"<?xml version=""1.0"" encoding=""utf-8""?>
<PdfMetaData xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"">
 <lstVolInfo />
 <LastPageNo>0</LastPageNo>
 <PageNumberOffset>0</PageNumberOffset>
 <LstInkStrokes />
 <Favorites />
 <lstTocEntries />
</PdfMetaData>";

            var serializer = new XmlSerializer(typeof(SerializablePdfMetaData));
            using var reader = new StringReader(bmkWithEmptyVolumes);
            
            var result = (SerializablePdfMetaData)serializer.Deserialize(reader);

            Assert.IsNotNull(result.lstVolInfo);
            Assert.AreEqual(0, result.lstVolInfo.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TestBmkDeserialization_MissingVolumeList()
        {
            const string bmkWithNoVolumes = @"<?xml version=""1.0"" encoding=""utf-8""?>
<PdfMetaData xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"">
 <LastPageNo>0</LastPageNo>
 <PageNumberOffset>0</PageNumberOffset>
</PdfMetaData>";

            var serializer = new XmlSerializer(typeof(SerializablePdfMetaData));
            using var reader = new StringReader(bmkWithNoVolumes);
            
            var result = (SerializablePdfMetaData)serializer.Deserialize(reader);

            // lstVolInfo is initialized in the class, so it should be empty but not null
            Assert.IsNotNull(result.lstVolInfo);
            Assert.AreEqual(0, result.lstVolInfo.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TestBmkDeserialization_WithFavorites()
        {
            const string bmkWithFavorites = @"<?xml version=""1.0"" encoding=""utf-8""?>
<PdfMetaData xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"">
 <lstVolInfo>
  <PdfVolumeInfo>
   <NPages>100</NPages>
   <Rotation>0</Rotation>
   <FileName>TestBook.pdf</FileName>
  </PdfVolumeInfo>
 </lstVolInfo>
 <LastPageNo>15</LastPageNo>
 <PageNumberOffset>0</PageNumberOffset>
 <LstInkStrokes />
 <Favorites>
  <Favorite>
   <Pageno>10</Pageno>
   <FavoriteName>My Favorite Page</FavoriteName>
  </Favorite>
  <Favorite>
   <Pageno>25</Pageno>
   <FavoriteName>Another Favorite</FavoriteName>
  </Favorite>
 </Favorites>
 <lstTocEntries />
</PdfMetaData>";

            var serializer = new XmlSerializer(typeof(SerializablePdfMetaData));
            using var reader = new StringReader(bmkWithFavorites);
            
            var result = (SerializablePdfMetaData)serializer.Deserialize(reader);

            Assert.AreEqual(2, result.Favorites.Count);
            Assert.AreEqual(10, result.Favorites[0].Pageno);
            Assert.AreEqual("My Favorite Page", result.Favorites[0].FavoriteName);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TestBmkDeserialization_WithInkStrokes()
        {
            const string bmkWithInk = @"<?xml version=""1.0"" encoding=""utf-8""?>
<PdfMetaData xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"">
 <lstVolInfo>
  <PdfVolumeInfo>
   <NPages>50</NPages>
   <Rotation>0</Rotation>
   <FileName>InkBook.pdf</FileName>
  </PdfVolumeInfo>
 </lstVolInfo>
 <LastPageNo>5</LastPageNo>
 <PageNumberOffset>0</PageNumberOffset>
 <LstInkStrokes>
  <InkStrokeClass>
   <Pageno>5</Pageno>
   <StrokeData>AQIDBA==</StrokeData>
  </InkStrokeClass>
 </LstInkStrokes>
 <Favorites />
 <lstTocEntries />
</PdfMetaData>";

            var serializer = new XmlSerializer(typeof(SerializablePdfMetaData));
            using var reader = new StringReader(bmkWithInk);
            
            var result = (SerializablePdfMetaData)serializer.Deserialize(reader);

            Assert.AreEqual(1, result.LstInkStrokes.Count);
            Assert.AreEqual(5, result.LstInkStrokes[0].Pageno);
            // AQIDBA== is base64 for [1,2,3,4]
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, result.LstInkStrokes[0].StrokeData);
        }

        #endregion

        #region Full BMK File Sample Tests

        /// <summary>
        /// Full sample from "59 Piano Solos You Like To Play.bmk" with all 59 TOC entries
        /// </summary>
        private const string FullSample59PianoSolosBmk = @"<?xml version=""1.0"" encoding=""utf-8""?>
<PdfMetaData xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"">
 <lstVolInfo>
  <PdfVolumeInfo>
   <NPages>1</NPages>
   <Rotation>0</Rotation>
   <FileName>59 Piano Solos You Like To Play.pdf</FileName>
  </PdfVolumeInfo>
  <PdfVolumeInfo>
   <NPages>84</NPages>
   <Rotation>2</Rotation>
   <FileName>59 Piano Solos You Like to Play1.pdf</FileName>
  </PdfVolumeInfo>
  <PdfVolumeInfo>
   <NPages>10</NPages>
   <Rotation>2</Rotation>
   <FileName>59 Piano Solos You Like to Play2.pdf</FileName>
  </PdfVolumeInfo>
  <PdfVolumeInfo>
   <NPages>62</NPages>
   <Rotation>2</Rotation>
   <FileName>59 Piano Solos You Like to Play3.pdf</FileName>
  </PdfVolumeInfo>
 </lstVolInfo>
 <LastPageNo>27</LastPageNo>
 <dtLastWrite>2024-04-27T15:40:35.066011-07:00</dtLastWrite>
 <PageNumberOffset>0</PageNumberOffset>
 <LstInkStrokes />
 <Favorites />
 <lstTocEntries>
  <TOCEntry>
   <SongName>Tango in D</SongName>
   <Composer>ALBENIZ ISAAC</Composer>
   <PageNo>4</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Solfeggietto</SongName>
   <Composer>BACH C P E</Composer>
   <PageNo>6</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Prelude in C (Well -Tempered Clavier, Book 1)</SongName>
   <Composer>BACH, JS</Composer>
   <PageNo>10</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Prelude in Bb minor (Well-Tempered Clavier Book 1)</SongName>
   <Composer>BACH, JS</Composer>
   <PageNo>12</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Variations on &quot;Nel Cor Piu non mi sento&quot;</SongName>
   <Composer>BEETHOVEN, LUDWIG VAN</Composer>
   <PageNo>14</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Minuet in G</SongName>
   <Composer>BEETHOVEN, LUDWIG VAN</Composer>
   <PageNo>22</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Hungarian Dance No 5</SongName>
   <Composer>BRAHMS, JOHANNES</Composer>
   <PageNo>24</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Waltz in A flat</SongName>
   <Composer>BRAHMS, JOHANNES</Composer>
   <PageNo>28</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Scarf Dance</SongName>
   <Composer>CHAMINADE, CÉCILE</Composer>
   <PageNo>30</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Prelude in A Op 28 No 7</SongName>
   <Composer>CHOPIN, Frederic</Composer>
   <PageNo>33</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Prelude in C Minor, 28, NO 20</SongName>
   <Composer>CHOPIN, Frederic</Composer>
   <PageNo>33</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Waltz (&quot;Minute&quot;) in Db. Op 64 NO 1.</SongName>
   <Composer>CHOPIN, Frederic</Composer>
   <PageNo>34</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Mazurka inBb, Op. 7, No. 1</SongName>
   <Composer>CHOPIN, Frederic</Composer>
   <PageNo>38</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Polonaise (&quot;Military&quot;) in A Op 40 No i</SongName>
   <Composer>CHOPIN, Frederic</Composer>
   <PageNo>40</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Orientale</SongName>
   <Composer>CUI, CÉSAR</Composer>
   <PageNo>47</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Humoreske</SongName>
   <Composer>DVORÅK ANTONIN,</Composer>
   <PageNo>50</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Love's Greeting (Salut d'amour)</SongName>
   <Composer>ELGAR SIR EDWARD</Composer>
   <PageNo>54</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Poem</SongName>
   <Composer>FIBICH ZDENKO</Composer>
   <PageNo>58</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Chanson</SongName>
   <Composer>FRIML, Rudolph</Composer>
   <PageNo>60</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Spanish Dance (Playera)</SongName>
   <Composer>GRANADOS, ENRIQUE</Composer>
   <PageNo>63</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Album-Leaf</SongName>
   <Composer>GRIEG, EDVARD</Composer>
   <PageNo>67</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Anitra's Dance</SongName>
   <Composer>GRIEG, EDVARD</Composer>
   <PageNo>68</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>To Spring</SongName>
   <Composer>GRIEG, EDVARD</Composer>
   <PageNo>71</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>March of the Dwarfs</SongName>
   <Composer>GRIEG, EDVARD</Composer>
   <PageNo>76</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Largo</SongName>
   <Composer>HANDEL G F</Composer>
   <PageNo>82</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Crescendo</SongName>
   <Composer>LASSON, PER</Composer>
   <PageNo>84</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Consolation No S, in E</SongName>
   <Composer>LISZT FRANZ</Composer>
   <PageNo>87</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Liebestraum NO 3 in Ab</SongName>
   <Composer>LISZT FRANZ</Composer>
   <PageNo>90</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Intermezzo from &quot;Cavalleria rusticana&quot;</SongName>
   <Composer>MASCAGNI, PIETRO</Composer>
   <PageNo>96</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Melodie (Elegie)</SongName>
   <Composer>MASSENET JULES</Composer>
   <PageNo>98</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Spring Song</SongName>
   <Composer>MENDELSSOHN, FELIX</Composer>
   <PageNo>100</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Venetian boat Song, No. 1, in G Minor</SongName>
   <Composer>MENDELSSOHN, FELIX</Composer>
   <PageNo>104</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Conolation</SongName>
   <Composer>MENDELSSOHN, FELIX</Composer>
   <PageNo>106</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Serenata</SongName>
   <Composer>MOSZXOWSKI, MORITZ</Composer>
   <PageNo>107</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Rondo (alla turca)</SongName>
   <Composer>Mozart, W. A.</Composer>
   <PageNo>110</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Barcarolle from &quot;Les Contes d'Hoffmann)</SongName>
   <Composer>OFFENBACH, JACQUES</Composer>
   <PageNo>114</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>May Night</SongName>
   <Composer>PALMGREN, SELIM</Composer>
   <PageNo>116</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Prelude in C#Minor</SongName>
   <Composer>RACHMANINOFF, SERGEI</Composer>
   <PageNo>119</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Prelude in G Minor</SongName>
   <Composer>RACHMANINOFF, SERGEI</Composer>
   <PageNo>124</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Song of India, from &quot;Sadko&quot;</SongName>
   <Composer>RIMSKY-KORSAKOFF</Composer>
   <PageNo>130</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Kamennoi Ostrow</SongName>
   <Composer>RUBINSTEIN, ANTON</Composer>
   <PageNo>134</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Melodie (in F)</SongName>
   <Composer>RUBINSTEIN, ANTON</Composer>
   <PageNo>142</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>The Swan</SongName>
   <Composer>SAINT-SAENS, CAMILLE</Composer>
   <PageNo>146</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Polish Dance in Eb Minor</SongName>
   <Composer>SHARWENKA, XAVER</Composer>
   <PageNo>149</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Military March in D Op 51 NO 1</SongName>
   <Composer>SHUBERT, FRANZ</Composer>
   <PageNo>153</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Moment Musical NO 3 in F Minor</SongName>
   <Composer>SHUBERT, FRANZ</Composer>
   <PageNo>156</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Romance in F# Op. 28 No.2</SongName>
   <Composer>SHUMANN, ROBERT</Composer>
   <PageNo>158</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Traumerei</SongName>
   <Composer>SHUMANN, ROBERT</Composer>
   <PageNo>160</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Romance</SongName>
   <Composer>SIBELIUS, JEAN</Composer>
   <PageNo>161</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Rustles of Spring</SongName>
   <Composer>SINDING CHRISTIAN</Composer>
   <PageNo>166</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>On the Beautiful Blue Danube</SongName>
   <Composer>STRAUSS, JOHANN (Jr.)</Composer>
   <PageNo>174</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>None but the lonely heart (Nur wer die Sehnsucht kennt)</SongName>
   <Composer>TCHAIKOVSKY P I</Composer>
   <PageNo>181</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Andante Cantabile, from the Quartet, Op 11</SongName>
   <Composer>TCHAIKOVSKY P I</Composer>
   <PageNo>184</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Song without words</SongName>
   <Composer>TCHAIKOVSKY P I</Composer>
   <PageNo>190</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Under The Leaves</SongName>
   <Composer>THOME, F</Composer>
   <PageNo>193</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Y como le vå?</SongName>
   <Composer>VALVERDE, J</Composer>
   <PageNo>197</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>March from&quot;Aida&quot;</SongName>
   <Composer>VERDI, GIUSEPPE</Composer>
   <PageNo>201</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Bridal Song, from &quot;Lohengrin&quot;</SongName>
   <Composer>WAGNER, RICHARD</Composer>
   <PageNo>203</PageNo>
  </TOCEntry>
  <TOCEntry>
   <SongName>Tannhauser March</SongName>
   <Composer>WAGNER, RICHARD</Composer>
   <PageNo>205</PageNo>
  </TOCEntry>
 </lstTocEntries>
</PdfMetaData>";

        [TestMethod]
        [TestCategory("Unit")]
        public void TestBmkDeserialization_Full59PianoSolos_VolumeCount()
        {
            var serializer = new XmlSerializer(typeof(SerializablePdfMetaData));
            using var reader = new StringReader(FullSample59PianoSolosBmk);
            
            var result = (SerializablePdfMetaData)serializer.Deserialize(reader);

            Assert.AreEqual(4, result.lstVolInfo.Count, 
                "59 Piano Solos should have 4 volumes");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TestBmkDeserialization_Full59PianoSolos_TocEntryCount()
        {
            var serializer = new XmlSerializer(typeof(SerializablePdfMetaData));
            using var reader = new StringReader(FullSample59PianoSolosBmk);
            
            var result = (SerializablePdfMetaData)serializer.Deserialize(reader);

            Assert.AreEqual(59, result.lstTocEntries.Count, 
                "59 Piano Solos should have 59 TOC entries");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TestBmkDeserialization_Full59PianoSolos_TotalPages()
        {
            var serializer = new XmlSerializer(typeof(SerializablePdfMetaData));
            using var reader = new StringReader(FullSample59PianoSolosBmk);
            
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
            using var reader = new StringReader(FullSample59PianoSolosBmk);
            
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
            using var reader = new StringReader(FullSample59PianoSolosBmk);
            
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
