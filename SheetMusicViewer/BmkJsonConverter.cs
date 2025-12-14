using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Media;
using SheetMusicLib;

namespace SheetMusicViewer
{
    /// <summary>
    /// WPF-specific converter for BMK format (XML to JSON) and Ink Strokes (ISF to JSON).
    /// Uses portable types from SheetMusicLib for cross-platform data.
    /// </summary>
    public static class BmkJsonConverter
    {
        #region Ink Stroke Conversion (ISF ? Portable JSON)

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
                        portableStroke.Points.Add(new PortableInkPoint
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
                    if (BmkJsonSerializer.IsJsonFormat(inkStroke.StrokeData))
                    {
                        updatedInkStrokes[pageNo] = inkStroke;
                        continue;
                    }

                    // Convert from ISF to portable format
                    var wpfDimension = new Point(inkStroke.InkStrokeDimension.X, inkStroke.InkStrokeDimension.Y);
                    var portable = ConvertWpfToPortable(inkStroke.StrokeData, wpfDimension);
                    if (portable != null)
                    {
                        var json = BmkJsonSerializer.SerializeInk(portable);
                        if (!string.IsNullOrEmpty(json))
                        {
                            var newInkStroke = new InkStrokeClass
                            {
                                Pageno = pageNo,
                                InkStrokeDimension = inkStroke.InkStrokeDimension,
                                StrokeData = Encoding.UTF8.GetBytes(json)
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
                    if (BmkJsonSerializer.IsJsonFormat(inkStroke.StrokeData))
                    {
                        var jsonText = Encoding.UTF8.GetString(inkStroke.StrokeData);
                        portableStrokes = BmkJsonSerializer.DeserializeInk(jsonText);
                    }
                    else // ISF format - convert it
                    {
                        var wpfDimension = new Point(inkStroke.InkStrokeDimension.X, inkStroke.InkStrokeDimension.Y);
                        portableStrokes = ConvertWpfToPortable(inkStroke.StrokeData, wpfDimension);
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
                metadata.Favorites.Add(favorite);
                metadata.dictFav[fav.PageNo] = favorite;
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

                var jsonText = BmkJsonSerializer.SerializeInk(portableStrokes);
                var inkStroke = new InkStrokeClass
                {
                    Pageno = kvp.Key,
                    InkStrokeDimension = new PortablePoint(kvp.Value.CanvasWidth, kvp.Value.CanvasHeight),
                    StrokeData = Encoding.UTF8.GetBytes(jsonText)
                };
                metadata.LstInkStrokes.Add(inkStroke);
                metadata.dictInkStrokes[kvp.Key] = inkStroke;
            }

            return metadata;
        }

        #endregion

        #region Serialization Helpers

        /// <summary>
        /// Serialize to JSON string (wrapper for BmkJsonSerializer)
        /// </summary>
        public static string SerializeToJson(BmkJsonFormat bmkData)
        {
            return BmkJsonSerializer.Serialize(bmkData);
        }

        /// <summary>
        /// Deserialize from JSON string (wrapper for BmkJsonSerializer)
        /// </summary>
        public static BmkJsonFormat DeserializeFromJson(string json)
        {
            return BmkJsonSerializer.Deserialize(json);
        }

        #endregion

        #region File Operations

        /// <summary>
        /// Save BMK file in JSON format
        /// </summary>
        public static void SaveAsJson(PdfMetaData metadata, string bmkFilePath)
        {
            var jsonData = ConvertToJson(metadata);
            BmkJsonSerializer.SaveToFile(jsonData, bmkFilePath);
        }

        /// <summary>
        /// Load BMK file from JSON format
        /// </summary>
        public static PdfMetaData LoadFromJson(string bmkFilePath, string pdfFilePath, bool isSinglesFolder)
        {
            var jsonData = BmkJsonSerializer.LoadFromFile(bmkFilePath);
            return ConvertFromJson(jsonData, pdfFilePath, isSinglesFolder);
        }

        /// <summary>
        /// Check if a BMK file is in JSON format (vs XML)
        /// </summary>
        public static bool IsJsonFormat(string bmkFilePath)
        {
            return BmkJsonSerializer.IsJsonFormat(bmkFilePath);
        }

        /// <summary>
        /// Convert all BMK files in a list to JSON format with .json extension
        /// Overwrites any existing JSON files to ensure correct format with portable ink strokes
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
                    var jsonPath = Path.ChangeExtension(bmkPath, ".json");
                    
                    // Always convert - overwrite any existing JSON (it might be incorrectly formatted)
                    SaveAsJson(metadata, jsonPath);
                    converted++;
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
