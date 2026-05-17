# Features

## PDF Viewing

- Render any PDF file using the cross-platform PDFium engine
- Single-page or two-page (spread) view
- Page slider with pop-up preview thumbnail
- Full-screen mode
- Zoom and rotate per volume
- Page number offset — maps printed page numbers to PDF page indices

## Multi-Volume Support

A single "piece" may span multiple PDF files (volumes). SheetMusicViewer treats them as a continuous sequence, allowing:

- Unified table of contents across all volumes
- Single slider covering all pages
- Per-volume rotation settings

## Table of Contents (TOC)

Each piece can have a TOC listing individual songs/chapters with:

- Song name
- Composer
- Composition/publication date
- Notes
- Page number
- Optional URL link (e.g., YouTube recording, purchase page)

The TOC is editable through the **Metadata** form and is stored in the `.bmk` JSON file.

## Favorites / Bookmarks

Mark any page as a favorite with an optional name. Favorites appear in a quick-access list and can be navigated directly.

## Ink Annotations

Draw on any page using the built-in ink canvas:

- Freehand pen strokes
- Highlighter mode
- Configurable color and stroke thickness
- Per-page undo/redo stack
- Annotations are stored in a portable JSON format (see [bmk-json-format.md](bmk-json-format.md)) — not WPF/ISF binary, so they survive across platforms

### Ink Toolbar

Two ink toolbars are docked at the left and right edges of the window (one per visible page). Each toolbar provides:

- Pen / highlighter toggle
- Color picker
- Undo / redo buttons
- Erase all strokes for the page

## Page Cache

An async background cache pre-loads thumbnail images for nearby pages, making navigation feel instant. Cache status is shown in the status bar.

## Metronome

A built-in, drift-corrected metronome (see [metronome.md](metronome.md)):

- Configurable BPM (20–300)
- Accent every N beats
- Multiple click sounds (woodblock, click, beep)
- Mute audio (visual beat only)
- Floating overlay window so it stays visible while reading music

## Choose Music / Library Browser

The **Choose Music** window lets you:

- Add root folders that are scanned for PDF files
- See all discovered pieces with metadata
- Filter and search by title or composer
- Open a piece directly

## Metadata Editor

The **Metadata Form** provides a full editor for:

- Volume list (add / remove / reorder PDF files)
- Table of contents entries
- Page number offset
- General notes
- Per-volume rotation

## MuseScore Export

Convert a PDF page range to a MuseScore file via the Audiveris OMR (Optical Music Recognition) engine. See [musescore-export.md](musescore-export.md).

## Options

Configurable preferences include:

- Default root music folder
- Two-page view default
- Metronome defaults
- Audiveris and MuseScore executable paths

## Sample Music

On first launch the app installs a bundled sample piece (*Getting Started*) so you can explore all features immediately without providing your own PDF files.
