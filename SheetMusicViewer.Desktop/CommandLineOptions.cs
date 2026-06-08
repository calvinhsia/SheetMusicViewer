using System;
using System.IO;

namespace SheetMusicViewer.Desktop;

/// <summary>
/// Parses and holds command-line options for the application.
/// Supported options:
///   --root &lt;path&gt;   Override the root music folder
/// </summary>
public class CommandLineOptions
{
    /// <summary>The parsed options from the current process command line.</summary>
    public static CommandLineOptions Current { get; private set; } = new();

    /// <summary>Override for the root music folder (null = use saved setting).</summary>
    public string? RootFolder { get; private init; }

    private CommandLineOptions() { }

    public static void Parse(string[] args)
    {
        var options = new CommandLineOptions();
        string? rootFolder = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--root":
                    if (i + 1 < args.Length)
                    {
                        rootFolder = args[++i];
                        // if the path is invalid or doesn't exist, fail fast
                        if (!Directory.Exists(rootFolder))
                        {
                            Console.Error.WriteLine($"The specified root folder does not exist: {rootFolder}");
                            Environment.Exit(1);
                        }
                    }
                    else
                    {
                        Console.Error.WriteLine("--root requires a path argument");
                    }
                    break;

                // Future options can be added here
            }
        }

        Current = new CommandLineOptions { RootFolder = rootFolder };
    }
}
