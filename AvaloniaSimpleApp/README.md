# AvaloniaSimpleApp - Test Project

This is a hybrid project that serves as both a standalone Avalonia application AND a test project that appears in Visual Studio Test Explorer.

## Features

- **PDF Stress Test**: Repeatedly renders the same PDF page using PDFium to verify deterministic rendering
- **Full-screen Display**: Maximized window showing the rendered PDF
- **Real-time Statistics**: Shows iteration count, unique checksums, and rendering performance
- **Test Explorer Integration**: Appears as a test in Visual Studio Test Explorer

## Running the Application

### Method 1: Run from Test Explorer
1. Open **Test Explorer** in Visual Studio (Test > Test Explorer)
2. Find the test: `AvaloniaTests.TestAvaloniaPdfStressTest`
3. Right-click and select **Run**
4. The Avalonia window will open

### Method 2: Run Directly
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
- **AvaloniaTests.TestAvaloniaPdfStressTest**: MSTest method (used in Test Explorer)

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

## PDF File Location

The app looks for the PDF at:
```
C:\Users\{username}\OneDrive\SheetMusic\Pop\PopSingles\Be Our Guest - G Major - MN0174098.pdf
```

If not found, it tries:
```
d:\OneDrive\SheetMusic\Pop\PopSingles\Be Our Guest - G Major - MN0174098.pdf
```

You can modify the path in `MainWindow.axaml.cs` constructor if needed.

## Comparing with WPF

This project demonstrates Avalonia's PDF rendering capabilities and can be compared with the WPF tests in the `Tests` project:
- **TestStressOnePage**: WPF with Windows.Data.Pdf (non-deterministic)
- **TestStressOnePagePdfium**: WPF with PDFium (deterministic)
- **TestAvaloniaPdfStressTest**: Avalonia with PDFium (deterministic)

All three tests should show that PDFium produces consistent checksums across multiple renders.
