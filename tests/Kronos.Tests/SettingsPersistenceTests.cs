using System;
using System.IO;
using System.Linq;
using Kronos;
using Xunit;

namespace Kronos.Tests;

/// <summary>
/// Guards settings persistence against destroying the user's settings.
/// </summary>
/// <remarks>
/// Settings are the one thing a user cannot get back. They are not a cache and not a download - they are
/// choices they made, and there is no cloud copy. So the bar here is not "does saving work" but "can any
/// failure leave the user with worse settings than they had, and would they be told".
///
/// This file had no tests at all, which is how the following shipped: SaveSettingsJson opened the real
/// file with FileMode.Create, which truncates to zero the instant it opens. Anything failing after that
/// left a 0-byte file, LoadSettingsJson could not parse it, returned null, and FromJson read null as
/// "first run" and wrote the defaults over it. Theme, language, AllowUntrusted, logging level, grid
/// width, ignored paths and window position all reset silently. Settings save on every change, so there
/// were many chances to hit it.
///
/// Storage.StoragePath is redirected to a temporary directory for each test, because these tests must
/// never touch a real installation's settings.
///
/// In the StorageCollection, because overriding that static while other test classes read it made
/// DllMigration117Tests fail intermittently.
/// </remarks>
[Collection(StorageCollection.Name)]
public class SettingsPersistenceTests : IDisposable
{
    readonly string _root;

    public SettingsPersistenceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"kronos-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        // Redirected so a failing test cannot damage the real settings of whoever is running it.
        Storage.OverrideStoragePath(_root);

        // Normally created by Storage.CreateDirectories at startup. The atomic write needs it to already
        // exist, so a test that has not called that would fail for a reason that has nothing to do with
        // what it is testing.
        Directory.CreateDirectory(Storage.GetDynamicJsonFolder());
    }

    public void Dispose()
    {
        Storage.OverrideStoragePath(null);

        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    string SettingsPath => Path.Combine(Storage.GetDynamicJsonFolder(), "settings.json");

    [Fact]
    public void SavedSettingsAreReadBack()
    {
        var settings = new Settings { GridViewItemWidth = 321 };

        Storage.SaveSettingsJson(settings);
        var loaded = Storage.LoadSettingsJson();

        Assert.NotNull(loaded);
        Assert.Equal(321, loaded!.GridViewItemWidth);
    }

    [Fact]
    public void AFailedWriteLeavesThePreviousSettingsIntactRatherThanTruncatingThem()
    {
        // The regression, and the most important test in this file.
        //
        // GridViewItemWidth is the payload because it is a plain serialised value needing no other setup.
        // The old code opened settings.json with FileMode.Create, which truncates to zero on open, so any
        // failure after that left a 0-byte file and the next launch turned it into defaults.
        //
        // The write is made to fail by locking the *temp* file the atomic write has to create, which is the
        // same class of failure as a full disk or antivirus interference: the destination is never opened.
        // A read-only directory was tried first and does not reliably fail, because the account running the
        // tests may still be permitted to create files in it.
        var settings = new Settings { GridViewItemWidth = 321 };
        Storage.SaveSettingsJson(settings);

        Assert.True(new FileInfo(SettingsPath).Length > 0, "the saved settings should not be empty to begin with");

        // Occupy the temp path so SaveSettingsJson cannot create it.
        var tempPath = SettingsPath + ".tmp";
        using (new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            Storage.SaveSettingsJson(new Settings { GridViewItemWidth = 999 });
        }

        Assert.True(
            File.Exists(SettingsPath),
            "A failed save must not remove the existing settings file.");

        var reloaded = Storage.LoadSettingsJson();
        Assert.NotNull(reloaded);
        Assert.Equal(321, reloaded!.GridViewItemWidth);
    }

    [Fact]
    public void NoTemporaryFileIsLeftBehindByASave()
    {
        Storage.SaveSettingsJson(new Settings { GridViewItemWidth = 100 });

        var leftovers = Directory
            .EnumerateFiles(Storage.GetDynamicJsonFolder(), "*.tmp")
            .ToList();

        Assert.Empty(leftovers);
    }

    [Fact]
    public void SettingsThatCannotBeParsedAreKeptRatherThanOverwrittenWithDefaults()
    {
        // LoadSettingsJson returning null is not a neutral result: FromJson reads it as "first run" and
        // writes defaults. So a file that merely failed to parse gets turned into defaults and the
        // original content is destroyed. It has to be preserved instead.
        Directory.CreateDirectory(Storage.GetDynamicJsonFolder());
        const string damaged = "{ this is not valid json";
        File.WriteAllText(SettingsPath, damaged);

        var loaded = Storage.LoadSettingsJson();

        Assert.Null(loaded);

        // The damaged content must still be on disk, and a copy kept separately.
        Assert.Equal(damaged, File.ReadAllText(SettingsPath));

        var preserved = Directory
            .EnumerateFiles(Storage.GetDynamicJsonFolder(), "settings.json.corrupt-*")
            .ToList();

        Assert.NotEmpty(preserved);
        Assert.Equal(damaged, File.ReadAllText(preserved[0]));
    }

    [Fact]
    public void SettingsAreNeverOpenedInATruncatingMode()
    {
        // A direct guard on the mechanism rather than its consequences, because the consequence is only
        // reachable by arranging a failure and the mechanism is what has to be absent.
        var source = ReadSource("src", "Storage.cs");

        Assert.DoesNotContain("FileMode.Create)", source.Replace(" ", string.Empty).Replace("File.Open(tempFile,FileMode.Create", "SAFE_TEMP_FILE_OPEN"));
        Assert.Contains("File.Replace", source, StringComparison.Ordinal);
    }

    static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { FindRepositoryRoot() }.Concat(parts).ToArray()));

    static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kronos.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}