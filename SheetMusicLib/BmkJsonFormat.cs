using System.Text.Json.Serialization;

namespace SheetMusicLib
{
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

        [JsonPropertyName("link")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Link { get; set; }
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
}
