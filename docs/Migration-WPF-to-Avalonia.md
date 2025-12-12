# SheetMusicViewer Migration: WPF/WinRT to Avalonia

## Executive Summary

This document describes the migration path for SheetMusicViewer from a Windows-only WPF application using Windows Runtime (WinRT) APIs to a cross-platform Avalonia application. The migration enables the application to run on Windows, macOS, and Linux while maintaining the same functionality.

## Current Architecture (WPF + WinRT)

### Technology Stack

| Component | Technology | Windows Version |
|-----------|-----------|-----------------|
| UI Framework | WPF (.NET 8) | Windows 10+ |
| PDF Rendering | Windows.Data.Pdf (WinRT) | Windows 10 SDK 19041 |
| File Access | Windows.Storage (WinRT) | Windows 10+ |
| Bitmap Images | System.Windows.Media.Imaging | Windows |
| Ink/Annotations | System.Windows.Ink | Windows |
| Settings | Properties.Settings | Windows |

### Key WinRT Dependencies

The current implementation relies heavily on Windows Runtime APIs that are not available on other platforms:

```csharp
// PDF rendering via WinRT
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

// In PdfViewerWindow.xaml.cs and PdfMetaData.cs
StorageFile f = await StorageFile.GetFileFromPathAsync(pathPdfFileVol);
var pdfDoc = await PdfDocument.LoadFromFileAsync(f);
using var pdfPage = pdfDoc.GetPage(pageNo);
await pdfPage.RenderToStreamAsync(stream, renderOpts);
```

### Project Structure (Before)

```
SheetMusicViewer/
├── SheetMusicViewer/          # WPF Application (Windows-only)
│   ├── PdfViewerWindow.xaml   # Main PDF viewer
│   ├── ChooseMusic.xaml       # Book/song chooser dialog
│   ├── PdfMetaData.cs         # WPF-specific PDF metadata
│   └── BrowsePanel.cs         # ListView with filtering/sorting
├── Tests/                     # Unit tests (WPF-dependent)
└── WpfApp/                    # Additional WPF components
```

---

## Target Architecture (Avalonia + PDFtoImage)

### Technology Stack

| Component | Technology | Platform Support |
|-----------|-----------|------------------|
| UI Framework | Avalonia 11.x | Windows, macOS, Linux |
| PDF Rendering | PDFtoImage (SkiaSharp + Pdfium) | Cross-platform |
| Bitmap Images | Avalonia.Media.Imaging | Cross-platform |
| Settings | TBD (JSON file or Avalonia preferences) | Cross-platform |

### New Project Structure

```
SheetMusicViewer/
├── SheetMusicViewer/          # Legacy WPF Application (Windows-only)
├── SheetMusicLib/             # NEW: Platform-agnostic core library
│   ├── PdfMetaDataCore.cs     # Portable metadata reading
│   ├── TOCEntry.cs            # Table of contents
│   ├── Favorite.cs            # Favorites
│   └── InkStrokeClass.cs      # Ink data (serialization only)
├── AvaloniaTests/             # NEW: Avalonia implementation
│   ├── ChooseMusicWindow.cs   # Avalonia book chooser
│   ├── BrowseControl.cs       # Virtualized ListView
│   └── Tests/                 # Avalonia UI tests
├── Tests/                     # Core library tests
└── WpfApp/                    # Additional WPF components
```

---

## Migration Strategy

### Phase 1: Extract Platform-Agnostic Core ✅ COMPLETED

**Goal:** Separate business logic from WPF/WinRT dependencies

**Created:** `SheetMusicLib` - A portable .NET 8 class library

#### Key Components Extracted

| Component | Description |
|-----------|-------------|
| `PdfMetaDataCore.cs` | Metadata reading/writing without PDF rendering |
| `PdfMetaDataReadResult` | Platform-agnostic metadata container |
| `IPdfDocumentProvider` | Interface for PDF page count |
| `IExceptionHandler` | Interface for error handling |
| `TOCEntry`, `Favorite`, `InkStrokeClass` | Data models |
| `PdfVolumeInfoBase` | Volume information base class |

