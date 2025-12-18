using Microsoft.VisualStudio.TestTools.UnitTesting;
using SheetMusicLib;
using System;
using System.Text;
using System.Text.Json;

namespace AvaloniaTests.Tests;

/// <summary>
/// Unit tests for PortableInkStroke classes - the portable ink stroke format.
/// These tests verify ink stroke serialization/deserialization without requiring Avalonia UI.
/// </summary>
[TestClass]
public class PortableInkStrokeTests : TestBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    [TestMethod]
    [TestCategory("Unit")]
    public void PortableInkStroke_Serialization_RoundTrip()
    {
        // Arrange
        var stroke = new PortableInkStroke
        {
            Color = "#FF0000",
            Thickness = 3.5,
            Opacity = 0.8,
            IsHighlighter = false
        };
        stroke.Points.Add(new PortableInkPoint { X = 10, Y = 20 });
        stroke.Points.Add(new PortableInkPoint { X = 30, Y = 40 });
        stroke.Points.Add(new PortableInkPoint { X = 50, Y = 60 });

        // Act
        var json = JsonSerializer.Serialize(stroke, JsonOptions);
        LogMessage($"Serialized stroke:\n{json}");
        
        var deserialized = JsonSerializer.Deserialize<PortableInkStroke>(json);

        // Assert
        Assert.IsNotNull(deserialized);
        Assert.AreEqual(stroke.Color, deserialized.Color);
        Assert.AreEqual(stroke.Thickness, deserialized.Thickness);
        Assert.AreEqual(stroke.Opacity, deserialized.Opacity);
        Assert.AreEqual(stroke.IsHighlighter, deserialized.IsHighlighter);
        Assert.AreEqual(stroke.Points.Count, deserialized.Points.Count);
        Assert.AreEqual(10, deserialized.Points[0].X);
        Assert.AreEqual(20, deserialized.Points[0].Y);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void PortableInkStrokeCollection_Serialization_RoundTrip()
    {
        // Arrange
        var collection = new PortableInkStrokeCollection
        {
            CanvasWidth = 800,
            CanvasHeight = 600
        };

        var stroke1 = new PortableInkStroke { Color = "#000000", Thickness = 2.0 };
        stroke1.Points.Add(new PortableInkPoint { X = 100, Y = 100 });
        stroke1.Points.Add(new PortableInkPoint { X = 200, Y = 200 });
        collection.Strokes.Add(stroke1);

        var stroke2 = new PortableInkStroke { Color = "#FFFF00", Thickness = 15.0, IsHighlighter = true, Opacity = 0.5 };
        stroke2.Points.Add(new PortableInkPoint { X = 300, Y = 100 });
        stroke2.Points.Add(new PortableInkPoint { X = 400, Y = 100 });
        collection.Strokes.Add(stroke2);

        // Act
        var json = JsonSerializer.Serialize(collection, JsonOptions);
        LogMessage($"Serialized collection:\n{json}");
        
        var deserialized = JsonSerializer.Deserialize<PortableInkStrokeCollection>(json);

        // Assert
        Assert.IsNotNull(deserialized);
        Assert.AreEqual(collection.CanvasWidth, deserialized.CanvasWidth);
        Assert.AreEqual(collection.CanvasHeight, deserialized.CanvasHeight);
        Assert.AreEqual(2, deserialized.Strokes.Count);
        
        Assert.AreEqual("#000000", deserialized.Strokes[0].Color);
        Assert.AreEqual(2.0, deserialized.Strokes[0].Thickness);
        Assert.IsFalse(deserialized.Strokes[0].IsHighlighter);
        
        Assert.AreEqual("#FFFF00", deserialized.Strokes[1].Color);
        Assert.AreEqual(15.0, deserialized.Strokes[1].Thickness);
        Assert.IsTrue(deserialized.Strokes[1].IsHighlighter);
        Assert.AreEqual(0.5, deserialized.Strokes[1].Opacity);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void PortableInkStrokeCollection_ToBytes_CanBeDeserialized()
    {
        // Arrange
        var collection = new PortableInkStrokeCollection
        {
            CanvasWidth = 1024,
            CanvasHeight = 768
        };
        
        var stroke = new PortableInkStroke { Color = "#FF0000", Thickness = 2.5 };
        stroke.Points.Add(new PortableInkPoint { X = 50, Y = 100 });
        stroke.Points.Add(new PortableInkPoint { X = 150, Y = 200 });
        collection.Strokes.Add(stroke);

        // Act - Simulate how InkCanvasControl saves stroke data
        var json = JsonSerializer.Serialize(collection);
        var bytes = Encoding.UTF8.GetBytes(json);
        
        // Verify it starts with '{'
        Assert.AreEqual((byte)'{', bytes[0], "JSON bytes should start with '{'");
        
        // Deserialize from bytes
        var jsonFromBytes = Encoding.UTF8.GetString(bytes);
        var deserialized = JsonSerializer.Deserialize<PortableInkStrokeCollection>(jsonFromBytes);

        // Assert
        Assert.IsNotNull(deserialized);
        Assert.AreEqual(1, deserialized.Strokes.Count);
        Assert.AreEqual(1024, deserialized.CanvasWidth);
        Assert.AreEqual(768, deserialized.CanvasHeight);
        
        LogMessage($"Serialized to {bytes.Length} bytes");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void PortableInkPoint_DefaultValues()
    {
        // Arrange & Act
        var point = new PortableInkPoint();

        // Assert
        Assert.AreEqual(0, point.X);
        Assert.AreEqual(0, point.Y);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void PortableInkStroke_DefaultValues()
    {
        // Arrange & Act
        var stroke = new PortableInkStroke();

        // Assert
        Assert.IsNotNull(stroke.Points);
        Assert.AreEqual(0, stroke.Points.Count);
        Assert.AreEqual(2.0, stroke.Thickness, "Default thickness should be 2.0");
        Assert.AreEqual(1.0, stroke.Opacity, "Default opacity should be 1.0");
        Assert.IsFalse(stroke.IsHighlighter, "Default IsHighlighter should be false");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void PortableInkStrokeCollection_EmptyCollection_Serializes()
    {
        // Arrange
        var collection = new PortableInkStrokeCollection
        {
            CanvasWidth = 500,
            CanvasHeight = 400
        };

        // Act
        var json = JsonSerializer.Serialize(collection, JsonOptions);
        LogMessage($"Empty collection JSON:\n{json}");
        
        var deserialized = JsonSerializer.Deserialize<PortableInkStrokeCollection>(json);

        // Assert
        Assert.IsNotNull(deserialized);
        Assert.AreEqual(0, deserialized.Strokes.Count);
        Assert.AreEqual(500, deserialized.CanvasWidth);
        Assert.AreEqual(400, deserialized.CanvasHeight);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void PortableInkStroke_ManyPoints_HandledCorrectly()
    {
        // Arrange - Create stroke with many points (like a continuous line)
        var stroke = new PortableInkStroke { Color = "#000000", Thickness = 2.0 };
        
        for (int i = 0; i < 1000; i++)
        {
            stroke.Points.Add(new PortableInkPoint 
            { 
                X = i * 0.5, 
                Y = Math.Sin(i * 0.1) * 100 + 200
            });
        }

        // Act
        var json = JsonSerializer.Serialize(stroke);
        var deserialized = JsonSerializer.Deserialize<PortableInkStroke>(json);

        // Assert
        Assert.IsNotNull(deserialized);
        Assert.AreEqual(1000, deserialized.Points.Count);
        Assert.AreEqual(0, deserialized.Points[0].X);
        Assert.AreEqual(499.5, deserialized.Points[999].X);
        
        LogMessage($"Serialized 1000-point stroke to {json.Length} characters");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void InkStrokeClass_WithPortableData_RoundTrips()
    {
        // Arrange - Create InkStrokeClass with portable JSON data
        var collection = new PortableInkStrokeCollection
        {
            CanvasWidth = 640,
            CanvasHeight = 480
        };
        
        var stroke = new PortableInkStroke { Color = "#0000FF", Thickness = 3.0 };
        stroke.Points.Add(new PortableInkPoint { X = 10, Y = 20 });
        stroke.Points.Add(new PortableInkPoint { X = 100, Y = 200 });
        collection.Strokes.Add(stroke);
        
        var json = JsonSerializer.Serialize(collection);
        var bytes = Encoding.UTF8.GetBytes(json);
        
        var inkStrokeClass = new InkStrokeClass
        {
            Pageno = 5,
            InkStrokeDimension = new PortablePoint(640, 480),
            StrokeData = bytes
        };

        // Act - Simulate reading back the data
        var readJson = Encoding.UTF8.GetString(inkStrokeClass.StrokeData);
        var readCollection = JsonSerializer.Deserialize<PortableInkStrokeCollection>(readJson);

        // Assert
        Assert.AreEqual(5, inkStrokeClass.Pageno);
        Assert.AreEqual(640, inkStrokeClass.InkStrokeDimension.X);
        Assert.AreEqual(480, inkStrokeClass.InkStrokeDimension.Y);
        
        Assert.IsNotNull(readCollection);
        Assert.AreEqual(1, readCollection.Strokes.Count);
        Assert.AreEqual("#0000FF", readCollection.Strokes[0].Color);
        Assert.AreEqual(2, readCollection.Strokes[0].Points.Count);
        
        LogMessage("InkStrokeClass round-trip verified");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void PortableInkStroke_ColorParsing_ValidHexColors()
    {
        // Arrange
        var testCases = new[]
        {
            ("#FF0000", "Red"),
            ("#00FF00", "Green"),
            ("#0000FF", "Blue"),
            ("#000000", "Black"),
            ("#FFFFFF", "White"),
            ("#FFFF00", "Yellow"),
            ("#FFA500", "Orange")
        };

        foreach (var (hexColor, name) in testCases)
        {
            // Act
            var stroke = new PortableInkStroke { Color = hexColor };
            var json = JsonSerializer.Serialize(stroke);
            var deserialized = JsonSerializer.Deserialize<PortableInkStroke>(json);

            // Assert
            Assert.IsNotNull(deserialized);
            Assert.AreEqual(hexColor, deserialized.Color, $"Color {name} should round-trip correctly");
        }
        
        LogMessage($"Tested {testCases.Length} color values");
    }
}
