using System.Text.Json.Serialization;

namespace SheetMusicLib
{
    /// <summary>
    /// Portable ink stroke format for cross-platform compatibility
    /// </summary>
    public class PortableInkStroke
    {
        [JsonPropertyName("points")]
        public List<PortableInkPoint> Points { get; set; } = new();

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

    /// <summary>
    /// Point within an ink stroke (separate from PortablePoint for JSON serialization clarity)
    /// </summary>
    public class PortableInkPoint
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
}
