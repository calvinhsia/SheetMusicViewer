using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;
using System.IO;

namespace AvaloniaTests.Tests;

/// <summary>
/// Base class for all Avalonia test classes providing common test utilities and helpers
/// </summary>
public class TestBase
{
    /// <summary>
    /// Gets the TestContext which provides information about and functionality for the current test run.
    /// </summary>
    public TestContext TestContext { get; set; }

    /// <summary>
    /// Detects if tests are running in a CI/CD environment (GitHub Actions, Azure DevOps, etc.)
    /// </summary>
    public static bool IsCI
    {
        get
        {
            return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) ||
                   !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")) ||
                   !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TF_BUILD")) ||
                   !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JENKINS_HOME")) ||
                   !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TRAVIS")) ||
                   !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CIRCLECI"));
        }
    }

    /// <summary>
    /// Called before each test method runs
    /// </summary>
    [TestInitialize]
    public virtual void TestInitialize()
    {
        var ciStatus = IsCI ? "CI" : "Local";
        LogMessage($"Starting test {TestContext.TestName} ({ciStatus})");
    }

    /// <summary>
    /// Called after each test method completes
    /// </summary>
    [TestCleanup]
    public virtual void TestCleanup()
    {
        LogMessage($"Completed test {TestContext.TestName} - Result: {TestContext.CurrentTestOutcome}");
    }

    /// <summary>
    /// Logs a message to both the test output and debug trace
    /// </summary>
    protected void LogMessage(string message)
    {
        var timestampedMessage = $"{DateTime.Now:HH:mm:ss.fff} {message}";
        TestContext?.WriteLine(timestampedMessage);
        
        if (Debugger.IsAttached)
        {
            Debug.WriteLine(timestampedMessage);
        }
        else
        {
            Trace.WriteLine(timestampedMessage);
        }
    }

    /// <summary>
    /// Skips the test if running in CI environment with an explanatory message
    /// </summary>
    protected void SkipIfCI(string reason = "Test requires local environment (SkiaSharp native library compatibility)")
    {
        if (IsCI)
        {
            Assert.Inconclusive($"Test skipped in CI environment: {reason}");
        }
    }

    /// <summary>
    /// Creates a minimal valid PDF file for testing purposes
    /// This is a simple PDF 1.4 structure with one blank page
    /// </summary>
    protected string CreateTestPdf(string customPath = null)
    {
        var path = customPath ?? Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.pdf");
        
        var pdfContent = @"%PDF-1.4
1 0 obj
<<
/Type /Catalog
/Pages 2 0 R
>>
endobj
2 0 obj
<<
/Type /Pages
/Kids [3 0 R]
/Count 1
>>
endobj
3 0 obj
<<
/Type /Page
/Parent 2 0 R
/Resources <<
/Font <<
/F1 <<
/Type /Font
/Subtype /Type1
/BaseFont /Helvetica
>>
>>
>>
/MediaBox [0 0 612 792]
/Contents 4 0 R
>>
endobj
4 0 obj
<<
/Length 44
>>
stream
BT
/F1 12 Tf
100 700 Td
(Test PDF) Tj
ET
endstream
endobj
xref
0 5
0000000000 65535 f 
0000000009 00000 n 
0000000058 00000 n 
0000000115 00000 n 
0000000317 00000 n 
trailer
<<
/Size 5
/Root 1 0 R
>>
startxref
410
%%EOF";
        
        File.WriteAllText(path, pdfContent);
        LogMessage($"Created test PDF at: {path}");
        return path;
    }

    /// <summary>
    /// Safely deletes a file, ignoring any errors
    /// </summary>
    protected void SafeDeleteFile(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                LogMessage($"Deleted test file: {path}");
            }
        }
        catch (Exception ex)
        {
            LogMessage($"Warning: Could not delete file {path}: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets environment information for diagnostic purposes
    /// </summary>
    protected string GetEnvironmentInfo()
    {
        return $"OS: {Environment.OSVersion}, " +
               $"64-bit: {Environment.Is64BitOperatingSystem}, " +
               $"CI: {IsCI}, " +
               $"User: {Environment.UserName}, " +
               $"Machine: {Environment.MachineName}";
    }
}
