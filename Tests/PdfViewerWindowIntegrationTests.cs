using Microsoft.VisualStudio.TestTools.UnitTesting;
using SheetMusicViewer;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace Tests
{
    /// <summary>
    /// Integration tests for PdfViewerWindow that test UI and service interactions
    /// </summary>
    [TestClass]
    public class PdfViewerWindowIntegrationTests : TestBase
    {
        private string testDirectory;

        [TestInitialize]
        public void Setup()
        {
            testDirectory = Path.Combine(Path.GetTempPath(), $"PdfViewerIntegrationTests_{Guid.NewGuid()}");
            Directory.CreateDirectory(testDirectory);
            AddLogEntry($"Test directory created: {testDirectory}");
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(testDirectory))
            {
                try
                {
                    Directory.Delete(testDirectory, recursive: true);
                    AddLogEntry($"Test directory cleaned up: {testDirectory}");
                }
                catch (Exception ex)
                {
                    AddLogEntry($"Failed to cleanup test directory: {ex.Message}");
                }
            }
        }

        #region Service Integration Tests

        [TestMethod]
        [TestCategory("Integration")]
        public async Task TestPdfViewerWindow_WithMockMessageBoxService_HandlesException()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                await Task.Yield();
                
                // Arrange
                var mockMessageBoxService = new TestMessageBoxService();
                var window = new PdfViewerWindow(rootFolderForTesting: testDirectory, UseSettings: false)
                {
                    _messageBoxService = mockMessageBoxService
                };

                var testException = new InvalidOperationException("Test exception for integration test");
                var exceptionRaised = false;
                Exception capturedEx = null;

                window.PdfExceptionEvent += (sender, args) =>
                {
                    exceptionRaised = true;
                    capturedEx = args.ErrorException;
                };

                // Act
                window.OnException("Integration test error", testException);

                // Assert
                Assert.IsTrue(exceptionRaised, "Exception event should be raised");
                Assert.AreSame(testException, capturedEx);
                Assert.AreEqual(1, mockMessageBoxService.ShowCallCount);
                Assert.IsTrue(mockMessageBoxService.LastMessage.Contains("Integration test error"));
                AddLogEntry($"MessageBox service integration successful: {mockMessageBoxService.ShowCallCount} calls");
            });
        }

        [TestMethod]
        [TestCategory("Integration")]
        public async Task TestPdfViewerWindow_DefaultConstruction_InitializesWithoutCrashing()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                await Task.Yield();
                
                // Act
                var window = new PdfViewerWindow(rootFolderForTesting: testDirectory, UseSettings: false);

                // Assert
                Assert.IsNotNull(window);
                Assert.AreEqual(testDirectory, window._RootMusicFolder);
                Assert.AreEqual(0, window.MaxPageNumber);
                Assert.IsFalse(window.PdfUIEnabled);
                AddLogEntry($"Window constructed successfully with root: {window._RootMusicFolder}");
            });
        }

        [TestMethod]
        [TestCategory("Integration")]
        public async Task TestPdfViewerWindow_PropertyNotifications_WorkCorrectly()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                await Task.Yield();
                
                // Arrange
                var window = new PdfViewerWindow(rootFolderForTesting: testDirectory, UseSettings: false);
                var propertyChangedCount = 0;
                string lastPropertyName = null;

                window.PropertyChanged += (sender, args) =>
                {
                    propertyChangedCount++;
                    lastPropertyName = args.PropertyName;
                };

                // Act - Change various properties
                window.CurrentPageNumber = 5;

                // Assert
                Assert.IsTrue(propertyChangedCount > 0, "PropertyChanged should fire");
                AddLogEntry($"PropertyChanged fired {propertyChangedCount} time(s), last property: {lastPropertyName}");
            });
        }

        #endregion

        #region Window Lifecycle Integration Tests

        [TestMethod]
        [TestCategory("Integration")]
        [Ignore("Requires interactive UI - use for manual testing only")]
        public async Task TestPdfViewerWindow_ShowAndClose_NoErrors()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                await Task.Yield();
                
                // Arrange
                var window = new PdfViewerWindow(rootFolderForTesting: testDirectory, UseSettings: false);
                var windowClosed = false;

                window.Closed += (sender, args) =>
                {
                    windowClosed = true;
                };

                // Act
                window.Show();
                await Task.Delay(100); // Let window initialize
                window.Close();
                await Task.Delay(100); // Let window close

                // Assert
                Assert.IsTrue(windowClosed, "Window should have closed");
                AddLogEntry($"Window lifecycle completed successfully");
            });
        }

        [TestMethod]
        [TestCategory("Integration")]
        public async Task TestPdfViewerWindow_MultipleTouchEvents_IncrementCounter()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                await Task.Yield();
                
                // Arrange
                var window = new PdfViewerWindow(rootFolderForTesting: testDirectory, UseSettings: false);
                var initialCount = window.TouchCount;

                // Act
                window.TouchCount++;
                window.TouchCount++;
                window.TouchCount++;

                // Assert
                Assert.AreEqual(initialCount + 3, window.TouchCount);
                AddLogEntry($"TouchCount properly incremented to {window.TouchCount}");
            });
        }

        #endregion

        #region Page View Mode Integration Tests

        [TestMethod]
        [TestCategory("Integration")]
        public async Task TestPdfViewerWindow_TogglePageMode_UpdatesNumPagesPerView()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                await Task.Yield();
                
                // Arrange
                var window = new PdfViewerWindow(rootFolderForTesting: testDirectory, UseSettings: false);
                window.Show2Pages = false;
                var propertyChangeCount = 0;

                window.PropertyChanged += (sender, args) =>
                {
                    if (args.PropertyName == nameof(window.NumPagesPerView))
                    {
                        propertyChangeCount++;
                    }
                };

                // Act - Toggle to 2-page mode
                window.Show2Pages = true;
                await Task.Delay(50);

                // Assert
                Assert.AreEqual(2, window.NumPagesPerView);
                Assert.IsTrue(propertyChangeCount > 0, "NumPagesPerView property should notify");
                AddLogEntry($"Page mode toggled successfully, NumPagesPerView={window.NumPagesPerView}");
            });
        }

        #endregion

        #region Distance Calculation Integration Test

        [TestMethod]
        [TestCategory("Integration")]
        public async Task TestPdfViewerWindow_DistanceCalculation_WithRealPoints()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                await Task.Yield();
                
                // Arrange - Simulate real touch/mouse points
                var point1 = new Point(100.5, 200.7);
                var point2 = new Point(400.3, 500.9);

                // Act
                var distance = PdfViewerWindow.GetDistanceBetweenPoints(point1, point2);

                // Assert
                Assert.IsTrue(distance > 0);
                var expectedDistance = Math.Sqrt(Math.Pow(400.3 - 100.5, 2) + Math.Pow(500.9 - 200.7, 2));
                Assert.AreEqual(expectedDistance, distance, 0.001);
                AddLogEntry($"Distance calculation accurate: {distance:F2} pixels");
            });
        }

        #endregion

        #region Event Handling Integration Tests

        [TestMethod]
        [TestCategory("Integration")]
        public async Task TestPdfViewerWindow_ExceptionEvent_FiresForSubscribers()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                await Task.Yield();
                
                // Arrange
                var window = new PdfViewerWindow(rootFolderForTesting: testDirectory, UseSettings: false)
                {
                    _messageBoxService = new TestMessageBoxService()
                };

                var eventFiredCount = 0;
                string lastMessage = null;
                Exception lastException = null;

                window.PdfExceptionEvent += (sender, args) =>
                {
                    eventFiredCount++;
                    lastMessage = args.Message;
                    lastException = args.ErrorException;
                };

                // Act - Trigger multiple exceptions
                window.OnException("Error 1", new Exception("Test 1"));
                window.OnException("Error 2", new Exception("Test 2"));

                // Assert
                Assert.AreEqual(2, eventFiredCount);
                Assert.AreEqual("Error 2", lastMessage);
                Assert.AreEqual("Test 2", lastException.Message);
                AddLogEntry($"Exception events handled correctly: {eventFiredCount} events");
            });
        }

        #endregion

        #region State Management Integration Tests

        [TestMethod]
        [TestCategory("Integration")]
        public async Task TestPdfViewerWindow_PageNumberChanges_TriggerPropertyChanged()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                await Task.Yield();
                
                // Arrange
                var window = new PdfViewerWindow(rootFolderForTesting: testDirectory, UseSettings: false);
                var pageNumberChanges = 0;

                window.PropertyChanged += (sender, args) =>
                {
                    if (args.PropertyName == nameof(window.CurrentPageNumber))
                    {
                        pageNumberChanges++;
                    }
                };

                // Act
                window.CurrentPageNumber = 10;
                window.CurrentPageNumber = 20;
                window.CurrentPageNumber = 30;

                // Assert
                Assert.IsTrue(pageNumberChanges >= 3, $"Should have at least 3 changes, got {pageNumberChanges}");
                Assert.AreEqual(30, window.CurrentPageNumber);
                AddLogEntry($"Page number changes tracked: {pageNumberChanges} notifications");
            });
        }

        [TestMethod]
        [TestCategory("Integration")]
        [Ignore("Requires interactive UI - use for manual testing only")]
        public async Task TestPdfViewerWindow_WithEmptyRootFolder_HandlesGracefully()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                await Task.Yield();
                
                // Arrange
                var emptyFolder = Path.Combine(testDirectory, "empty");
                Directory.CreateDirectory(emptyFolder);

                // Act
                var window = new PdfViewerWindow(rootFolderForTesting: emptyFolder, UseSettings: false);
                window.Show();
                await Task.Delay(100);

                // Assert
                Assert.IsNotNull(window);
                Assert.AreEqual(emptyFolder, window._RootMusicFolder);
                Assert.AreEqual(0, window.MaxPageNumber);
                AddLogEntry($"Empty folder handled gracefully");

                window.Close();
            });
        }

        #endregion

        #region Concurrent Operation Tests

        [TestMethod]
        [TestCategory("Integration")]
        public async Task TestPdfViewerWindow_RapidPropertyChanges_NoErrors()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                await Task.Yield();
                
                // Arrange
                var window = new PdfViewerWindow(rootFolderForTesting: testDirectory, UseSettings: false);
                var errors = 0;

                try
                {
                    // Act - Rapid property changes
                    for (int i = 0; i < 100; i++)
                    {
                        window.CurrentPageNumber = i;
                        window.Show2Pages = i % 2 == 0;
                        window.TouchCount++;
                    }
                }
                catch (Exception ex)
                {
                    errors++;
                    AddLogEntry($"Error during rapid changes: {ex.Message}");
                }

                // Assert
                Assert.AreEqual(0, errors, "Should handle rapid property changes without errors");
                AddLogEntry($"Rapid property changes completed successfully");
            });
        }

        #endregion
    }
}
