using Microsoft.VisualStudio.TestTools.UnitTesting;
using SheetMusicViewer;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Tests
{
    /// <summary>
    /// Unit tests for PdfViewerWindow class
    /// Note: Many PdfViewerWindow methods are UI-focused and require integration testing
    /// These tests focus on testable business logic and helper methods
    /// </summary>
    [TestClass]
    public class PdfViewerWindowTests : TestBase
    {
        #region Static Helper Method Tests

        [TestMethod]
        [TestCategory("Unit")]
        public void TestGetDistanceBetweenPoints_SamePoint()
        {
            // Arrange
            var p1 = new Point(10, 10);
            var p2 = new Point(10, 10);

            // Act
            var distance = PdfViewerWindow.GetDistanceBetweenPoints(p1, p2);

            // Assert
            Assert.AreEqual(0, distance, 0.001);
            AddLogEntry($"Distance between same points: {distance}");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TestGetDistanceBetweenPoints_HorizontalDistance()
        {
            // Arrange
            var p1 = new Point(0, 0);
            var p2 = new Point(3, 0);

            // Act
            var distance = PdfViewerWindow.GetDistanceBetweenPoints(p1, p2);

            // Assert
            Assert.AreEqual(3, distance, 0.001);
            AddLogEntry($"Horizontal distance: {distance}");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TestGetDistanceBetweenPoints_VerticalDistance()
        {
            // Arrange
            var p1 = new Point(0, 0);
            var p2 = new Point(0, 4);

            // Act
            var distance = PdfViewerWindow.GetDistanceBetweenPoints(p1, p2);

            // Assert
            Assert.AreEqual(4, distance, 0.001);
            AddLogEntry($"Vertical distance: {distance}");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TestGetDistanceBetweenPoints_DiagonalDistance()
        {
            // Arrange
            var p1 = new Point(0, 0);
            var p2 = new Point(3, 4);

            // Act
            var distance = PdfViewerWindow.GetDistanceBetweenPoints(p1, p2);

            // Assert
            Assert.AreEqual(5, distance, 0.001); // 3-4-5 triangle
            AddLogEntry($"Diagonal distance: {distance}");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TestGetDistanceBetweenPoints_NegativeCoordinates()
        {
            // Arrange
            var p1 = new Point(-5, -5);
            var p2 = new Point(-2, -1);

            // Act
            var distance = PdfViewerWindow.GetDistanceBetweenPoints(p1, p2);

            // Assert
            Assert.AreEqual(5, distance, 0.001); // 3-4-5 triangle
            AddLogEntry($"Distance with negative coords: {distance}");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TestGetDistanceBetweenPoints_LargeValues()
        {
            // Arrange
            var p1 = new Point(1000, 2000);
            var p2 = new Point(1300, 2400);

            // Act
            var distance = PdfViewerWindow.GetDistanceBetweenPoints(p1, p2);

            // Assert
            Assert.AreEqual(500, distance, 0.001); // 300-400-500 triangle
            AddLogEntry($"Distance with large values: {distance}");
        }

        #endregion

        #region Property Tests

        [TestMethod]
        [TestCategory("Unit")]
        public async Task TestPdfViewerWindow_Construction()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                await Task.Yield();
                // Arrange & Act
                var window = new PdfViewerWindow(rootFolderForTesting: null, UseSettings: false);

                // Assert
                Assert.IsNotNull(window);
                Assert.IsFalse(window.IsTesting);
                Assert.IsNull(window.currentPdfMetaData);
                Assert.AreEqual(0, window.MaxPageNumber);
                AddLogEntry($"PdfViewerWindow constructed successfully");
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task TestPdfViewerWindow_MaxPageNumber_NoPdfLoaded()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                await Task.Yield();
                // Arrange
                var window = new PdfViewerWindow(rootFolderForTesting: null, UseSettings: false);

                // Act
                var maxPage = window.MaxPageNumber;

                // Assert
                Assert.AreEqual(0, maxPage);
                AddLogEntry($"MaxPageNumber with no PDF: {maxPage}");
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task TestPdfViewerWindow_NumPagesPerView_SinglePage()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                await Task.Yield();
                // Arrange
                var window = new PdfViewerWindow(rootFolderForTesting: null, UseSettings: false);
                window.Show2Pages = false;

                // Act
                var numPages = window.NumPagesPerView;

                // Assert
                Assert.AreEqual(1, numPages);
                AddLogEntry($"NumPagesPerView in single page mode: {numPages}");
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task TestPdfViewerWindow_NumPagesPerView_TwoPages()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                await Task.Yield();
                // Arrange
                var window = new PdfViewerWindow(rootFolderForTesting: null, UseSettings: false);
                window.Show2Pages = true;

                // Act
                var numPages = window.NumPagesPerView;

                // Assert
                Assert.AreEqual(2, numPages);
                AddLogEntry($"NumPagesPerView in two page mode: {numPages}");
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task TestPdfViewerWindow_PdfTitle_NoPdf()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                await Task.Yield();
                // Arrange
                var window = new PdfViewerWindow(rootFolderForTesting: null, UseSettings: false);

                // Act
                var title = window.PdfTitle;

                // Assert
                Assert.AreEqual(string.Empty, title);
                AddLogEntry($"PdfTitle with no PDF: '{title}'");
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task TestPdfViewerWindow_PdfUIEnabled_NoPdf()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                await Task.Yield();
                // Arrange
                var window = new PdfViewerWindow(rootFolderForTesting: null, UseSettings: false);

                // Act
                var enabled = window.PdfUIEnabled;

                // Assert
                Assert.IsFalse(enabled);
                AddLogEntry($"PdfUIEnabled with no PDF: {enabled}");
            });
        }

        #endregion

        #region State Management Tests

        [TestMethod]
        [TestCategory("Unit")]
        public async Task TestPdfViewerWindow_TouchCount_Increments()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                await Task.Yield();
                // Arrange
                var window = new PdfViewerWindow(rootFolderForTesting: null, UseSettings: false);
                var initialCount = window.TouchCount;

                // Act
                window.TouchCount++;
                window.TouchCount++;

                // Assert
                Assert.AreEqual(initialCount + 2, window.TouchCount);
                AddLogEntry($"TouchCount incremented to: {window.TouchCount}");
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task TestPdfViewerWindow_CurrentPageNumber_SetsValue()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                await Task.Yield();
                // Arrange
                var window = new PdfViewerWindow(rootFolderForTesting: null, UseSettings: false);

                // Act
                window.CurrentPageNumber = 42;

                // Assert
                Assert.AreEqual(42, window.CurrentPageNumber);
                AddLogEntry($"CurrentPageNumber set to: {window.CurrentPageNumber}");
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task TestPdfViewerWindow_Show2Pages_ToggleState()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                await Task.Yield();
                // Arrange
                var window = new PdfViewerWindow(rootFolderForTesting: null, UseSettings: false);
                var initialState = window.Show2Pages;

                // Act
                window.Show2Pages = !initialState;

                // Assert
                Assert.AreEqual(!initialState, window.Show2Pages);
                AddLogEntry($"Show2Pages toggled to: {window.Show2Pages}");
            });
        }

        #endregion

        #region Exception Handling Tests

        [TestMethod]
        [TestCategory("Unit")]
        public async Task TestPdfViewerWindow_OnException_RaisesEvent()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                await Task.Yield();
                // Arrange
                var testDir = Path.Combine(Path.GetTempPath(), "PdfViewerWindowTests");
                Directory.CreateDirectory(testDir);
                
                var mockMessageBoxService = new TestMessageBoxService();
                var window = new PdfViewerWindow(rootFolderForTesting: testDir, UseSettings: false)
                {
                    _messageBoxService = mockMessageBoxService
                };
                
                var eventRaised = false;
                string capturedMessage = null;
                Exception capturedException = null;

                window.PdfExceptionEvent += (sender, args) =>
                {
                    eventRaised = true;
                    capturedMessage = args.Message;
                    capturedException = args.ErrorException;
                };

                var testException = new InvalidOperationException("Test error");

                // Act
                window.OnException("Test message", testException);

                // Assert
                Assert.IsTrue(eventRaised, "PdfExceptionEvent should have been raised");
                Assert.AreEqual("Test message", capturedMessage);
                Assert.AreSame(testException, capturedException);
                Assert.AreEqual(1, mockMessageBoxService.ShowCallCount, "MessageBox should have been shown once");
                Assert.IsTrue(mockMessageBoxService.LastMessage.Contains("Test message"), "MessageBox should contain the test message");
                Assert.IsTrue(mockMessageBoxService.LastMessage.Contains(testException.ToString()), "MessageBox should contain the exception details");
                AddLogEntry($"Exception event raised with message: {capturedMessage}");
                AddLogEntry($"MessageBox shown {mockMessageBoxService.ShowCallCount} time(s) with message: {mockMessageBoxService.LastMessage}");
                
                // Cleanup
                try
                {
                    if (Directory.Exists(testDir))
                    {
                        Directory.Delete(testDir, recursive: true);
                    }
                }
                catch { }
            });
        }

        #endregion

        #region Integration Test Markers

        // The following tests would require full integration testing with UI infrastructure:
        // - TestPdfViewerWindow_LoadPdfFileAndShowAsync
        // - TestPdfViewerWindow_ShowPageAsync
        // - TestPdfViewerWindow_NavigateAsync
        // - TestPdfViewerWindow_ChkFavToggled
        // - TestPdfViewerWindow_ChkInkToggled
        // - TestPdfViewerWindow_BtnRotate_Click
        // - TestPdfViewerWindow_OnPreviewKeyDown
        // - TestPdfViewerWindow_DpPage_ManipulationDelta
        // - TestPdfViewerWindow_DpPage_MouseWheel
        // - TestPdfViewerWindow_OnRenderSizeChanged

        #endregion
    }
}
