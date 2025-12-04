# BMK to JSON Conversion

## Summary

The Sheet Music Viewer now supports converting all BMK (bookmark) metadata files from XML format to a portable JSON format. This enables cross-platform compatibility and future Avalonia migration.

## What Gets Converted

? **Volume Information** - Multi-volume PDF sets  
? **Table of Contents** - Song names, composers, dates, notes  
? **Favorites** - Bookmarked pages with custom names  
? **Ink Annotations** - Drawing strokes (ISF ? portable JSON)  
? **Page Settings** - Rotation, page number offsets  
? **Metadata** - Last viewed page, notes, timestamps  

## How to Convert

1. Open the PDF Viewer application
2. Click **Menu ? Convert BMK to JSON**
3. Confirm the conversion
4. Original XML files are automatically backed up as `*.bmk.xml.backup`

## What Changed

### Before Conversion
- **Format**: XML with XmlSerializer
- **Ink Storage**: WPF ISF binary format (platform-specific)
- **Compatibility**: Windows/WPF only

### After Conversion
- **Format**: JSON with readable structure
- **Ink Storage**: Portable JSON with explicit points/colors
- **Compatibility**: Cross-platform (WPF, Avalonia, web, mobile)

## Files Created

### New Files
- `BmkJsonConverter.cs` - Complete BMK ? JSON converter
- `BmkJsonFormat.cs` - JSON data structures (embedded in converter)
- `InkStrokeConverter.cs` - Ink stroke ISF ? JSON converter  
- `PortableInkStroke.cs` - Portable ink data structures (embedded in converter)
- `BmkJsonFormat.md` - Complete documentation
- `Tests/BmkJsonConversionTests.cs` - Integration tests

### Modified Files
- `PdfViewerWindow.xaml` - Added "Convert BMK to JSON" menu item
- `PdfViewerWindow.xaml.cs` - Added `BtnConvertInkToJson_Click` handler

## Test Results

All 6 integration tests passing:
- ? Complete metadata preservation (volumes, TOC, favorites, ink)
- ? Save and reload from JSON
- ? Format detection (JSON vs XML)
- ? Automatic backup creation
- ? Multi-volume support
- ? Empty metadata handling

## Example JSON Structure

```json
{
  "Version": 1,
  "LastPageNo": 42,
  "PageNumberOffset": -10,
  "Volumes": [
    {
      "FileName": "Book_Vol1.pdf",
      "PageCount": 150,
      "Rotation": 0
    }
  ],
  "TableOfContents": [
    {
      "SongName": "Moonlight Sonata",
      "Composer": "Beethoven",
      "PageNo": 5
    }
  ],
  "Favorites": [
    {
      "PageNo": 12,
      "Name": "My favorite"
    }
  ],
  "InkStrokes": {
    "15": {
      "PageNo": 15,
      "CanvasWidth": 800,
      "CanvasHeight": 1200,
      "Strokes": [
        {
          "Points": [
            { "X": 10, "Y": 20 },
            { "X": 100, "Y": 200 }
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

## Benefits

1. **Cross-Platform** - Works with WPF, Avalonia, web, mobile
2. **Human-Readable** - Easy to inspect, debug, and edit
3. **Version Control Friendly** - Git diffs work well with JSON
4. **Interoperable** - Standard format, many tools support it
5. **Future-Proof** - Not tied to Windows-specific APIs
6. **Safe** - Original XML files backed up automatically

## Migration Notes

- **Backward Compatibility**: Application still reads XML BMK files
- **Forward Compatibility**: New saves always use JSON format
- **Gradual Migration**: Convert when convenient, no rush
- **Backup Safety**: Original files preserved as `.xml.backup`
- **One-Way Conversion**: XML ? JSON (no automatic JSON ? XML)

## Usage Statistics

After conversion, the application shows:
- Total BMKs processed
- BMKs converted to JSON
- BMKs with ink annotations
- Backup file locations

## Documentation

See `BmkJsonFormat.md` for complete technical documentation including:
- Full JSON schema
- Programmatic API usage
- Multi-volume support details
- Error handling
- Example code

## Next Steps

1. **Test the Conversion** - Try it on a small subset first
2. **Verify Backups** - Check that `.xml.backup` files were created
3. **Validate JSON** - Open a `.bmk` file in a text editor to inspect
4. **Report Issues** - File GitHub issues if problems occur

## Avalonia Readiness

This conversion prepares the codebase for Avalonia migration:
- ? Ink data no longer depends on WPF ISF format
- ? Metadata serialization no longer uses XmlSerializer
- ? JSON format works on all platforms
- ? Data structures are platform-agnostic
