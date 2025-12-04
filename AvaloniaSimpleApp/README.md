# AvaloniaSimpleApp - Test Project

This is a hybrid project that serves as both a standalone Avalonia application AND a test project that appears in Visual Studio Test Explorer.

## Features

### Test 1: PDF Stress Test (TestAvaloniaPdfStressTest)
- **PDF Stress Test**: Repeatedly renders the same PDF page using PDFium to verify deterministic rendering
- **Full-screen Display**: Maximized window showing the rendered PDF
- **Real-time Statistics**: Shows iteration count, unique checksums, and rendering performance

### Test 2: PDF Viewer UI (TestAvaloniaPdfViewerUI) ? NEW
- **Complete UI**: Replicates the WPF PdfViewerWindow interface with all controls
- **PDF Rendering**: Displays pages 1 and 2 side-by-side using PDFium
- **Two-Page Layout**: Left page (page 1) aligned right, right page (page 2) aligned left
- **Control Layout**: Same toolbar with favorites, ink, rotate, navigation buttons, slider, and menu
- **Data Binding**: All controls properly bound to properties
- **UI Elements**: 
  - Favorite checkboxes (left and right)
  - Ink mode toggles
  - Page navigation (Previous/Next buttons, page textbox, slider)
  - Menu with Chooser, Show 2 Pages, Full Screen, About, Quit
  - Page descriptions at bottom showing current page info
  - Touch count display

## Running the Tests

### Method 1: Run from Test Explorer ? RECOMMENDED
1. Open **Test Explorer** in Visual Studio (Test > Test Explorer)
2. Find the tests:
   - `AvaloniaTests.TestAvaloniaPdfStressTest` - PDF rendering stress test
   - `AvaloniaTests.TestAvaloniaPdfViewerUI` - **Complete UI with PDF pages 1 & 2 displayed**
3. Right-click and select **Run**
4. The Avalonia window will open showing the PDF

### Method 2: Run Directly (Stress Test Only)
```powershell
cd AvaloniaSimpleApp
dotnet run
```

### Method 3: Debug from Visual Studio
1. Set `AvaloniaSimpleApp` as the startup project
2. Press F5 to run with debugging

## How It Works

The project has two entry points:
- **Program.Main**: Standard Avalonia application entry point (used when running directly)
- **AvaloniaTests.TestAvaloniaPdfStressTest**: MSTest method for PDF stress test
- **AvaloniaTests.TestAvaloniaPdfViewerUI**: MSTest method for UI demo with real PDF rendering

### PDF Rendering Implementation

The `PdfViewerWindow` now includes:
- **`LoadAndDisplayPagesAsync()`**: Loads PDF and renders pages 1 and 2
- **`RenderPageAsync()`**: Uses PDFium to render individual pages to Avalonia Bitmap
- **Grid Layout**: Two columns for side-by-side page display
- **Async/Await**: Proper async loading and UI thread marshaling

Both tests use `PdfViewerApp` (custom Application class) or the default `App` to configure and launch the Avalonia application.

## Configuration

The project is marked as a test project with these settings in `.csproj`:
```xml
<IsTestProject>true</IsTestProject>
<StartupObject>AvaloniaSimpleApp.Program</StartupObject>
```

This allows it to:
- Show up in Test Explorer (IsTestProject=true)
- Still run as a standalone app (StartupObject=Program)

## Files Structure

- **MainWindow.axaml/cs**: PDF stress test window with start/stop button
- **PdfViewerWindow.axaml/cs**: Full UI replication of WPF PdfViewerWindow with PDF rendering
- **AvaloniaTests.cs**: Test methods that launch each window
- **Program.cs**: Standard Avalonia entry point
- **App.axaml/cs**: Avalonia application class
- **PdfViewerApp**: Custom Application class for the viewer UI test

## PDF File Location

Both tests look for the PDF at:
```
C:\Users\{username}\OneDrive\SheetMusic\Pop\PopSingles\Be Our Guest - G Major - MN0174098.pdf
```

If not found, it tries:
```
d:\OneDrive\SheetMusic\Pop\PopSingles\Be Our Guest - G Major - MN0174098.pdf
```

You can modify the path in the respective `.axaml.cs` files if needed.

## Comparing with WPF

This project demonstrates Avalonia's capabilities and can be compared with the WPF implementation:

### PDF Rendering Tests
- **TestStressOnePage** (WPF): Windows.Data.Pdf (non-deterministic)
- **TestStressOnePagePdfium** (WPF): PDFium (deterministic)
- **TestAvaloniaPdfStressTest** (Avalonia): PDFium (deterministic, single page loop)
- **TestAvaloniaPdfViewerUI** (Avalonia): PDFium (deterministic, pages 1 & 2 side-by-side)

All PDFium tests should show that rendering produces consistent checksums across multiple renders.

### UI Comparison
- **PdfViewerWindow.xaml** (WPF): Original WPF implementation with two-page view
- **PdfViewerWindow.axaml** (Avalonia): Avalonia port with same controls + PDF rendering

Key differences in the Avalonia port:
- `x:DataType` required for compiled bindings
- CheckBox content uses TextBlock children instead of Content property
- Element name binding syntax: `{Binding #elementName.Property}`
- ToolTip syntax: `ToolTip.Tip` instead of `ToolTip`
- PDF rendering uses Avalonia.Media.Imaging.Bitmap
- Grid layout with two columns for side-by-side pages
- Async/await pattern for PDF loading with Dispatcher.UIThread.InvokeAsync()

### What You'll See

When running `TestAvaloniaPdfViewerUI`:
1. **Full-screen maximized window** with all controls visible
2. **Page 1 on the left side** (aligned to the right edge)
3. **Page 2 on the right side** (aligned to the left edge)
4. **Page descriptions at bottom** showing "Page 1 of N" and "Page 2 of N"
5. **All toolbar controls** functional (favorites, navigation, menu, etc.)
6. **PDF title** in the toolbar
