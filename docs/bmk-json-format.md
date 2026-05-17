# BMK JSON Format

The `.bmk` file is the metadata store for each piece of sheet music. It lives alongside the PDF file and uses JSON (UTF-8, pretty-printed).

> **Legacy note**: Older installations may have `.bmk` files in XML format. Use **Menu → Convert BMK to JSON** to migrate them. The application reads both formats but always writes JSON.

## File Location

```
SheetMusic/
  PianoBook.pdf              ← PDF file
  PianoBook.bmk              ← JSON metadata file
  PianoBook.bmk.xml.backup   ← Backup of the original XML (created on conversion)
```

For **Singles folders** (a directory of individual PDFs) one `.bmk` file covers all songs:

```
SheetMusic/
  Singles/          ← folder containing individual PDFs
	Song1.pdf
	Song2.pdf
  Singles.bmk       ← one JSON BMK for the whole folder
```

## Complete JSON Structure

```json
{
  "Version": 1,
  "LastWrite": "2024-12-04T14:30:00",
  "LastPageNo": 42,
  "PageNumberOffset": -10,
  "Notes": "My collection of piano music",
  "Volumes": [
	{ "FileName": "PianoBook_Vol1.pdf", "PageCount": 150, "Rotation": 0 },
	{ "FileName": "PianoBook_Vol2.pdf", "PageCount": 120, "Rotation": 2 }
  ],
  "TableOfContents": [
	{
	  "SongName": "Moonlight Sonata",
	  "Composer": "Ludwig van Beethoven",
	  "Date": "1801",
	  "Notes": "Op. 27, No. 2",
	  "PageNo": 5,
	  "Link": "https://www.youtube.com/watch?v=..."
	}
  ],
  "Favorites": [
	{ "PageNo": 12, "Name": "My favorite piece" }
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

## Property Reference

### Root object (`BmkJsonFormat`)

| Property | Type | Description |
|---|---|---|
| `Version` | int | Format version — currently `1` |
| `LastWrite` | DateTime | Last modification timestamp |
| `LastPageNo` | int | Last viewed page (restored on open) |
| `PageNumberOffset` | int | Offset between printed page numbers and PDF page indices |
| `Notes` | string | Free-form user notes about the piece |
| `Volumes` | array | Ordered list of PDF volumes |
| `TableOfContents` | array | Song / chapter entries |
| `Favorites` | array | Bookmarked pages |
| `InkStrokes` | dictionary | Page number (string key) → ink stroke data |

### `JsonPdfVolumeInfo`

| Property | Type | Description |
|---|---|---|
| `FileName` | string | PDF filename only (not the full path) |
| `PageCount` | int | Number of pages in this volume |
| `Rotation` | int | `0`=Normal, `1`=90°, `2`=180°, `3`=270° |

### `JsonTOCEntry`

| Property | Type | Description |
|---|---|---|
| `SongName` | string | Title of the song or chapter |
| `Composer` | string | Composer or author |
| `Date` | string | Composition or publication date |
| `Notes` | string | Additional notes |
| `PageNo` | int | Page number where content starts |
| `Link` | string | Optional URL (YouTube, purchase, etc.) |

### `JsonFavorite`

| Property | Type | Description |
|---|---|---|
| `PageNo` | int | Page number |
| `Name` | string | User-defined label |

### `JsonInkStrokes` (per-page)

| Property | Type | Description |
|---|---|---|
| `PageNo` | int | Page number |
| `CanvasWidth` | double | Canvas width at annotation time (used for scaling) |
| `CanvasHeight` | double | Canvas height at annotation time |
| `Strokes` | array | List of `PortableInkStroke` objects |

### `PortableInkStroke`

| Property | Type | Description |
|---|---|---|
| `Points` | array | Ordered `{X, Y}` coordinates |
| `Color` | string | Hex color `#RRGGBB` |
| `Thickness` | double | Stroke width in pixels |
| `IsHighlighter` | bool | Semi-transparent highlighter mode |
| `Opacity` | double | `0.0` (transparent) – `1.0` (opaque) |

## Multi-Volume Page Numbers

Page numbers in `TableOfContents`, `Favorites`, and `InkStrokes` are **continuous across all volumes**. Volume 1 uses pages `1…PageCount₁`; Volume 2 uses `PageCount₁+1…PageCount₁+PageCount₂`, and so on.

## Format Detection

The library detects format by inspecting the first byte:
- `{` → JSON
- `<` → legacy XML

Both are readable; all new writes use JSON.

## Programmatic Access

```csharp
// Load (auto-detects JSON or XML)
var metadata = BmkJsonConverter.LoadFromJson(bmkPath, pdfPath, isSinglesFolder: false);

// Save as JSON
BmkJsonConverter.SaveAsJson(metadata, bmkPath);

// Check format
bool isJson = BmkJsonConverter.IsJsonFormat(bmkPath);

// Batch-convert XML → JSON
var (total, converted) = BmkJsonConverter.ConvertAllBmksToJson(metadataList);
```

## XML Migration

Run **Menu → Convert BMK to JSON** to convert all XML `.bmk` files in the current library. The original XML is backed up as `*.bmk.xml.backup` before conversion.
