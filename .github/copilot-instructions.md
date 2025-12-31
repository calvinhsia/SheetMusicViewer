# Copilot Instructions for SheetMusicViewer

## CRITICAL: No Summary Markdown Files

**DO NOT create summary or documentation markdown files unless explicitly requested by the user.**

Examples of files NOT to create:
- `*_SUMMARY.md`
- `*_COMPLETE.md`
- `*_REPORT.md`
- `*_DOCUMENTATION.md`
- `TEST_COVERAGE_*.md`
- `GITHUB_ACTIONS_*.md`

When completing work:
- Report results directly in conversation
- Update existing documentation if needed
- Don't create new markdown files for status/summary

---

## Running Tests

### Exclude Manual Tests
When running tests, **always exclude Manual tests** unless explicitly asked to run them. Manual tests may:
- Modify source files (e.g., regenerating `GettingStarted.pdf` and `GettingStarted.json`)
- Require user interaction or special setup
- Take a long time to complete
- Have side effects on the repository

**Correct way to run tests:**
```powershell
# Run only Unit tests (recommended for quick verification)
dotnet test --filter "TestCategory=Unit"

# Run Unit and Integration tests (excludes Manual)
dotnet test --filter "TestCategory!=Manual"

# Run specific test by name
dotnet test --filter "FullyQualifiedName~TestMethodName"
```

**Do NOT run without a filter** - this will include Manual tests that may modify files.

### Test Categories
- `[TestCategory("Unit")]` - Fast, isolated unit tests. Safe to run anytime.
- `[TestCategory("Integration")]` - Tests that use file system or multiple components. Safe to run.
- `[TestCategory("Manual")]` - Tests that modify source files, require user interaction, or have special setup. **Only run when explicitly requested.**

### Examples of Manual Tests
- `GenerateGettingStartedPdf` - Regenerates the sample PDF and JSON files
- Tests marked `[Ignore]` - Disabled tests that may have issues
- Stress tests or performance tests

---

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

## Bug Fixes and Unit Tests

### Always Add Unit Tests for Bug Fixes
When fixing bugs, especially:
- **Re-entrancy issues** - Add tests that verify the guard pattern works correctly, including exception scenarios
- **Collection modification errors** - Add tests that simulate the cascading event pattern
- **UI event handling** - Add tests for keyboard shortcuts (Enter/Escape) and event handlers

### Re-entrancy Guard Pattern
When using boolean flags to prevent re-entrancy, **always use try/finally**:

```csharp
// CORRECT - Flag is always reset
private void OnSomeEvent()
{
    if (_isHandling) return;
    _isHandling = true;
    
    try
    {
        // ... code that might trigger re-entrant calls or throw ...
    }
    finally
    {
        _isHandling = false;
    }
}

// WRONG - Flag stays stuck if exception occurs
private void OnSomeEvent()
{
    if (_isHandling) return;
    _isHandling = true;
    
    // ... code ...
    
    _isHandling = false; // Never reached if exception thrown!
}
```

### Test Categories
- Use `[TestCategory("Unit")]` for unit tests
- Use `[TestCategory("Manual")]` for tests requiring user interaction or special setup