#### Abstraction Pattern

```csharp
// SheetMusicLib/PdfMetaDataCore.cs - Platform-agnostic interface
public interface IPdfDocumentProvider
{
    Task<int> GetPageCountAsync(string pdfFilePath);
}

// SheetMusicViewer/PdfMetaData.cs - WPF implementation
public class WpfPdfDocumentProvider : IPdfDocumentProvider
{
    public async Task<int> GetPageCountAsync(string pdfFilePath)
    {
        var pdfDoc = await PdfDocument.LoadFromFileAsync(file);
        return (int)pdfDoc.PageCount;
    }
}

// AvaloniaTests/... - Avalonia implementation uses PDFtoImage
public class AvaloniaPdfDocumentProvider : IPdfDocumentProvider
{
    public Task<int> GetPageCountAsync(string pdfFilePath)
    {
        return Task.FromResult(PDFtoImage.Conversion.GetPageCount(pdfFilePath));
    }
}
```

### Phase 2: Replace PDF Rendering ✅ COMPLETED

**Goal:** Replace Windows.Data.Pdf with cross-platform PDFtoImage

#### PDFtoImage Integration

```xml
<!-- AvaloniaTests.csproj -->
<PackageReference Include="PDFtoImage" Version="5.0.0" />
<PackageReference Include="SkiaSharp" Version="3.116.1" />
```

#### Rendering Implementation

```csharp
// AvaloniaTests/ChooseMusicWindow.cs
private async Task<Bitmap> GetPdfThumbnailAsync(PdfMetaDataReadResult pdfMetaData)
{
    return await Task.Run(() =>
    {
        var pdfPath = pdfMetaData.GetFullPathFileFromVolno(0);
        
        // Handle rotation
        var rotation = firstVolume.Rotation switch
        {
            1 => PDFtoImage.PdfRotation.Rotate90,
            2 => PDFtoImage.PdfRotation.Rotate180,
            3 => PDFtoImage.PdfRotation.Rotate270,
            _ => PDFtoImage.PdfRotation.Rotate0
        };
        
        using var pdfStream = File.OpenRead(pdfPath);
        using var skBitmap = Conversion.ToImage(pdfStream, page: 0, 
            options: new RenderOptions(
                Width: ThumbnailWidth, 
                Height: ThumbnailHeight,
                Rotation: rotation));
        
        // Convert SkiaSharp bitmap to Avalonia bitmap
        using var data = skBitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Seek(0, SeekOrigin.Begin);
        
        return new Avalonia.Media.Imaging.Bitmap(stream);
    });
}
```

### Phase 3: Rebuild UI Components ✅ COMPLETED

**Goal:** Create Avalonia equivalents of WPF UI components

#### Component Mapping

| WPF Component | Avalonia Component | Status |
|--------------|-------------------|--------|
| `ChooseMusic.xaml` (Window) | `ChooseMusicWindow.cs` | ✅ |
| `BrowsePanel` (ListView) | `BrowseControl.cs` | ✅ |
| `MyContentControl` | `BookItemCache` + StackPanel.Tag | ✅ |
| `MyTreeViewItem` | `FavoriteItem` + StackPanel.Tag | ✅ |
| `TabControl` | `TabControl` | ✅ Same API |
| `ListBox` with `WrapPanel` | `ListBox` with `FuncTemplate<Panel>` | ✅ |

#### Key UI Differences

**WPF WrapPanel in ListBox:**
```csharp
// WPF - XAML
<ListBox.ItemsPanel>
    <ItemsPanelTemplate>
        <WrapPanel/>
    </ItemsPanelTemplate>
</ListBox.ItemsPanel>
```

**Avalonia WrapPanel in ListBox:**
```csharp
// Avalonia - Code
var wrapPanelFactory = new FuncTemplate<Panel?>(() => new WrapPanel
{
    Orientation = Orientation.Horizontal
});
_lbBooks.ItemsPanel = wrapPanelFactory;
```

**WPF Custom ContentControl:**
```csharp
// WPF
internal class MyContentControl : ContentControl
{
    public PdfMetaData pdfMetaDataItem;
}
```

