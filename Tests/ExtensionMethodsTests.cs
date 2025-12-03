using Microsoft.VisualStudio.TestTools.UnitTesting;
using SheetMusicViewer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Tests
{
    [TestClass]
    public class ExtensionMethodsTests : TestBase
    {
        #region FindIndexOfFirstGTorEQTo Tests

        [TestMethod]
        [TestCategory("Unit")]
        public void TestFindIndexOfFirstGTorEQTo_EmptyList_ReturnsMinusOne()
        {
            // Arrange
            var emptyList = new List<int>();

            // Act
            var result = emptyList.FindIndexOfFirstGTorEQTo(5);

            // Assert
            Assert.AreEqual(-1, result);
            AddLogEntry($"Empty list correctly returns -1");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TestFindIndexOfFirstGTorEQTo_ExactMatch_ReturnsCorrectIndex()
        {
            // Arrange
            var list = new List<int> { 1, 3, 5, 7, 9, 11 };

            // Act
            var result = list.FindIndexOfFirstGTorEQTo(5);

            // Assert
            Assert.AreEqual(2, result);
            Assert.AreEqual(5, list[result]);
            AddLogEntry($"Found exact match at index {result}");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TestFindIndexOfFirstGTorEQTo_NoExactMatch_ReturnsNextHigher()
        {
            // Arrange
            var list = new List<int> { 1, 3, 5, 7, 9, 11 };

            // Act
            var result = list.FindIndexOfFirstGTorEQTo(6);

            // Assert
            Assert.AreEqual(3, result);
            Assert.AreEqual(7, list[result]);
            AddLogEntry($"Found next higher value at index {result}");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TestFindIndexOfFirstGTorEQTo_KeyLessThanAll_ReturnsFirstIndex()
        {
            // Arrange
            var list = new List<int> { 5, 10, 15, 20, 25 };

            // Act
            var result = list.FindIndexOfFirstGTorEQTo(3);

            // Assert
            Assert.AreEqual(0, result);
            Assert.AreEqual(5, list[result]);
            AddLogEntry($"Key less than all elements returns index 0");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TestFindIndexOfFirstGTorEQTo_KeyGreaterThanAll_ReturnsCount()
        {
            // Arrange
            var list = new List<int> { 5, 10, 15, 20, 25 };

            // Act
            var result = list.FindIndexOfFirstGTorEQTo(30);

            // Assert
            Assert.AreEqual(list.Count, result);
            AddLogEntry($"Key greater than all elements returns count: {result}");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TestFindIndexOfFirstGTorEQTo_StringList()
        {
            // Arrange
            var list = new List<string> { "apple", "banana", "cherry", "date", "elderberry" };

            // Act
            var result = list.FindIndexOfFirstGTorEQTo("cherry");

            // Assert
            Assert.AreEqual(2, result);
            Assert.AreEqual("cherry", list[result]);
            AddLogEntry($"String list correctly finds 'cherry' at index {result}");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TestFindIndexOfFirstGTorEQTo_NegativeNumbers()
        {
            // Arrange
            var list = new List<int> { -50, -30, -10, 0, 10, 30, 50 };

            // Act
            var result = list.FindIndexOfFirstGTorEQTo(-15);

            // Assert
            Assert.AreEqual(2, result);
            Assert.AreEqual(-10, list[result]);
            AddLogEntry($"Negative numbers handled correctly, found {list[result]} at index {result}");
        }

        #endregion

        #region AddMnuItem Tests

        [TestMethod]
        [TestCategory("Unit")]
        public async Task TestAddMnuItem_CreatesMenuItem()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                await Task.Yield();
                // Arrange
                var contextMenu = new ContextMenu();
                var handlerCalled = false;
                RoutedEventHandler handler = (s, e) => { handlerCalled = true; };

                // Act
                var menuItem = contextMenu.AddMnuItem("Test Item", "Test Tooltip", handler);

                // Assert
                Assert.IsNotNull(menuItem);
                Assert.AreEqual("Test Item", menuItem.Header);
                Assert.AreEqual("Test Tooltip", menuItem.ToolTip);
                Assert.AreEqual(1, contextMenu.Items.Count);
                AddLogEntry($"MenuItem created successfully");
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task TestAddMnuItem_HandlerIsAttached()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                await Task.Yield();
                // Arrange
                var contextMenu = new ContextMenu();
                var handlerCalled = false;
                RoutedEventHandler handler = (s, e) => { handlerCalled = true; };

                // Act
                var menuItem = contextMenu.AddMnuItem("Test Item", "Test Tooltip", handler);
                menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

                // Assert
                Assert.IsTrue(handlerCalled);
                AddLogEntry($"Handler was called successfully");
            });
        }

        #endregion
    }
}
