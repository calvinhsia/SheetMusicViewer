using System.Text.Json.Serialization;

namespace SheetMusicLib
{
    /// <summary>
    /// Portable 2D point structure for cross-platform compatibility (replaces System.Windows.Point)
    /// </summary>
    public struct PortablePoint
    {
        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }

        public PortablePoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        public override readonly string ToString() => $"({X}, {Y})";
    }

    /// <summary>
    /// Portable size structure for cross-platform compatibility (replaces System.Windows.Size)
    /// </summary>
    public struct PortableSize
    {
        public double Width { get; set; }
        public double Height { get; set; }

        public PortableSize(double width, double height)
        {
            Width = width;
            Height = height;
        }

        public override readonly string ToString() => $"{Width} x {Height}";
    }

    /// <summary>
    /// Rotation values matching WPF's System.Windows.Media.Imaging.Rotation enum
    /// </summary>
    public enum PortableRotation
    {
        Rotate0 = 0,
        Rotate90 = 1,
        Rotate180 = 2,
        Rotate270 = 3
    }
}
