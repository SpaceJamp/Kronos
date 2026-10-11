using System;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Kronos;

// TODO: Test portable app.
// TODO: Clean portable temp path on launch

/*
 * For notes on where data is stored please see https://github.com/beeradmoore/dlss-swapper/wiki/Local-Data-Structure 
 */
static class Storage
{
    static string? _storagePath;
    static string? _overriddenStoragePath;

    /// <summary>
    /// Points storage at a specific directory, overriding where it would otherwise live.
    /// </summary>
    /// <remarks>
    /// Exists for the tests, which must never read or write a real installation's settings.json: these
    /// tests delete files and simulate failed writes, and pointing them at %LOCALAPPDATA%\Kronos would be
    /// genuinely destructive. Pass null to go back to the normal location.
    ///
    /// Setting this discards any previously resolved path, so it works whether or not the real one has
    /// already been computed.
    /// </remarks>
    internal static void OverrideStoragePath(string? path)
    {
        _overriddenStoragePath = path;
        _storagePath = null;
    }

#if PORTABLE && DEBUG
    //public static string StoragePath => _storagePath ??= Path.Combine(AppContext.BaseDirectory, "StoredData", "DEBUG", Guid.NewGuid().ToString());
    public static string StoragePath => _storagePath ??= _overriddenStoragePath ?? Path.Combine(AppContext.BaseDirectory, "StoredData", "DEBUG");
#elif PORTABLE && !DEBUG
    public static string StoragePath => _storagePath ??= _overriddenStoragePath ?? Path.Combine(AppContext.BaseDirectory, "StoredData");
#elif !PORTABLE && DEBUG
    public static string StoragePath => _storagePath ??= _overriddenStoragePath ?? Path.Combine(GetAppDataPath(), "Kronos", "DEBUG");
#elif !PORTABLE && !DEBUG
    public static string StoragePath => _storagePath  ??= _overriddenStoragePath ?? Path.Combine(GetAppDataPath(), "Kronos");
#endif


    static string GetAppDataPath()
    {
        // Cross-platform app data path
        // On Windows: %LOCALAPPDATA%
        // On Linux: ~/.local/share (or $XDG_DATA_HOME)
        // On macOS: ~/Library/Application Support
        if (OperatingSystem.IsWindows())
        {
            return Environment.ExpandEnvironmentVariables("%LOCALAPPDATA%");
        }
        else if (OperatingSystem.IsLinux())
        {
            var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (!string.IsNullOrEmpty(xdgDataHome))
                return xdgDataHome;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }
        else if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support");
        }
        // Fallback
        return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    static Storage()
    {
        // Create directories if they doesn't exist.
        //CreateDirectoryIfNotExists(GetTemp());
        CreateDirectoryIfNotExists(GetStorageFolder());
        CreateDirectoryIfNotExists(GetDynamicJsonFolder());
        CreateDirectoryIfNotExists(GetImageCachePath());
    }

    public static string GetTemp()
    {
#if PORTABLE
        var path = Path.Combine(StoragePath, "temp");
        CreateDirectoryIfNotExists(path);
#else
        var path = Path.Combine(Path.GetTempPath(), "Kronos");
        CreateDirectoryIfNotExists(path);
#endif
        return path;
    }

    public static string GetStorageFolder()
    {
        return StoragePath;
    }

    public static string GetDynamicJsonFolder()
    {
        return Path.Combine(StoragePath, "json");
    }

    public static string GetUpdatesFolder()
    {
        return Path.Combine(GetTemp(), "updates");
    }

    public static string GetDBPath()
    {
        CreateDirectoryIfNotExists(StoragePath);
        return Path.Combine(StoragePath, "kronos.db");
    }

    public static string GetImageCachePath()
    {
        return Path.Combine(StoragePath, "image_cache");
    }

    public static string GetReleasesPath()
    {
        return Path.Combine(GetDynamicJsonFolder(), "releases.json");
    }

    public static string GetManifestPath()
    {
        return Path.Combine(GetDynamicJsonFolder(), "manifest.json");
    }

    public static string GetImportedManifestPath()
    {
        return Path.Combine(GetDynamicJsonFolder(), "imported_manifest.json");
    }

