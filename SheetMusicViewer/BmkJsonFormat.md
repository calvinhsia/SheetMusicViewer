# Portable Ink Stroke Format

## Overview

The **BMK JSON Format** is a complete replacement for the XML-based BMK (bookmark) format. It provides cross-platform compatibility for all PDF metadata including:
- **Volume Information**: Multi-volume PDF sets
- **Table of Contents**: Song names, composers, dates, notes
- **Favorites/Bookmarks**: Marked pages with custom names
- **Ink Annotations**: Drawing strokes in portable JSON format
- **Page Settings**: Rotation, page number offsets

This replaces both the WPF-specific ISF (Ink Serialized Format) binary format for ink and the XML serialization for metadata.

## Complete BMK JSON Format Structure

### BmkJsonFormat

```json
{
  "Version": 1,
  "LastWrite": "2024-12-04T14:30:00",
  "LastPageNo": 42,
  "PageNumberOffset": -10,
  "Notes": "My collection of piano music",
  "Volumes": [
    {
      "FileName": "PianoBook_Vol1.pdf",
      "PageCount": 150,
      "Rotation": 0
    },
    {
      "FileName": "PianoBook_Vol2.pdf",
      "PageCount": 120,
      "Rotation": 2
    }
  ],
  "TableOfContents": [
    {
      "SongName": "Moonlight Sonata",
      "Composer": "Ludwig van Beethoven",
      "Date": "1801",
      "Notes": "Op. 27, No. 2",
      "PageNo": 5
    }
  ],
  "Favorites": [
    {
      "PageNo": 12,
      "Name": "My favorite piece"
    }
  ],
  "InkStrokes": {
    "15": {
      "PageNo": 15,
      "CanvasWidth": 800.0,
      "CanvasHeight": 1200.0,
      "Strokes": [
        {
          "Points": [
            { "X": 10.0, "Y": 20.0 },
            { "X": 100.0, "Y": 200.0 }
          ],
          "Color": "#FF0000",
          "Thickness": 2.0,
          "IsHighlighter": false,
          "Opacity": 1.0
        }
      ]
    }
  }
}
```

## Properties

### BmkJsonFormat
- **Version** (int): Format version (currently 1)
- **LastWrite** (DateTime): Last modification timestamp
- **LastPageNo** (int): Last viewed page number
- **PageNumberOffset** (int): Offset for printed page numbers vs PDF page numbers
- **Notes** (string): User notes about the document
- **Volumes** (array): List of PDF files in multi-volume sets
- **TableOfContents** (array): Song/chapter entries
- **Favorites** (array): Bookmarked pages
- **InkStrokes** (dictionary): Page number ? ink strokes mapping

### JsonPdfVolumeInfo
- **FileName** (string): PDF filename (not full path)
- **PageCount** (int): Number of pages in this volume
- **Rotation** (int): 0=Normal, 1=90°, 2=180°, 3=270°

### JsonTOCEntry
- **SongName** (string): Title of song/chapter
- **Composer** (string): Composer/author name
- **Date** (string): Composition/publication date
- **Notes** (string): Additional notes
- **PageNo** (int): Page number where content starts

### JsonFavorite
- **PageNo** (int): Page number
- **Name** (string): User-defined name for the favorite

### JsonInkStrokes
- **PageNo** (int): Page number with ink
- **CanvasWidth** (double): Canvas width when drawn (for scaling)
- **CanvasHeight** (double): Canvas height when drawn (for scaling)
- **Strokes** (array): List of PortableInkStroke objects

### PortableInkStroke
- **Points** (array): Collection of points (X, Y coordinates)
- **Color** (string): Hex color in #RRGGBB format (e.g., "#FF0000" for red)
- **Thickness** (double): Stroke width/thickness in pixels
- **IsHighlighter** (bool): Whether this stroke is a highlighter (semi-transparent)
- **Opacity** (double): Opacity value from 0.0 (transparent) to 1.0 (opaque)

### PortablePoint
- **X** (double): X coordinate
- **Y** (double): Y coordinate

## Conversion

### Converting All BMK Files to JSON

Use the menu item **"Menu ? Convert BMK to JSON"** in the PDF Viewer to batch convert all existing BMK files from XML format to JSON format.

**Important Notes:**
- Original XML files are automatically backed up with `.xml.backup` extension
- Conversion is one-way (XML ? JSON)
- Ink strokes are converted from ISF binary to portable JSON
- All metadata (TOC, favorites, volumes) is preserved

### Programmatic Conversion

```csharp
// Convert all BMK files in a list
var (totalCount, convertedCount) = BmkJsonConverter.ConvertAllBmksToJson(metadataList);

// Convert single PdfMetaData to JSON
var jsonData = BmkJsonConverter.ConvertToJson(pdfMetadata);
var jsonText = BmkJsonConverter.SerializeToJson(jsonData);

// Save as JSON file
BmkJsonConverter.SaveAsJson(pdfMetadata, bmkFilePath);

// Load from JSON file
var metadata = BmkJsonConverter.LoadFromJson(bmkFilePath, pdfFilePath, isSinglesFolder);

// Check if file is JSON format
bool isJson = BmkJsonConverter.IsJsonFormat(bmkFilePath);
```

