# Copilot Instructions for SheetMusicViewer

## CRITICAL: Avoid Reading Diff/Temp Files Instead of Real Files

### Problem Description
Visual Studio creates temporary comparison files in `AppData\Local\Temp\TFSTemp` folders when showing diffs or browsing code. These files contain side-by-side views of old and new code or multiple versions. AI assistants can accidentally read these diff files instead of the real source files.

### Symptoms of Reading Diff Files
When an AI assistant reads diff/temp files instead of actual source, you'll see:

- **Reporting duplicate code that doesn't exist** - Side-by-side diff lines (red/green) are misinterpreted as duplicate code blocks in the actual file
- **Incorrect analysis of current file state** - Seeing both old and new versions and thinking both exist simultaneously
- **Confusion about what changes have been applied** - Reporting that code needs to be removed when the actual file is already correct
- **Failed edit operations** - Attempting to "fix" problems that aren't real, resulting in edit_file tool failures
- **Contradictory analysis** - User says "that code doesn't exist" but the AI insists it saw it

### Solution
1. **Always check file paths carefully**
   - If a path contains `TFSTemp`, `vctmp`, `Temp\`, or similar temporary directory markers, **it's likely a diff file, not actual source**
   - Diff files often have patterns like: `vctmp[numbers]_[numbers].Browse.[hexadecimal].cs`

2. **Verify with user when confused**
   - If your analysis seems contradictory or the user reports "no difference" after you've identified an issue
   - Ask: "Are you viewing a diff file or comparison view in Visual Studio?"

3. **Use explicit file paths from project structure**
   - When using tools like `get_file`, use actual project file paths (e.g., `SheetMusicViewer/PdfMetaData.cs`)
   - Don't rely solely on paths from the IDE's open files list - these may include temp files

4. **Cross-reference with project structure**
   - Use `get_projects_in_solution` and `get_files_in_project` to verify actual file locations
   - Compare suspect paths against known project structure

### Red Flags (Warning Signs)
Watch for these indicators that you might be reading a diff file:

- **Path patterns**: 
  - Contains `TFSTemp`, `vctmp`, `.Browse.`, or `Temp\`
  - Has hexadecimal suffixes (e.g., `555d73e2`, `62b24157`)
  - Includes version numbers in unusual places (e.g., `vctmp36700_970223`)

- **Code patterns**:
  - Seeing apparently duplicate code blocks with only slight differences
  - Code appears twice with similar but not identical content
  - Unusual formatting or side-by-side layout

- **User feedback patterns**:
  - User says "no difference" after you've identified an issue
  - User corrects you saying "that's a diff file"
  - User reports code doesn't exist that you clearly see

### Example from Real Session
**Problematic paths (these are diff files, not source):**
```
..\..\..\AppData\Local\Temp\TFSTemp\vctmp36700_970223.Browse.555d73e2.cs
..\..\..\AppData\Local\Temp\TFSTemp\vctmp36700_7595.Browse.62b24157.cs
```

**Correct paths (these are actual source files):**
```
Tests/UnitTest1.cs
SheetMusicViewer/PdfMetaData.cs
SheetMusicViewer/PdfViewerWindow.xaml.cs
```

### Correct Action When Suspecting Diff File
1. **Stop** - Don't proceed with analysis based on suspect file
2. **Verify** - Use `get_file` with the actual project path
3. **Confirm** - Ask user if they have a diff view open
4. **Correct** - Base your analysis only on actual source files from the project structure

---

## Project-Specific Guidance

### Technology Stack
- **Framework**: WPF targeting .NET 8, C# 14.0
- **PDF Rendering**: Windows.Data.Pdf (Windows Runtime API)
- **Testing**: MSTest with custom STA thread execution context
- **Version Control**: Git with Visual Studio TFS integration

### Known Issues
- **Windows.Data.Pdf Non-Determinism**: The `PdfPage.RenderToStreamAsync()` method has inherent non-deterministic behavior. Each render of the same page may produce slightly different pixel-level results due to font rendering heuristics, anti-aliasing decisions, and internal optimization state. This is a known API limitation, not a code bug.

### Important Patterns
- **Stream Position Management**: Always call `strm.Seek(0)` after write operations and before read operations when working with `InMemoryRandomAccessStream`
- **STA Threading**: UI tests must run on STA threads using `RunInSTAExecutionContextAsync` helper method
- **PDF Metadata**: The `PdfMetaData` class manages bookmarks, favorites, ink annotations, and multi-volume PDF sets

### Code Style
- Follow existing conventions in the codebase
- Use existing libraries; only add new dependencies if absolutely necessary
- Avoid adding comments unless they match existing style or explain complex logic
- Make minimal modifications to achieve the goal
