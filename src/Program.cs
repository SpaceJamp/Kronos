#if LINUX
using System;
using System.CommandLine;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Kronos;
using Kronos.Data;
using Kronos.Helpers;
using Serilog;

namespace Kronos;

/// <summary>
/// Linux entry point for Kronos CLI.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Initialize logger for console
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .CreateLogger();

        var rootCommand = new RootCommand("Kronos - DLSS/FSR/XeSS DLL Swapper for Linux");

        var listCommand = new Command("list", "List detected games and their current DLL versions");
        var swapCommand = new Command("swap", "Swap DLL version for a game");
        var updateCommand = new Command("update", "Update manifest from remote");
        var resetCommand = new Command("reset", "Reset DLL to original version for a game");
        var importCommand = new Command("import", "Import DLLs from a folder");

        rootCommand.AddCommand(listCommand);
        rootCommand.AddCommand(swapCommand);
        rootCommand.AddCommand(updateCommand);
        rootCommand.AddCommand(resetCommand);
        rootCommand.AddCommand(importCommand);

        // TODO: Implement CLI commands for Linux
        // This is a placeholder - the core logic would need to be ported

        Log.Information("Kronos CLI for Linux - Not yet fully implemented");
        Log.Information("This build provides core libraries for Linux. GUI is Windows-only (WinUI 3).");
        
        return await rootCommand.InvokeAsync(args);
    }
}
#endif