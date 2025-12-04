# AvaloniaSimpleApp - Test Project

This is a hybrid project that serves as both a standalone Avalonia application AND a test project that appears in Visual Studio Test Explorer.

## Features

### Test 1: PDF Stress Test (TestAvaloniaPdfStressTest)
- **PDF Stress Test**: Repeatedly renders the same PDF page using PDFium to verify deterministic rendering
- **Full-screen Display**: Maximized window showing the rendered PDF
- **Real-time Statistics**: Shows iteration count, unique checksums, and rendering performance

### Test 2: PDF Viewer UI (TestAvaloniaPdfViewerUI)
- **Complete UI**: Replicates the WPF PdfViewerWindow interface with all controls
- **Control Layout**: Same toolbar with favorites, ink, rotate, navigation buttons, slider, and menu
- **Data Binding**: Demonstrates Avalonia data binding with properties like CurrentPageNumber, PdfTitle, etc.
- **UI Elements**: 
  - Favorite checkboxes (left and right)
  - Ink mode toggles
  - Page navigation (Previous/Next buttons, page textbox, slider)
  - Menu with Chooser, Show 2 Pages, Full Screen, About, Quit
  - Page descriptions at bottom
  - Touch count display

## Running the Tests

### Method 1: Run from Test Explorer
1. Open **Test Explorer** in Visual Studio (Test > Test Explorer)
2. Find the tests:
   - `AvaloniaTests.TestAvaloniaPdfStressTest` - PDF rendering stress test
   - `AvaloniaTests.TestAvaloniaPdfViewerUI` - Complete UI demonstration
3. Right-click and select **Run**
4. The Avalonia window will open

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
- **AvaloniaTests.TestAvaloniaPdfStressTest**: MSTest method for PDF stress test (used in Test Explorer)
- **AvaloniaTests.TestAvaloniaPdfViewerUI**: MSTest method for UI demo (used in Test Explorer)

Both use the same `Program.BuildAvaloniaApp()` method to configure and launch the Avalonia application.

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
- **PdfViewerWindow.axaml/cs**: Full UI replication of WPF PdfViewerWindow
- **AvaloniaTests.cs**: Test methods that launch each window
- **Program.cs**: Standard Avalonia entry point
- **App.axaml/cs**: Avalonia application class

## PDF File Location

The stress test looks for the PDF at:
```
C:\Users\{username}\OneDrive\SheetMusic\Pop\PopSingles\Be Our Guest - G Major - MN0174098.pdf
```

If not found, it tries:
```
d:\OneDrive\SheetMusic\Pop\PopSingles\Be Our Guest - G Major - MN0174098.pdf
```

You can modify the path in `MainWindow.axaml.cs` constructor if needed.

## Comparing with WPF

This project demonstrates Avalonia's capabilities and can be compared with the WPF implementation:

### PDF Rendering Tests
- **TestStressOnePage** (WPF): Windows.Data.Pdf (non-deterministic)
- **TestStressOnePagePdfium** (WPF): PDFium (deterministic)
- **TestAvaloniaPdfStressTest** (Avalonia): PDFium (deterministic)

All PDFium tests should show that rendering produces consistent checksums across multiple renders.

### UI Comparison
- **PdfViewerWindow.xaml** (WPF): Original WPF implementation
- **PdfViewerWindow.axaml** (Avalonia): Avalonia port with same controls

Key differences in the Avalonia port:
- `x:DataType` required for compiled bindings
- CheckBox content uses TextBlock children instead of Content property
- Element name binding syntax: `{Binding #elementName.Property}`
- ToolTip syntax: `ToolTip.Tip` instead of `ToolTip`
- Some layout differences due to Avalonia's rendering engine
