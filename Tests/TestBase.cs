using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Tests
{
    /// <summary>
    /// Base class for all test classes providing common test utilities and helpers
    /// </summary>
    public class TestBase
    {
        /// <summary>
        /// Creates a custom STA thread on which UI elements can run. Has execution context that allows asynchronous code to work
        /// </summary>
        public static async Task RunInSTAExecutionContextAsync(Func<Task> actionAsync, string description = "", int maxStackSize = 512 * 1024)
        {
            Dispatcher mySTADispatcher = null;
            var tcsGetExecutionContext = new TaskCompletionSource<int>();
            var myStaThread = new Thread(() =>
            {
                mySTADispatcher = Dispatcher.CurrentDispatcher;
                var syncContext = new DispatcherSynchronizationContext(mySTADispatcher);
                SynchronizationContext.SetSynchronizationContext(syncContext);
                tcsGetExecutionContext.SetResult(0);
                try
                {
                    Dispatcher.Run();
                }
                catch (ThreadAbortException)
                {
                }
                catch (Exception) { }
                Debug.WriteLine($"Thread done {description}");
            }, maxStackSize: maxStackSize)
            {
                IsBackground = true,
                Name = $"MySta{description}"
            };
            myStaThread.SetApartmentState(ApartmentState.STA);
            myStaThread.Start();
            await tcsGetExecutionContext.Task;
            var tcsCallerAction = new TaskCompletionSource<int>();
            if (mySTADispatcher == null)
            {
                throw new NullReferenceException(nameof(mySTADispatcher));
            }

            await mySTADispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await actionAsync();
                }
                catch (Exception ex)
                {
                    tcsCallerAction.SetException(ex);
                    return;
                }
                finally
                {
                    Debug.WriteLine($"User code done. Shutting down dispatcher {description}");
                    mySTADispatcher.InvokeShutdown();
                }
                Debug.WriteLine($"StaThreadTask done");
                tcsCallerAction.SetResult(0);
            });
            await tcsCallerAction.Task;
            Debug.WriteLine($"sta thread finished {description}");
        }

        public TestContext TestContext { get; set; }

        [TestInitialize]
        public void TestInitialize()
        {
            TestContext.WriteLine($"{DateTime.Now} Starting test {TestContext.TestName}");
        }

        public static string GetSheetMusicFolder()
        {
            var username = Environment.UserName;
            var folder = $@"C:\Users\{username}\OneDrive";
            if (!Directory.Exists(folder))
            {
                folder = @"d:\OneDrive";
            }
            return $@"{folder}\SheetMusic";
        }

        public void AddLogEntry(string msg)
        {
            var str = DateTime.Now.ToString("hh:mm:ss:fff") + " " + msg;
            TestContext.WriteLine(str);
            if (Debugger.IsAttached)
            {
                Debug.WriteLine(str);
            }
        }

        /// <summary>
        /// Creates a minimal valid PDF file for testing purposes
        /// This is a simple PDF 1.4 structure with one blank page
        /// </summary>
        protected async Task CreateMinimalTestPdfAsync(string path)
        {
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
            await File.WriteAllTextAsync(path, pdfContent);
        }
    }
}
