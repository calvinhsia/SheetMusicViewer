using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Media;

namespace SheetMusicViewer
{
    #region Portable Ink Stroke Format Classes
    
    /// <summary>
    /// Portable ink stroke format for cross-platform compatibility
    /// </summary>
    public class PortableInkStroke
    {
        [JsonPropertyName("points")]
        public List<PortablePoint> Points { get; set; } = new();
        
        [JsonPropertyName("color")]
        public string Color { get; set; } = "#000000";
        
        [JsonPropertyName("thickness")]
        public double Thickness { get; set; } = 2.0;
        
        [JsonPropertyName("isHighlighter")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool IsHighlighter { get; set; }
        
        [JsonPropertyName("opacity")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double Opacity { get; set; } = 1.0;
    }

    public class PortablePoint
    {
        [JsonPropertyName("x")]
        public double X { get; set; }
        
        [JsonPropertyName("y")]
        public double Y { get; set; }
    }

    /// <summary>
    /// Portable ink stroke collection with dimension info for scaling
    /// </summary>
    public class PortableInkStrokeCollection
    {
        [JsonPropertyName("strokes")]
        public List<PortableInkStroke> Strokes { get; set; } = new();
        
        [JsonPropertyName("canvasWidth")]
        public double CanvasWidth { get; set; }
        
        [JsonPropertyName("canvasHeight")]
        public double CanvasHeight { get; set; }
    }
    
    #endregion

    #region BMK JSON Format Classes
    
    /// <summary>
    /// JSON-based BMK format for cross-platform compatibility
    /// Replaces XML serialization with JSON
    /// </summary>
    public class BmkJsonFormat
    {
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;
        
        [JsonPropertyName("lastWrite")]
        public DateTime LastWrite { get; set; }
        
        [JsonPropertyName("lastPageNo")]
        public int LastPageNo { get; set; }
        
        [JsonPropertyName("pageNumberOffset")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int PageNumberOffset { get; set; }
        
        [JsonPropertyName("notes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Notes { get; set; }
        
        [JsonPropertyName("volumes")]
        public List<JsonPdfVolumeInfo> Volumes { get; set; } = new();
        
        [JsonPropertyName("tableOfContents")]
        public List<JsonTOCEntry> TableOfContents { get; set; } = new();
        
        [JsonPropertyName("favorites")]
        public List<JsonFavorite> Favorites { get; set; } = new();
        
        [JsonPropertyName("inkStrokes")]
        public Dictionary<int, JsonInkStrokes> InkStrokes { get; set; } = new();
    }

    public class JsonPdfVolumeInfo
    {
        [JsonPropertyName("fileName")]
        public string FileName { get; set; }
        
        [JsonPropertyName("pageCount")]
        public int PageCount { get; set; }
        
        [JsonPropertyName("rotation")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int Rotation { get; set; }
    }

    public class JsonTOCEntry
    {
        [JsonPropertyName("songName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string SongName { get; set; }
        
        [JsonPropertyName("composer")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Composer { get; set; }
        
        [JsonPropertyName("date")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Date { get; set; }
        
        [JsonPropertyName("notes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Notes { get; set; }
        
        [JsonPropertyName("pageNo")]
        public int PageNo { get; set; }
    }

    public class JsonFavorite
    {
        [JsonPropertyName("pageNo")]
        public int PageNo { get; set; }
        
        [JsonPropertyName("name")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Name { get; set; }
    }

    public class JsonInkStrokes
    {
        [JsonPropertyName("pageNo")]
        public int PageNo { get; set; }
        
        [JsonPropertyName("canvasWidth")]
        public double CanvasWidth { get; set; }
        
        [JsonPropertyName("canvasHeight")]
        public double CanvasHeight { get; set; }
        
        [JsonPropertyName("strokes")]
        public List<PortableInkStroke> Strokes { get; set; } = new();
    }
    
    #endregion

    /// <summary>
    /// Unified converter for BMK format (XML to JSON) and Ink Strokes (ISF to JSON)
    /// Consolidates all conversion logic in one place
    /// </summary>
    public static class BmkJsonConverter
    {
        #region Ink Stroke Conversion (ISF ? JSON)
        
        /// <summary>
        /// Convert WPF StrokeCollection (ISF format) to portable JSON format
        /// </summary>
        private static PortableInkStrokeCollection ConvertWpfToPortable(byte[] isfData, Point canvasDimension)
        {
            if (isfData == null || isfData.Length == 0)
            {
                return null;
            }

            try
            {
                using var stream = new MemoryStream(isfData);
                var strokeCollection = new StrokeCollection(stream);
                
                var portable = new PortableInkStrokeCollection
                {
                    CanvasWidth = canvasDimension.X,
                    CanvasHeight = canvasDimension.Y
                };

                foreach (var stroke in strokeCollection)
                {
                    var portableStroke = new PortableInkStroke
                    {
                        Thickness = stroke.DrawingAttributes.Width,
                        IsHighlighter = stroke.DrawingAttributes.IsHighlighter,
                        Opacity = stroke.DrawingAttributes.Color.A / 255.0
                    };

                    // Convert color to #RRGGBB format
                    var color = stroke.DrawingAttributes.Color;
                    portableStroke.Color = $"#{color.R:X2}{color.G:X2}{color.B:X2}";

                    // Convert points
                    var points = stroke.StylusPoints;
                    foreach (var point in points)
                    {
                        portableStroke.Points.Add(new PortablePoint
                        {
                            X = point.X,
                            Y = point.Y
                        });
                    }

                    portable.Strokes.Add(portableStroke);
                }

                return portable;
            }
            catch (Exception)
            {
                // Invalid ISF data
                return null;
            }
        }

        /// <summary>
        /// Convert portable strokes back to WPF StrokeCollection (for compatibility)
        /// </summary>
        public static (byte[] isfData, Point dimension) ConvertPortableToWpf(PortableInkStrokeCollection portableStrokes)
        {
            if (portableStrokes == null || portableStrokes.Strokes.Count == 0)
            {
                return (null, new Point());
            }

            var strokeCollection = new StrokeCollection();

            foreach (var portableStroke in portableStrokes.Strokes)
            {
                // Convert points
                var points = new System.Windows.Input.StylusPointCollection();
                foreach (var pt in portableStroke.Points)
                {
                    points.Add(new System.Windows.Input.StylusPoint(pt.X, pt.Y));
                }

                var stroke = new Stroke(points);

                // Set drawing attributes
                var drawingAttributes = new DrawingAttributes
                {
                    Width = portableStroke.Thickness,
                    Height = portableStroke.Thickness,
                    IsHighlighter = portableStroke.IsHighlighter
                };

                // Parse color from #RRGGBB format
                if (!string.IsNullOrEmpty(portableStroke.Color) && portableStroke.Color.StartsWith("#"))
                {
                    try
                    {
                        var colorStr = portableStroke.Color.TrimStart('#');
                        var r = Convert.ToByte(colorStr.Substring(0, 2), 16);
                        var g = Convert.ToByte(colorStr.Substring(2, 2), 16);
                        var b = Convert.ToByte(colorStr.Substring(4, 2), 16);
                        var a = (byte)(portableStroke.Opacity * 255);
                        drawingAttributes.Color = Color.FromArgb(a, r, g, b);
                    }
                    catch
                    {
                        drawingAttributes.Color = Colors.Black;
                    }
                }

                stroke.DrawingAttributes = drawingAttributes;
                strokeCollection.Add(stroke);
            }

            // Save to ISF format
            using var stream = new MemoryStream();
            strokeCollection.Save(stream, compress: true);
            var dimension = new Point(portableStrokes.CanvasWidth, portableStrokes.CanvasHeight);
            return (stream.ToArray(), dimension);
        }

        /// <summary>
        /// Serialize portable ink strokes to JSON
        /// </summary>
        private static string SerializeInkToJson(PortableInkStrokeCollection portableStrokes)
        {
            if (portableStrokes == null)
            {
                return null;
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            return JsonSerializer.Serialize(portableStrokes, options);
        }

        /// <summary>
        /// Deserialize JSON to portable ink strokes
        /// </summary>
        private static PortableInkStrokeCollection DeserializeInkFromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true // Allow case variations
            };

            return JsonSerializer.Deserialize<PortableInkStrokeCollection>(json, options);
        }

        /// <summary>
        /// Convert all ink strokes in a PdfMetaData from ISF to JSON
        /// Returns the number of pages with ink that were converted
        /// </summary>
        public static int ConvertPdfMetadataInkToJson(PdfMetaData metadata)
        {
            int convertedCount = 0;
            var updatedInkStrokes = new Dictionary<int, InkStrokeClass>();

            foreach (var kvp in metadata.dictInkStrokes)
            {
                var pageNo = kvp.Key;
                var inkStroke = kvp.Value;

                // Check if already in JSON format (starts with '{')
                if (inkStroke.StrokeData != null && inkStroke.StrokeData.Length > 0)
                {
                    var firstByte = inkStroke.StrokeData[0];
                    if (firstByte == '{') // Already JSON
                    {
                        updatedInkStrokes[pageNo] = inkStroke;
                        continue;
                    }

                    // Convert from ISF to portable format
                    var portable = ConvertWpfToPortable(inkStroke.StrokeData, inkStroke.InkStrokeDimension);
                    if (portable != null)
                    {
                        var json = SerializeInkToJson(portable);
                        if (!string.IsNullOrEmpty(json))
                        {
                            var newInkStroke = new InkStrokeClass
                            {
                                Pageno = pageNo,
                                InkStrokeDimension = inkStroke.InkStrokeDimension,
                                StrokeData = System.Text.Encoding.UTF8.GetBytes(json)
                            };
                            updatedInkStrokes[pageNo] = newInkStroke;
                            convertedCount++;
                        }
                    }
                }
            }

            // Update the dictionary
            if (convertedCount > 0)
            {
                metadata.dictInkStrokes.Clear();
                foreach (var kvp in updatedInkStrokes)
                {
                    metadata.dictInkStrokes[kvp.Key] = kvp.Value;
                }
                metadata.IsDirty = true;
            }

            return convertedCount;
        }
        
        #endregion

        #region BMK Conversion (XML ? JSON)
        
        /// <summary>
        /// Convert PdfMetaData (XML format) to JSON format
        /// </summary>
        public static BmkJsonFormat ConvertToJson(PdfMetaData metadata)
        {
            var json = new BmkJsonFormat
            {
                LastWrite = metadata.dtLastWrite,
                LastPageNo = metadata.LastPageNo,
                PageNumberOffset = metadata.PageNumberOffset,
                Notes = metadata.Notes
            };

            // Convert volumes
            foreach (var vol in metadata.lstVolInfo)
            {
                json.Volumes.Add(new JsonPdfVolumeInfo
                {
                    FileName = vol.FileNameVolume,
                    PageCount = vol.NPagesInThisVolume,
                    Rotation = vol.Rotation
                });
            }

            // Convert TOC
            foreach (var toc in metadata.lstTocEntries)
            {
                json.TableOfContents.Add(new JsonTOCEntry
                {
                    SongName = toc.SongName,
                    Composer = toc.Composer,
                    Date = toc.Date,
                    Notes = toc.Notes,
                    PageNo = toc.PageNo
                });
            }

            // Convert favorites
            foreach (var fav in metadata.dictFav.Values)
            {
                json.Favorites.Add(new JsonFavorite
                {
                    PageNo = fav.Pageno,
                    Name = fav.FavoriteName
                });
            }

            // Convert ink strokes
            foreach (var kvp in metadata.dictInkStrokes)
            {
                var pageNo = kvp.Key;
                var inkStroke = kvp.Value;

                // Check if already in JSON format or ISF
                PortableInkStrokeCollection portableStrokes = null;
                
                if (inkStroke.StrokeData != null && inkStroke.StrokeData.Length > 0)
                {
                    var firstByte = inkStroke.StrokeData[0];
                    if (firstByte == '{') // Already JSON
                    {
                        var jsonText = System.Text.Encoding.UTF8.GetString(inkStroke.StrokeData);
                        portableStrokes = DeserializeInkFromJson(jsonText);
                    }
                    else // ISF format - convert it
                    {
                        portableStrokes = ConvertWpfToPortable(
                            inkStroke.StrokeData, 
                            inkStroke.InkStrokeDimension);
                    }

                    if (portableStrokes != null)
                    {
                        json.InkStrokes[pageNo] = new JsonInkStrokes
                        {
                            PageNo = pageNo,
                            CanvasWidth = portableStrokes.CanvasWidth,
                            CanvasHeight = portableStrokes.CanvasHeight,
                            Strokes = portableStrokes.Strokes
                        };
                    }
                }
            }

            return json;
        }

        /// <summary>
        /// Convert JSON format back to PdfMetaData (for loading)
        /// </summary>
        public static PdfMetaData ConvertFromJson(BmkJsonFormat json, string fullPathFile, bool isSinglesFolder)
        {
            var metadata = new PdfMetaData
            {
                _FullPathFile = fullPathFile,
                IsSinglesFolder = isSinglesFolder,
                dtLastWrite = json.LastWrite,
                LastPageNo = json.LastPageNo,
                PageNumberOffset = json.PageNumberOffset,
                Notes = json.Notes
            };

            // Convert volumes
            foreach (var vol in json.Volumes)
            {
                metadata.lstVolInfo.Add(new PdfVolumeInfo
                {
                    FileNameVolume = vol.FileName,
                    NPagesInThisVolume = vol.PageCount,
                    Rotation = vol.Rotation
                });
            }

            // Convert TOC
            foreach (var toc in json.TableOfContents)
            {
                metadata.lstTocEntries.Add(new TOCEntry
                {
                    SongName = toc.SongName,
                    Composer = toc.Composer,
                    Date = toc.Date,
                    Notes = toc.Notes,
                    PageNo = toc.PageNo
                });
            }

            // Initialize TOC dictionary
            metadata.InitializeDictToc(metadata.lstTocEntries);

            // Convert favorites - populate both lists
            foreach (var fav in json.Favorites)
            {
                var favorite = new Favorite
                {
                    Pageno = fav.PageNo,
                    FavoriteName = fav.Name
                };
                metadata.Favorites.Add(favorite); // Add to list for serialization
                metadata.dictFav[fav.PageNo] = favorite; // Add to dict for runtime use
            }

            // Convert ink strokes - keep as portable JSON format
            foreach (var kvp in json.InkStrokes)
            {
                var portableStrokes = new PortableInkStrokeCollection
                {
                    CanvasWidth = kvp.Value.CanvasWidth,
                    CanvasHeight = kvp.Value.CanvasHeight,
                    Strokes = kvp.Value.Strokes
                };

                var jsonText = SerializeInkToJson(portableStrokes);
                var inkStroke = new InkStrokeClass
                {
                    Pageno = kvp.Key,
                    InkStrokeDimension = new Point(kvp.Value.CanvasWidth, kvp.Value.CanvasHeight),
                    StrokeData = System.Text.Encoding.UTF8.GetBytes(jsonText)
                };
                metadata.LstInkStrokes.Add(inkStroke); // Add to list for serialization
                metadata.dictInkStrokes[kvp.Key] = inkStroke; // Add to dict for runtime use
            }

            return metadata;
        }

        #endregion

        #region Serialization

        /// <summary>
        /// Serialize to JSON string
        /// </summary>
        public static string SerializeToJson(BmkJsonFormat bmkData)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            return JsonSerializer.Serialize(bmkData, options);
        }

        /// <summary>
        /// Deserialize from JSON string
        /// </summary>
        public static BmkJsonFormat DeserializeFromJson(string json)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true // Allow case variations
            };

            return JsonSerializer.Deserialize<BmkJsonFormat>(json, options);
        }

        #endregion

        #region File Operations

        /// <summary>
        /// Save BMK file in JSON format
        /// </summary>
        public static void SaveAsJson(PdfMetaData metadata, string bmkFilePath)
        {
            var jsonData = ConvertToJson(metadata);
            var jsonText = SerializeToJson(jsonData);
            File.WriteAllText(bmkFilePath, jsonText);
        }

        /// <summary>
        /// Load BMK file from JSON format
        /// </summary>
        public static PdfMetaData LoadFromJson(string bmkFilePath, string pdfFilePath, bool isSinglesFolder)
        {
            var jsonText = File.ReadAllText(bmkFilePath);
            var jsonData = DeserializeFromJson(jsonText);
            return ConvertFromJson(jsonData, pdfFilePath, isSinglesFolder);
        }

        /// <summary>
        /// Check if a BMK file is in JSON format (vs XML)
        /// </summary>
        public static bool IsJsonFormat(string bmkFilePath)
        {
            if (!File.Exists(bmkFilePath))
                return false;

            try
            {
                using var reader = new StreamReader(bmkFilePath);
                var firstChar = (char)reader.Read();
                return firstChar == '{';
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Convert all BMK files in a list to JSON format
        /// Returns (total count, converted count)
        /// </summary>
        public static (int total, int converted) ConvertAllBmksToJson(List<PdfMetaData> metadataList)
        {
            int total = 0;
            int converted = 0;

            foreach (var metadata in metadataList)
            {
                total++;
                try
                {
                    var bmkPath = metadata.PdfBmkMetadataFileName;
                    if (File.Exists(bmkPath) && !IsJsonFormat(bmkPath))
                    {
                        // Create backup of original XML file
                        var backupPath = bmkPath + ".xml.backup";
                        if (!File.Exists(backupPath))
                        {
                            File.Copy(bmkPath, backupPath);
                        }

                        // Convert and save as JSON
                        SaveAsJson(metadata, bmkPath);
                        converted++;
                    }
                }
                catch (Exception)
                {
                    // Continue with other files
                }
            }

            return (total, converted);
        }

        #endregion
    }
}