### Round-Trip Conversion

```csharp
// XML ? JSON
var metadata = await PdfMetaData.ReadPdfMetaDataAsync(pdfPath); // Reads XML
var jsonData = BmkJsonConverter.ConvertToJson(metadata);
BmkJsonConverter.SaveAsJson(metadata, bmkPath);

// JSON ? PdfMetaData
var loaded = BmkJsonConverter.LoadFromJson(bmkPath, pdfPath, isSinglesFolder: false);
```

## Benefits

1. **Complete Cross-Platform**: JSON can be read by any platform (WPF, Avalonia, web, mobile)
2. **Human-Readable**: JSON format can be inspected and debugged easily with any text editor
3. **Version Control Friendly**: Text-based format works better with Git than binary XML
4. **Interoperable**: Standard format that can be processed by many tools and languages
5. **Future-Proof**: Not tied to any specific UI framework or Windows-specific APIs
6. **Preserves All Data**: Volumes, TOC, favorites, ink, rotation, page offsets all preserved
7. **Automatic Backups**: Original XML files backed up before conversion

## Compatibility

### Format Detection
- Automatically detects whether a BMK file is XML or JSON format
- Checks first character: `{` = JSON, `<` = XML
- Both formats are supported for loading
- New saves always use JSON format

### Migration Path
1. **Before conversion**: BMK files are in XML format, ink is in ISF binary
2. **Run converter**: Menu ? Convert BMK to JSON
3. **After conversion**: BMK files are in JSON format, ink is in portable JSON
4. **Backup created**: Original XML saved as `*.bmk.xml.backup`

### Ink Stroke Compatibility
- Old ISF format: WPF `StrokeCollection.Save()` compressed binary
- New JSON format: Portable strokes with explicit point lists, colors, attributes
- Converter handles both formats transparently
- JSON ink can be read by both WPF and Avalonia

## Storage Location

BMK files are stored alongside PDF files with the same base name but `.bmk` extension:

```
SheetMusic/
  PianoBook.pdf          ? PDF file
  PianoBook.bmk          ? JSON BMK file (after conversion)
  PianoBook.bmk.xml.backup  ? Original XML backup
```

For **Singles folders** (collections of individual songs), one BMK file is created next to the folder:

```
SheetMusic/
  Singles/               ? Folder with individual PDFs
    Song1.pdf
    Song2.pdf
    Song3.pdf
  Singles.bmk            ? Single JSON BMK for all songs
  Singles.bmk.xml.backup ? Backup
```

## Multi-Volume Support

Multi-volume PDF sets (e.g., books scanned in multiple files) are fully supported:

```json
{
  "Volumes": [
    { "FileName": "Book_Vol1.pdf", "PageCount": 150 },
    { "FileName": "Book_Vol2.pdf", "PageCount": 140 },
    { "FileName": "Book_Vol3.pdf", "PageCount": 120 }
  ],
  "PageNumberOffset": -10,
  "Favorites": [
    { "PageNo": 5, "Name": "In Volume 1" },
    { "PageNo": 160, "Name": "In Volume 2" },
    { "PageNo": 305, "Name": "In Volume 3" }
  ]
}
```

Page numbers are continuous across volumes. The converter automatically:
- Preserves volume order
- Maintains page offsets
- Adjusts favorites/ink/TOC page numbers correctly

## Error Handling

The converter handles errors gracefully:
- **Corrupted files**: Skipped, conversion continues with other files
- **Missing PDFs**: Conversion still works on metadata
- **Already converted**: Skips files already in JSON format
- **Backup failures**: Reports error but doesn't block conversion

## Example Usage

```csharp
// Convert all BMK files in a music collection
var (metadataList, _) = await PdfMetaData.LoadAllPdfMetaDataFromDiskAsync(rootFolder);
var (total, converted) = BmkJsonConverter.ConvertAllBmksToJson(metadataList);
Console.WriteLine($"Converted {converted} of {total} BMK files");

// Work with JSON BMK programmatically
var bmkPath = "MyMusic.bmk";
if (BmkJsonConverter.IsJsonFormat(bmkPath))
{
    var metadata = BmkJsonConverter.LoadFromJson(bmkPath, "MyMusic.pdf", false);
    
    // Add a favorite
    metadata.ToggleFavorite(42, IsFavorite: true, FavoriteName: "Best song");
    
    // Save back as JSON
    BmkJsonConverter.SaveAsJson(metadata, bmkPath);
}
```

## Technical Notes

- **Thread Safety**: Conversion operations are not thread-safe, run on single thread
- **Memory**: JSON format may use slightly more memory than compressed XML
- **Performance**: JSON parsing is typically faster than XML deserialization
- **Size**: JSON files are usually larger than XML but more compressible
- **Encoding**: Always uses UTF-8 encoding for JSON files
- **Line Endings**: Uses `WriteIndented = true` for readable formatting