**Avalonia Pattern (using Tag):**
```csharp
// Avalonia - Store data in control's Tag property
var sp = new StackPanel { Tag = bookItemCache };
// ... add children ...
listBox.Items.Add(sp);

// Retrieve on selection
if (listBox.SelectedItem is StackPanel sp && sp.Tag is BookItemCache cache)
{
    var metadata = cache.Metadata;
}
```

### Phase 4: Cloud File Handling ✅ COMPLETED

**Goal:** Handle OneDrive/cloud-only files without triggering downloads

```csharp
// AvaloniaTests/ChooseMusicWindow.cs
if (SkipCloudOnlyFiles)
{
    var fileInfo = new FileInfo(pdfPath);
    const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;
    const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    
    var attrs = fileInfo.Attributes;
    bool isCloudOnly = (attrs & RecallOnDataAccess) == RecallOnDataAccess ||
                       (attrs & RecallOnOpen) == RecallOnOpen ||
                       (attrs & FileAttributes.Offline) == FileAttributes.Offline;
    
    if (isCloudOnly)
    {
        throw new IOException($"Cloud-only file, skipping: {pdfPath}");
    }
}
```

---

## Feature Comparison

### Completed Features

| Feature | WPF | Avalonia | Notes |
|---------|-----|----------|-------|
| Books Tab | ✅ | ✅ | Thumbnails, filtering, sorting |
| Favorites Tab | ✅ | ✅ | List with thumbnails |
| Query Tab | ✅ | ✅ | BrowseControl with columns |
| PDF Thumbnails | ✅ | ✅ | PDFtoImage rendering |
| Multi-volume PDFs | ✅ | ✅ | Via SheetMusicLib |
| BMK File Reading | ✅ | ✅ | XML and JSON support |
| Cloud File Detection | ✅ | ✅ | Skip OneDrive placeholders |

### Pending Features

| Feature | WPF | Avalonia | Notes |
|---------|-----|----------|-------|
| PDF Page Viewing | ✅ | ⏳ | Need to implement viewer |
| Ink Annotations | ✅ | ⏳ | Avalonia ink canvas |
| Settings Persistence | ✅ | ⏳ | Need JSON/preferences |
| Folder Browser | ✅ | ⏳ | Use StorageProvider API |
| Touch Gestures | ✅ | ⏳ | Avalonia gestures |
| Playlists | ⏳ | ⏳ | Placeholder in both |

---

## API Migration Reference

### PDF Document Loading

| Operation | WPF (Windows.Data.Pdf) | Avalonia (PDFtoImage) |
|-----------|------------------------|----------------------|
| Open PDF | `PdfDocument.LoadFromFileAsync(file)` | `File.OpenRead(path)` |
| Get Page | `pdfDoc.GetPage(pageNo)` | N/A (render directly) |
| Render | `page.RenderToStreamAsync(stream)` | `Conversion.ToImage(stream, page)` |
| Page Count | `pdfDoc.PageCount` | `Conversion.GetPageCount(path)` |

### Image Types

| Operation | WPF | Avalonia |
|-----------|-----|----------|
| Bitmap type | `BitmapImage` | `Avalonia.Media.Imaging.Bitmap` |
| Create from stream | `bmi.StreamSource = stream` | `new Bitmap(stream)` |
| Image control | `System.Windows.Controls.Image` | `Avalonia.Controls.Image` |

### Control Types

| WPF | Avalonia | Notes |
|-----|----------|-------|
| `Window` | `Window` | Same |
| `TabControl` | `TabControl` | Same |
| `ListBox` | `ListBox` | Same |
| `ComboBox` | `ComboBox` | Same |
| `Button` | `Button` | Same |
| `TextBlock` | `TextBlock` | Same |
| `StackPanel` | `StackPanel` | Same |
| `Grid` | `Grid` | Same |
| `TreeView` | `TreeView` | Same |
| `ContextMenu` | `ContextMenu` | Same |
| `FolderBrowserDialog` | `StorageProvider.OpenFolderPickerAsync` | Different API |
| `ContentControl` | Use `Tag` property | Pattern change |

