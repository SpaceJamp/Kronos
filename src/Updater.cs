using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Kronos.Data.GitHub;
using Kronos.Helpers;
using Serilog;

namespace Kronos;

/// <summary>
/// Cross-platform update manager for Kronos.
/// Checks GitHub Releases for new versions and performs self-update.
/// </summary>
internal static class Updater
{
    private const string GitHubApiUrl = "https://api.github.com/repos/SpaceJamp/Kronos/releases/latest";
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(5),
        DefaultRequestHeaders =
        {
            { "User-Agent", "Kronos-Updater" },
            { "Accept", "application/vnd.github.v3+json" }
        }
    };

    /// <summary>
    /// Checks for updates and returns update info if available.
    /// </summary>
    public static async Task<UpdateInfo?> CheckForUpdateAsync(string currentVersion)
    {
        try
        {
            Log.Information("Checking for updates...");
            var response = await HttpClient.GetStringAsync(GitHubApiUrl);
            var release = JsonSerializer.Deserialize<GitHubRelease>(response, SourceGenerationContext.Default.GitHubRelease);

            if (release is null || string.IsNullOrEmpty(release.TagName))
            {
                Log.Warning("Could not parse release information");
                return null;
            }

            var latestVersion = release.TagName.TrimStart('v');
            if (Version.TryParse(latestVersion, out var latest) &&
                Version.TryParse(currentVersion, out var current) &&
                latest <= current)
            {
                Log.Information("Already on latest version ({Current})", currentVersion);
                return null;
            }

            var asset = FindMatchingAsset(release.Assets);
            if (asset is null)
            {
                Log.Warning("No matching asset found for this platform");
                return null;
            }

            return new UpdateInfo
            {
                Version = latestVersion,
                ReleaseNotes = release.Body ?? string.Empty,
                DownloadUrl = asset.BrowserDownloadUrl,
                AssetName = asset.Name,
                Sha256 = asset.Sha256 ?? string.Empty,
                FileSize = asset.Size
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to check for updates");
            return null;
        }
    }

    /// <summary>
    /// Finds the matching asset for the current platform/architecture.
    /// </summary>
    private static GitHubAsset? FindMatchingAsset(GitHubAsset[] assets)
    {
        var rid = GetRuntimeIdentifier();
        var prefix = OperatingSystem.IsWindows() ? "Kronos-" : "kronos-";
        var suffix = OperatingSystem.IsWindows() ? "-portable.zip" : $"-{rid}.tar.gz";

        foreach (var asset in assets)
        {
            if (asset.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                asset.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return asset;
            }
        }

        // Fallback: try to find any asset with the RID
        foreach (var asset in assets)
        {
            if (asset.Name.Contains(rid, StringComparison.OrdinalIgnoreCase))
            {
                return asset;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the .NET runtime identifier for the current platform.
    /// </summary>
    private static string GetRuntimeIdentifier()
    {
        if (OperatingSystem.IsWindows())
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "win-x64",
                Architecture.Arm64 => "win-arm64",
                _ => "win-x64"
            };
        }
        else if (OperatingSystem.IsLinux())
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "linux-x64",
                Architecture.Arm64 => "linux-arm64",
                Architecture.Arm => "linux-arm",
                _ => "linux-x64"
            };
        }
        return "unknown";
    }

    /// <summary>
    /// Downloads and applies the update.
    /// </summary>
    public static async Task<bool> ApplyUpdateAsync(UpdateInfo updateInfo, IProgress<double>? progress = null)
    {
        try
        {
            Log.Information("Downloading update {Version} ({Size:N0} bytes)...", updateInfo.Version, updateInfo.FileSize);

            var tempDir = Path.Combine(Path.GetTempPath(), "KronosUpdate", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var downloadPath = Path.Combine(tempDir, updateInfo.AssetName);

            // Download with progress
            using var downloadStream = await HttpClient.GetStreamAsync(updateInfo.DownloadUrl);
            using var fileStream = File.Create(downloadPath);

            var buffer = new byte[81920];
            long totalRead = 0;
            int read;
            while ((read = await downloadStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read));
                totalRead += read;
                progress?.Report(updateInfo.FileSize > 0 ? (double)totalRead / updateInfo.FileSize : 0);
            }

            // Verify SHA256 if provided
            if (!string.IsNullOrEmpty(updateInfo.Sha256))
            {
                Log.Information("Verifying SHA256...");
                var computed = await ComputeSha256Async(downloadPath);
                if (!string.Equals(computed, updateInfo.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Error("SHA256 mismatch! Expected: {Expected}, Got: {Actual}", updateInfo.Sha256, computed);
                    return false;
                }
                Log.Information("SHA256 verified");
            }

            // Apply update based on platform
            if (OperatingSystem.IsWindows())
            {
                return await ApplyWindowsUpdateAsync(downloadPath, tempDir);
            }
            else if (OperatingSystem.IsLinux())
            {
                return await ApplyLinuxUpdateAsync(downloadPath, tempDir);
            }

            Log.Error("Unsupported platform for auto-update");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to apply update");
            return false;
        }
    }

    /// <summary>
    /// Applies update on Windows: extracts portable zip and replaces files.
    /// </summary>
    private static async Task<bool> ApplyWindowsUpdateAsync(string zipPath, string tempDir)
    {
        try
        {
            var extractDir = Path.Combine(tempDir, "extract");
            Directory.CreateDirectory(extractDir);

            Log.Information("Extracting update...");
            ZipFile.ExtractToDirectory(zipPath, extractDir, true);

            var exePath = Assembly.GetExecutingAssembly().Location;
            var exeDir = Path.GetDirectoryName(exePath)!;

            // Find Kronos.exe in extracted files
            var newExe = Directory.GetFiles(extractDir, "Kronos.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (newExe is null)
            {
                Log.Error("Kronos.exe not found in update package");
                return false;
            }

            var newExeDir = Path.GetDirectoryName(newExe)!;

            // Copy all files from extracted directory to app directory
            foreach (var file in Directory.GetFiles(newExeDir, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(newExeDir, file);
                var destPath = Path.Combine(exeDir, relativePath);
                var destDir = Path.GetDirectoryName(destPath)!;
                Directory.CreateDirectory(destDir);
                File.Copy(file, destPath, true);
            }

            Log.Information("Update applied. Restart required.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to apply Windows update");
            return false;
        }
    }

    /// <summary>
    /// Applies update on Linux: replaces the single binary.
    /// </summary>
    private static async Task<bool> ApplyLinuxUpdateAsync(string tarGzPath, string tempDir)
    {
        try
        {
            var extractDir = Path.Combine(tempDir, "extract");
            Directory.CreateDirectory(extractDir);

            Log.Information("Extracting update...");
            // Use tar command for .tar.gz extraction
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "tar",
                    Arguments = $"-xzf \"{tarGzPath}\" -C \"{extractDir}\"",
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
                Log.Error("tar extraction failed: {Error}", error);
                return false;
            }

            // Find the Kronos binary
            var newBinary = Directory.GetFiles(extractDir, "Kronos", SearchOption.AllDirectories).FirstOrDefault();
            if (newBinary is null)
            {
                // Try without extension
                newBinary = Directory.GetFiles(extractDir, "Kronos*", SearchOption.AllDirectories).FirstOrDefault();
            }
            if (newBinary is null)
            {
                Log.Error("Kronos binary not found in update package");
                return false;
            }

            var currentExe = Assembly.GetExecutingAssembly().Location;
            var backupPath = currentExe + ".bak";

            // Backup current binary
            if (File.Exists(backupPath))
                File.Delete(backupPath);
            File.Move(currentExe, backupPath);

            try
            {
                // Copy new binary
                File.Copy(newBinary, currentExe, true);
                
                // Make executable (Linux only)
                if (OperatingSystem.IsLinux())
                {
                    File.SetUnixFileMode(currentExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                                   UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                                   UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }

                // Remove backup on success
                File.Delete(backupPath);
                
                Log.Information("Update applied. Restart required.");
                return true;
            }
            catch
            {
                // Restore backup on failure
                if (File.Exists(backupPath))
                {
                    if (File.Exists(currentExe))
                        File.Delete(currentExe);
                    File.Move(backupPath, currentExe);
                }
                throw;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to apply Linux update");
            return false;
        }
    }

    /// <summary>
    /// Computes SHA256 hash of a file.
    /// </summary>
    private static async Task<string> ComputeSha256Async(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = await sha256.ComputeHashAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <summary>
/// Information about an available update.
/// </summary>
internal sealed class UpdateInfo
{
    public string Version { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string AssetName { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long FileSize { get; set; }
}

/// <summary>
/// GitHub Release DTO.
/// </summary>
internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("assets")]
    public GitHubAsset[] Assets { get; set; } = Array.Empty<GitHubAsset>();
}

/// <summary>
/// GitHub Release Asset DTO.
/// </summary>
internal sealed class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }
}