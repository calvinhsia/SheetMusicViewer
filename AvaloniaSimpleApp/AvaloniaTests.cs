using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

namespace AvaloniaSimpleApp;

[TestClass]
public class AvaloniaTests
{
    [TestMethod]
    [TestCategory("Integration")]
    public async Task TestAvaloniaPdfStressTest()
    {
        await Task.Run(() =>
        {
            // Build and start the Avalonia application
            var app = Program.BuildAvaloniaApp();
            app.StartWithClassicDesktopLifetime(new string[0]);
        });
    }
}