---

## Testing Strategy

### Unit Tests (SheetMusicLib)
- BMK file parsing (XML and JSON)
- TOC entry handling
- Volume info management
- Path calculations

### Integration Tests (AvaloniaTests)
- UI component rendering
- PDF thumbnail generation
- Filter and sort operations
- Tab navigation

### Manual Tests
- Cross-platform execution (Windows, macOS, Linux)
- Cloud file handling
- Large library performance

---

## Build Configuration

### SheetMusicLib (Portable)
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>disable</Nullable>
  </PropertyGroup>
</Project>
```

### AvaloniaTests (Cross-platform)
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\SheetMusicLib\SheetMusicLib.csproj" />
    <PackageReference Include="Avalonia" Version="11.3.9" />
    <PackageReference Include="Avalonia.Desktop" Version="11.3.9" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.3.9" />
    <PackageReference Include="PDFtoImage" Version="5.0.0" />
    <PackageReference Include="SkiaSharp" Version="3.116.1" />
  </ItemGroup>
</Project>
```

### SheetMusicViewer (Legacy WPF)
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\SheetMusicLib\SheetMusicLib.csproj" />
    <PackageReference Include="Microsoft.Windows.Compatibility" Version="8.0.11" />
  </ItemGroup>
</Project>
```

---

## Performance Considerations

### Parallel BMK Loading
The new `PdfMetaDataCore` supports parallel loading of BMK files:

```csharp
// SheetMusicLib/PdfMetaDataCore.cs
public static async Task<(List<PdfMetaDataReadResult>, List<string>)> 
    LoadAllPdfMetaDataFromDiskAsync(
        string rootMusicFolder,
        IPdfDocumentProvider pdfDocumentProvider,
        IExceptionHandler exceptionHandler = null,
        bool useParallelLoading = true)  // Enable parallel loading
```

### Thumbnail Caching
Thumbnails are cached on the metadata object to avoid re-rendering:

```csharp
// SheetMusicLib/PdfMetaDataCore.cs
public async Task<T> GetOrCreateThumbnailAsync<T>(Func<Task<T>> thumbnailFactory)
{
    if (ThumbnailCache is T cached)
        return cached;
    
    var thumbnail = await thumbnailFactory();
    ThumbnailCache = thumbnail;
    return thumbnail;
}
```

### UI Virtualization
The Avalonia `BrowseControl` uses a virtualized `ListBox` with on-demand container customization to handle large datasets efficiently.

---

## Known Issues and Workarounds

### 1. Avalonia ListBox Item Styling
Default ListBoxItem has padding that affects item appearance:

```csharp
// Remove default padding
_lbBooks.ItemContainerTheme = new ControlTheme(typeof(ListBoxItem))
{
    Setters =
    {
        new Setter(ListBoxItem.PaddingProperty, new Thickness(0)),
        new Setter(ListBoxItem.MarginProperty, new Thickness(0)),
    }
};
```

### 2. Context Menu on Custom Containers
When using custom Grid containers in ListBox, context menu must be attached to the ListBox, not individual items.

### 3. Cloud File Attributes
Windows-specific file attributes for OneDrive detection may not work on other platforms. Consider platform-specific implementations.

---

## Next Steps

1. **Implement PDF Page Viewer** - Port `PdfViewerWindow` functionality
2. **Add Ink Canvas Support** - Investigate Avalonia ink alternatives
3. **Implement Settings** - JSON-based settings storage
4. **Add Folder Picker** - Use Avalonia `StorageProvider` API
5. **Touch/Gesture Support** - Port manipulation handlers
6. **Create macOS/Linux builds** - Test cross-platform deployment
7. **Performance optimization** - Profile large library loading

---

## References

- [Avalonia Documentation](https://docs.avaloniaui.net/)
- [PDFtoImage NuGet](https://www.nuget.org/packages/PDFtoImage)
- [SkiaSharp](https://github.com/mono/SkiaSharp)
- [WPF to Avalonia Migration Guide](https://docs.avaloniaui.net/docs/get-started/wpf)
