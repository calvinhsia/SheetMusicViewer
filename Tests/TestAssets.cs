using System.IO;
using System.Runtime.CompilerServices;

namespace Tests
{
    /// <summary>
    /// Provides access to test asset files (BMK samples, etc.) for use in tests.
    /// Files are read directly from the source directory - no deployment/copying needed.
    /// </summary>
    public static class TestAssets
    {
        private static readonly string AssetsDirectory;

        static TestAssets()
        {
            var sourceDir = Path.GetDirectoryName(GetSourceFilePath())!;
            AssetsDirectory = Path.Combine(sourceDir, "TestAssets");
        }

        // CallerFilePath is evaluated at compile time, giving us the source directory path
        private static string GetSourceFilePath([CallerFilePath] string path = "") => path;

        /// <summary>
        /// Reads the content of a test asset file as a string.
        /// </summary>
        public static string ReadAsset(string fileName) => File.ReadAllText(Path.Combine(AssetsDirectory, fileName));

        // BMK Sample Files
        public static string SampleMultiVolumeBmk => ReadAsset("SampleMultiVolume.bmk");
        public static string SampleSingleVolumeBmk => ReadAsset("SampleSingleVolume.bmk");
        public static string SampleEmptyVolumesBmk => ReadAsset("SampleEmptyVolumes.bmk");
        public static string SampleNoVolumesBmk => ReadAsset("SampleNoVolumes.bmk");
        public static string SampleWithFavoritesBmk => ReadAsset("SampleWithFavorites.bmk");
        public static string SampleWithInkStrokesBmk => ReadAsset("SampleWithInkStrokes.bmk");
        public static string Sample59PianoSolosFullBmk => ReadAsset("Sample59PianoSolosFull.bmk");
    }
}
