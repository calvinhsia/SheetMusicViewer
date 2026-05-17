# MuseScore Export

SheetMusicViewer can convert PDF sheet music pages to an editable MuseScore file using the [Audiveris](https://github.com/Audiveris/audiveris) Optical Music Recognition (OMR) engine.

## Prerequisites

| Software | Purpose | Where to get |
|---|---|---|
| [Audiveris](https://github.com/Audiveris/audiveris/releases) | OMR — reads the PDF and produces a MusicXML/`.mxl` file | GitHub releases |
| [MuseScore Studio 4](https://musescore.org/en/download) (or 3) | Opens the resulting score for editing/playback | musescore.org |
| [Ghostscript](https://www.ghostscript.com/releases/gsdnld.html) *(optional)* | PDF normalisation pass before Audiveris for better recognition | ghostscript.com |

## How It Works

```
PDF pages (selected range)
		│
		▼  ExtractPdfPagesAsync
  Temp PDF file
		│
		▼  (optional) Ghostscript normalisation
  Normalised PDF
		│
		▼  Audiveris -batch -export -output
  .mxl / MusicXML archive
		│
		▼  MuseScore Studio launched with the .mxl file
  Editable score
```

## Using the Export Dialog

1. Open a PDF in SheetMusicViewer
2. Navigate to the page(s) you want to convert
3. Open **Menu → Export to MuseScore…**
4. Set the page range (start page, end page)
5. Confirm or override the auto-detected paths for Audiveris and MuseScore
6. Click **Export**

Progress is shown in the dialog. When complete, MuseScore Studio opens automatically with the resulting score.

## Auto-Detection of Executables

`MuseScoreExportService` searches standard installation paths automatically.

### Audiveris default paths

**Windows**
- `%ProgramFiles%\Audiveris\bin\Audiveris.bat`
- `%ProgramFiles%\Audiveris\Audiveris.bat`
- `%ProgramFiles%\Audiveris\bin\Audiveris.exe`
- `%LocalAppData%\Audiveris\bin\Audiveris.bat`

**macOS**
- `/Applications/Audiveris.app/Contents/MacOS/Audiveris`
- `/usr/local/bin/audiveris`
- `/opt/homebrew/bin/audiveris`

**Linux**
- `/usr/bin/audiveris`
- `/usr/local/bin/audiveris`
- `/opt/audiveris/bin/audiveris`

### MuseScore default paths

**Windows**
- `%ProgramFiles%\MuseScore 4\bin\MuseScore4.exe`
- `%ProgramFiles%\MuseScore 3\bin\MuseScore3.exe` *(fallback)*

**macOS**
- `/Applications/MuseScore 4.app/Contents/MacOS/mscore`
- `/Applications/MuseScore 3.app/Contents/MacOS/mscore` *(fallback)*

**Linux**
- `/usr/bin/musescore4`, `/usr/bin/mscore4`
- AppImage at `~/Applications/MuseScore-4.x86_64.AppImage`
- MuseScore 3 fallbacks

### Ghostscript default paths

**Windows**: scans `%ProgramFiles%\gs\gs*\bin\gswin64c.exe` (version-sorted, newest first)  
**macOS**: `/usr/local/bin/gs`, `/opt/homebrew/bin/gs`  
**Linux**: `/usr/bin/gs`, `/usr/local/bin/gs`

If none are found the Ghostscript step is skipped.

## Settings

Paths can be configured permanently in **Menu → Options → MuseScore Export**. They are saved in `AppSettings` and override auto-detection on next launch.

## Temporary Files

Extracted and intermediate files are written to:

```
%TEMP%\SheetMusicViewer_Export\
```

In Debug builds these files are retained for inspection. In Release builds they are cleaned up after MuseScore launches.

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| "Audiveris not found" | Audiveris not installed or not in default paths | Install Audiveris or set the path in Options |
| "MuseScore not found" | MuseScore Studio not installed | Install MuseScore 4 or set the path in Options |
| Poor recognition quality | Scanned PDF with low resolution or unusual engraving | Try running Ghostscript pre-processing; use 300 DPI+ scans |
| Audiveris hangs | Java not installed or wrong version | Install Java 17+ and ensure it is on `PATH` |
| Empty score opened | Audiveris produced no output | Check the Audiveris log in the export dialog for details |

## API Reference

```csharp
// Auto-detect installed tools
string? audiveris  = MuseScoreExportService.AutoDetectAudiveris();
string? musescore  = MuseScoreExportService.AutoDetectMuseScore();
string? ghostscript = MuseScoreExportService.AutoDetectGhostscript();

// Extract a page range to a temp PDF
string tempPdf = await MuseScoreExportService.ExtractPdfPagesAsync(
	sourcePdfPath: "MyMusic.pdf",
	startPage: 3,          // 1-based
	endPage: 6,            // 1-based, inclusive
	totalPages: 120,
	progress: new Progress<string>(msg => Console.WriteLine(msg)),
	ct: cancellationToken);

// Full pipeline (extract → Audiveris → open MuseScore)
await MuseScoreExportService.ExportToMuseScoreAsync(
	pdfPath, startPage, endPage, totalPages,
	audiverisPath, musescorePath, ghostscriptPath,
	progress, cancellationToken);
```