    /// <summary>
    /// When given a file path it will make the directory structure so that file is ready to be created in. A directory should not be passed to this. Use CreateDirectoryIfNotExists instead for that.
    /// </summary>
    /// <param name="path">File path</param>
    /// <returns>True if the directory could be created</returns>
    public static bool CreateDirectoryForFileIfNotExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Logger.Error("A path should not be empty in CreateDirectoryForFileIfNotExists");
            return false;
        }

        if (Directory.Exists(path))
        {
            Logger.Error("A directory should not be passed to CreateDirectoryForFileIfNotExists");
            return false;
        }
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
        {
            Logger.Error("A directory should not be empty in CreateDirectoryForFileIfNotExists");
            return false;
        }
        return CreateDirectoryIfNotExists(directory);
    }

    /// <summary>
    /// Creates a directory if it doesn't already exist.
    /// </summary>
    /// <param name="directory">Directory to be created</param>
    /// <returns>True if the directory could be created</returns>
    public static bool CreateDirectoryIfNotExists(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        try
        {
            if (Directory.Exists(directory) == false)
            {
                Directory.CreateDirectory(directory);
            }
            return true;
        }
        catch (Exception err)
        {
            Logger.Error(err);
            return false;
        }
    }

    /// <summary>
    /// Saves the current settings object to settings.json in the apps dynamic json folder.
    /// </summary>
    /// <param name="settings">Settings object to be saved</param>
    /// <returns>Task</returns>
    internal static void SaveSettingsJson(Settings settings)
    {
        var settingsFile = Path.Combine(GetDynamicJsonFolder(), "settings.json");

        // Serialized to a temporary file and then moved into place, never written over the real one.
        //
        // FileMode.Create truncates to zero the instant the file is opened, so anything that goes wrong
        // after that point - the disk filling, a virus scanner holding a handle, the process being killed
        // - leaves a 0-byte settings.json. On the next launch LoadSettingsJson cannot parse that, returns
        // null, and Settings.FromJson treats null as "first run" and writes the defaults over it. The user
        // loses their theme, language, AllowUntrusted choice, logging level, grid width, ignored paths and
        // window position, with nothing on screen saying so. Settings are saved on every change, so there
        // are many opportunities for this, not one rare one.
        //
        // A temp file plus Move means the real file is either the old one or the new one, never a
        // half-written one. File.Replace would additionally need the destination to already exist, which
        // it does not on first run, hence the create-or-replace choice.
        var tempFile = settingsFile + ".tmp";

        try
        {
            using (var stream = File.Open(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, settings, SourceGenerationContext.Default.Settings);
                // Flushed to disk before the rename. Without this the rename can be durable while the
                // contents are not, which is the same corruption by a different route.
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(settingsFile))
            {
                File.Replace(tempFile, settingsFile, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempFile, settingsFile);
            }
        }
        catch (Exception err)
        {
            Logger.Error(err);
            // The temporary file is deliberately left alone only if it is genuinely the new settings; if
            // the write failed it is partial and must not be mistaken for the real thing on the next read.
            TryDeleteFile(tempFile);
        }
    }

    /// <summary>
    /// Deletes a file, swallowing any failure. For cleanup after a write that has already failed, where a
    /// second exception would hide the first and there is nothing useful to do about either.
    /// </summary>
    internal static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception err)
        {
            Logger.Error(err);
        }
    }

    /// <summary>
    /// Loads settings from settings.json in the apps dynamic json folder.
    /// </summary>
    /// <returns>Settings object, or null if it could not be loaded</returns>
    internal static Settings? LoadSettingsJson()
    {
        var settingsFile = Path.Combine(GetDynamicJsonFolder(), "settings.json");

        // If the settings file doesn't exist we return null to default it elsewhere.
        if (File.Exists(settingsFile) == false)
        {
            return null;
        }

        try
        {
            using (var stream = File.OpenRead(settingsFile))
            {
                return JsonSerializer.Deserialize(stream, SourceGenerationContext.Default.Settings);
            }
        }
        catch (Exception err)
        {
            Logger.Error(err);

            // Kept, not deleted. Returning null here is not neutral: Settings.FromJson reads null as "first
            // run" and writes the defaults straight over the file, so a file that merely failed to parse
            // is turned into defaults and the original content is gone. Preserving it means a user whose
            // settings are damaged still has the file, and a fixed build can read it back.
            PreserveDamagedSettings(settingsFile);
            return null;
        }
    }

    /// <summary>
    /// Copies a settings file that could not be parsed to a timestamped .corrupt file, once per session.
    /// </summary>
    /// <remarks>
    /// Best effort throughout: this runs while recovering from a failure, so it must not be able to fail
    /// in a way that loses the original. The copy is taken before anything else can overwrite the file.
    /// </remarks>
    static void PreserveDamagedSettings(string settingsFile)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var backup = $"{settingsFile}.corrupt-{stamp}";

        try
        {
            File.Copy(settingsFile, backup, overwrite: false);
            Logger.Warning($"settings.json could not be read and was copied to {backup}. The original is kept.");
        }
        catch (Exception err)
        {
            Logger.Error(err);
        }
    }
}
