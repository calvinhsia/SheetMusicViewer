# Architecture

## Solution Layout

```
SheetMusicViewer.sln
├── SheetMusicLib/               # Portable class library — core data types & logic
├── SheetMusicViewer/            # WPF application (Windows-only, legacy)
├── SheetMusicViewer.Desktop/    # Avalonia application (cross-platform, primary)
├── AvaloniaApplication1/        # Avalonia sandbox / prototype project
├── WpfApp/                      # WPF sandbox / prototype project
├── Tests/                       # MSTest unit & integration tests (targets SheetMusicLib + WPF)
└── AvaloniaTests/               # MSTest tests for the Avalonia application
```

## Technology Stack

| Layer | Technology |
|---|---|
| Target framework | .NET 10 |
| Cross-platform UI | [Avalonia UI](https://avaloniaui.net/) |
| Legacy Windows UI | WPF (.NET 10) |
| PDF rendering | [PDFtoImage](https://github.com/sungaila/PDFtoImage) (SkiaSharp + PDFium) |
| Audio (metronome) | [NAudio](https://github.com/naudio/NAudio) (Windows), `afplay`/`aplay` (macOS/Linux) |
| Serialization | `System.Text.Json` |
| Unit testing | MSTest v3 |

## SheetMusicLib — Core Library

`SheetMusicLib` contains all platform-independent domain logic. It has no UI dependencies and can be consumed by any host (WPF, Avalonia, CLI, test harness).

### Key types

| Type | File | Purpose |
|---|---|---|
| `PdfMetaDataCore` | `PdfMetaDataCore.cs` | Core metadata loading, saving, TOC/favorites management |
| `PdfMetaDataUtils` | `PdfMetaDataUtils.cs` | Helper utilities (path resolution, volume merging, etc.) |
| `TOCEntry` | `MusicDataTypes.cs` | Table-of-contents entry: song name, composer, date, page |
| `Favorite` | `MusicDataTypes.cs` | Bookmarked page with optional label |
| `InkStrokeClass` | `MusicDataTypes.cs` | Per-page ink annotation container |
| `AppSettings` | `AppSettings.cs` | Persisted user preferences |
| `MetronomeSettings` | `MetronomeSettings.cs` | Tempo, accent, sound settings |
| `BmkJsonFormat` | `BmkJsonFormat.cs` | JSON serialization types for `.bmk` files |
| `BmkJsonSerializer` | `BmkJsonSerializer.cs` | Read/write `.bmk` JSON files |
| `PortableInkTypes` | `PortableInkTypes.cs` | Platform-agnostic ink stroke model |
| `PortableTypes` | `PortableTypes.cs` | Shared geometry types (`PortablePoint`, `PortableRect`) |
| `Logger` | `Logger.cs` | Lightweight trace/file logger |

### Interfaces

| Interface | Purpose |
|---|---|
| `IThumbnailCache` | Platform-specific thumbnail caching (WPF BitmapImage vs Avalonia Bitmap) |
| `IPdfDocumentProvider` | Async page-count provider — decouples PDF libraries from core logic |
| `IExceptionHandler` | Platform-specific exception/logging callback |

## SheetMusicViewer.Desktop — Avalonia Application

This is the primary, cross-platform application.

### Key classes

| Class | File | Purpose |
|---|---|---|
| `PdfViewerWindow` | `PdfViewerWindow.axaml.cs` | Main window: PDF rendering, navigation, ink, favorites |
| `ChooseMusicWindow` | `ChooseMusicWindow.cs` | Browse and select music collections |
| `MetaDataFormWindow` | `MetaDataFormWindow.axaml.cs` | Edit TOC entries, page offsets, notes |
| `MetronomeOverlayWindow` | `MetronomeOverlayWindow.cs` | Floating metronome overlay |
| `OptionsWindow` | `OptionsWindow.cs` | App settings UI |
| `ExportToMuseScoreWindow` | `ExportToMuseScoreWindow.cs` | MuseScore export dialog |
| `MuseScoreExportService` | `MuseScoreExportService.cs` | PDF extraction → Audiveris OMR → MuseScore pipeline |
| `MetronomeService` | `MetronomeService.cs` | Drift-corrected metronome engine with NAudio |
| `InkCanvasControl` | `InkCanvasControl.cs` | Custom ink canvas built on Avalonia |
| `GestureHandler` | `GestureHandler.cs` | Touch/pointer gesture recognition (swipe, pinch) |
| `BrowseControl` | `BrowseControl.cs` | Reusable file/folder browse control |
| `SampleDataHelper` | `SampleDataHelper.cs` | Installs sample music on first launch |
| `Converters` | `Converters.cs` | Avalonia value converters |

## SheetMusicViewer — WPF Application (Legacy)

The original Windows-only implementation. Shares `SheetMusicLib` for all data operations. New features are developed in the Avalonia project; the WPF project is maintained for reference.

## Data Flow

```
PDF file(s)
	│
	▼
PdfMetaDataCore (load .bmk JSON)
	│  metadata: TOC, favorites, ink, volumes
	▼
PdfViewerWindow
	├── PDFtoImage → SkiaSharp/Avalonia Bitmap → Image control
	├── InkCanvasControl → PortableInkStroke → BmkJsonSerializer
	└── PageCache → async thumbnail preload
```

## Cross-Platform PDF Rendering

PDF pages are rendered with **PDFtoImage** which wraps the native PDFium library via SkiaSharp. The rendered `SKBitmap` is converted to an `Avalonia.Media.Imaging.Bitmap` for display.

## Metadata Persistence

All metadata is stored in a `.bmk` JSON file that lives alongside the PDF (or at the root of a multi-volume set). See [bmk-json-format.md](bmk-json-format.md) for the full format specification.
