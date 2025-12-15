using System.Text.Json;
using System.Text.Json.Serialization;

namespace SheetMusicLib
{
    /// <summary>
    /// JSON serialization utilities for BMK format and ink strokes (platform-independent)
    /// </summary>
    public static class BmkJsonSerializer
    {
        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private static readonly JsonSerializerOptions ReadOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        #region BMK Format Serialization

        /// <summary>
        /// Serialize BMK data to JSON string
        /// </summary>
        public static string Serialize(BmkJsonFormat bmkData)
        {
            return JsonSerializer.Serialize(bmkData, WriteOptions);
        }

        /// <summary>
        /// Deserialize JSON string to BMK data
        /// </summary>
        public static BmkJsonFormat Deserialize(string json)
        {
            return JsonSerializer.Deserialize<BmkJsonFormat>(json, ReadOptions);
        }

        /// <summary>
        /// Save BMK data to JSON file
        /// </summary>
        public static void SaveToFile(BmkJsonFormat bmkData, string filePath)
        {
            var json = Serialize(bmkData);
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Load BMK data from JSON file
        /// </summary>
        public static BmkJsonFormat LoadFromFile(string filePath)
        {
            var json = File.ReadAllText(filePath);
            return Deserialize(json);
        }

        #endregion

        #region Ink Stroke Serialization

        /// <summary>
        /// Serialize portable ink strokes to JSON string
        /// </summary>
        public static string SerializeInk(PortableInkStrokeCollection portableStrokes)
        {
            if (portableStrokes == null)
                return null;

            return JsonSerializer.Serialize(portableStrokes, WriteOptions);
        }

        /// <summary>
        /// Deserialize JSON string to portable ink strokes
        /// </summary>
        public static PortableInkStrokeCollection DeserializeInk(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            return JsonSerializer.Deserialize<PortableInkStrokeCollection>(json, ReadOptions);
        }

        #endregion

        #region Format Detection

        /// <summary>
        /// Check if byte array contains JSON (starts with '{')
        /// </summary>
        public static bool IsJsonFormat(byte[] data)
        {
            return data != null && data.Length > 0 && data[0] == '{';
        }

        /// <summary>
        /// Check if a file is in JSON format (vs XML)
        /// </summary>
        public static bool IsJsonFormat(string filePath)
        {
            if (!File.Exists(filePath))
                return false;

            try
            {
                using var reader = new StreamReader(filePath);
                var firstChar = (char)reader.Read();
                return firstChar == '{';
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}
