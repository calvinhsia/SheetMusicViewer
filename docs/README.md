# SheetMusicViewer Documentation

Welcome to the SheetMusicViewer documentation. This folder contains guides covering architecture, features, data formats, and development workflows.

## Contents

| Document | Description |
|---|---|
| [architecture.md](architecture.md) | Project structure, solution layout, and technology stack |
| [features.md](features.md) | Feature overview: PDF viewing, ink annotations, favorites, TOC, metronome, MuseScore export |
| [bmk-json-format.md](bmk-json-format.md) | BMK JSON metadata format reference |
| [musescore-export.md](musescore-export.md) | MuseScore export via Audiveris OMR pipeline |
| [metronome.md](metronome.md) | Built-in cross-platform metronome service |
| [development.md](development.md) | Building, testing, and contributing |

## Quick Start

1. Install [.NET 10 SDK](https://dotnet.microsoft.com/download)
2. Clone the repository
3. Open `SheetMusicViewer.sln` in Visual Studio 2022+
4. Set **SheetMusicViewer.Desktop** as the startup project
5. Press **F5** to run

## Project Goals

SheetMusicViewer is a cross-platform desktop application for viewing, annotating, and organizing PDF sheet music. It supports:

- Multi-volume PDF sets with a unified table of contents
- Ink annotations stored in a portable JSON format
- A built-in metronome with drift-corrected timing
- MuseScore Studio integration via Audiveris OMR conversion
- Favorites/bookmarks and per-piece metadata
