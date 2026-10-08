#if LINUX
using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Kronos;
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

        // Update command
        var updateCommand = new Command("update", "Check for and apply updates");
        var checkOption = new Option<bool>("--check", "Only check for updates, don't apply");
        var forceOption = new Option<bool>("--force", "Force update even if version appears same");
        updateCommand.AddOption(checkOption);
        updateCommand.AddOption(forceOption);
        updateCommand.SetHandler(async (check, force) =>
        {
            await HandleUpdateAsync(check, force);
        }, checkOption, forceOption);

        // Version command
        var versionCommand = new Command("version", "Show version information");
        versionCommand.SetHandler(() =>
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
            Console.WriteLine($"Kronos {version}");
            Console.WriteLine($"Runtime: {Environment.Version}");
            Console.WriteLine($"OS: {RuntimeInformation.OSDescription}");
            Console.WriteLine($"Architecture: {RuntimeInformation.ProcessArchitecture}");
        });

        // Self-update command (internal)
        var selfUpdateCommand = new Command("self-update", "Internal: apply downloaded update")
        {
            IsHidden = true
        };
        var pathOption = new Option<string>("--path", "Path to downloaded update package");
        selfUpdateCommand.AddOption(pathOption);
        selfUpdateCommand.SetHandler(async (path) =>
        {
            await HandleSelfUpdateAsync(path);
        }, pathOption);

        rootCommand.AddCommand(updateCommand);
        rootCommand.AddCommand(versionCommand);
        rootCommand.AddCommand(selfUpdateCommand);

        // TODO: Implement other CLI commands (list, swap, reset, import)
        // These require porting the full game detection and DLL management logic

        return await rootCommand.InvokeAsync(args);
    }

    private static async Task HandleUpdateAsync(bool checkOnly, bool force)
    {
        var currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
        Log.Information("Current version: {Version}", currentVersion);

        var updateInfo = await Updater.CheckForUpdateAsync(currentVersion);
        if (updateInfo is null)
        {
            if (!force)
            {
                Log.Information("No updates available");
                return;
            }
            // Force mode: try to get latest anyway
            Log.Information("Force mode: attempting to fetch latest release...");
            // Would need to modify CheckForUpdateAsync to support force
        }

        Log.Information("Update available: v{Version} ({Size:N0} bytes)", updateInfo.Version, updateInfo.FileSize);
        if (!string.IsNullOrEmpty(updateInfo.ReleaseNotes))
        {
            Log.Information("Release notes:\n{Notes}", updateInfo.ReleaseNotes.Trim());
        }

        if (checkOnly)
        {
            Log.Information("Check-only mode: not applying update. Run 'kronos update' to apply.");
            return;
        }

        Console.Write("Apply update? [y/N]: ");
        var response = Console.ReadLine();
        if (!string.Equals(response, "y", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(response, "yes", StringComparison.OrdinalIgnoreCase))
        {
            Log.Information("Update cancelled by user");
            return;
        }

        var progress = new Progress<double>(p =>
        {
            Console.Write($"\rDownloading: {p:P1} ");
        });

        Log.Information("Downloading and applying update...");
        var success = await Updater.ApplyUpdateAsync(updateInfo, progress);
        Console.WriteLine(); // New line after progress

        if (success)
        {
            Log.Information("Update applied successfully! Please restart Kronos.");
            Environment.ExitCode = 0;
        }
        else
        {
            Log.Error("Update failed");
            Environment.ExitCode = 1;
        }
    }

    private static async Task HandleSelfUpdateAsync(string? packagePath)
    {
        if (string.IsNullOrEmpty(packagePath) || !File.Exists(packagePath))
        {
            Log.Error("Update package not found: {Path}", packagePath);
            Environment.ExitCode = 1;
            return;
        }

        // This would be called by an external updater process
        // For now, just extract and replace
        Log.Information("Applying self-update from: {Path}", packagePath);
        
        var tempDir = Path.Combine(Path.GetTempPath(), "KronosUpdate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            if (OperatingSystem.IsLinux())
            {
                var extractDir = Path.Combine(tempDir, "extract");
                Directory.CreateDirectory(extractDir);

                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "tar",
                        Arguments = $"-xzf \"{packagePath}\" -C \"{extractDir}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                process.Start();
                await process.WaitForExitAsync();
                if (process.ExitCode != 0)
                {
                    var error = await process.StandardError.ReadToEndAsync();
                    Log.Error("Extraction failed: {Error}", error);
                    Environment.ExitCode = 1;
                    return;
                }

                var newBinary = Directory.GetFiles(extractDir, "Kronos", SearchOption.AllDirectories).FirstOrDefault();
                if (newBinary is null)
                {
                    Log.Error("Kronos binary not found in package");
                    Environment.ExitCode = 1;
                    return;
                }

                var currentExe = Assembly.GetExecutingAssembly().Location;
                var backupPath = currentExe + ".bak";

                if (File.Exists(backupPath)) File.Delete(backupPath);
                File.Move(currentExe, backupPath);

                try
                {
                    File.Copy(newBinary, currentExe, true);
                    File.SetUnixFileMode(currentExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                                   UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                                   UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                    File.Delete(backupPath);
                    Log.Information("Self-update applied successfully");
                    Environment.ExitCode = 0;
                }
                catch
                {
                    if (File.Exists(backupPath))
                    {
                        if (File.Exists(currentExe)) File.Delete(currentExe);
                        File.Move(backupPath, currentExe);
                    }
                    throw;
                }
            }
            else if (OperatingSystem.IsWindows())
            {
                // Windows self-update would be similar but with zip extraction
                Log.Error("Windows self-update not yet implemented");
                Environment.ExitCode = 1;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Self-update failed");
            Environment.ExitCode = 1;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
#endif