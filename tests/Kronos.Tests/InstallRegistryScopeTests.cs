using Kronos;

namespace Kronos.Tests;

/// <summary>
/// Tests for how the app locates its own uninstall entry, and for when the measured install size is
/// worth writing back to it.
/// </summary>
/// <remarks>
/// This is not hypothetical. A real install was made and inspected directly, and the uninstall key
/// came out as
///   HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\{64E9E8E5-...}_is1
/// with DisplayName "Kronos". The previous code looked for a hard coded ...\Uninstall\Kronos in
/// HKCU alone, so it would have found nothing and the install size would never have been updated
/// again. The tests below pin the matching rules that replace that.
/// </remarks>
public class InstallRegistryScopeTests
{
    // ---- Identifying our own entry by DisplayName ----

    [Fact]
    public void TheBareProductNameIsRecognised()
    {
        // What the NSIS installer registered, and still a valid shape.
        Assert.True(App.IsThisProduct("Kronos"));
    }

    [Fact]
    public void TheNameWithATrailingVersionIsRecognised()
    {
        // What Inno Setup actually wrote on a real install. Without this the lookup finds nothing.
        Assert.True(App.IsThisProduct("Kronos version 1.45"));
    }

    [Theory]
    [InlineData("kronos")]
    [InlineData("KRONOS")]
    [InlineData("  Kronos  ")]
    [InlineData("Kronos version 1.45.0")]
    public void MatchingIgnoresCaseAndSurroundingSpace(string displayName)
    {
        Assert.True(App.IsThisProduct(displayName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AMissingDisplayNameIsNotOurs(string? displayName)
    {
        Assert.False(App.IsThisProduct(displayName));
    }

    [Theory]
    [InlineData("KronosBackup")]
    [InlineData("Kronos Manager")]
    [InlineData("KronosManager")]
    [InlineData("Uninstall Kronos")]
    [InlineData("DLSS Swapper")]
    [InlineData("Discord")]
    public void AnotherProductsNameIsNotOurs(string displayName)
    {
        // "Kronos Manager" and "KronosBackup" are the interesting ones. A naive "starts with the
        // name" match accepts both, and would then overwrite another application's EstimatedSize.
        // Only the exact name, or that name followed by "version", is accepted.
        Assert.False(App.IsThisProduct(displayName));
    }

    [Fact]
    public void OnlyTwoWordVersionSuffixesAreAccepted()
    {
        // Guards the narrowness of the rule directly, so loosening it later has to be deliberate.
        // A plain prefix match would make every one of these true.
        Assert.True(App.IsThisProduct("Kronos version 1.45"));
        Assert.True(App.IsThisProduct("Kronos version 1.45.0.0"));

        Assert.False(App.IsThisProduct("Kronos Manager"));
        Assert.False(App.IsThisProduct("KronosBackup"));
        Assert.False(App.IsThisProduct("Kronos ver 1.45"));
    }

    // ---- Which hive and view is searched ----

    [Fact]
    public void ThePerUserHiveIsSearchedBeforeTheMachineHive()
    {
        // A per user install is the common case, and HKCU is the only hive an unelevated process can
        // write EstimatedSize back to, so it has to win when both somehow contain an entry.
        var order = App.SearchedHives
            .Select(h => h.Hive)
            .ToList();

        var firstLocalMachine = order.FindIndex(h => h == Microsoft.Win32.RegistryHive.LocalMachine);
        var lastCurrentUser = order.FindLastIndex(h => h == Microsoft.Win32.RegistryHive.CurrentUser);

        Assert.True(lastCurrentUser < firstLocalMachine,
            "Every HKCU view must be searched before any HKLM view, otherwise a stale machine wide " +
            "entry is preferred over the per user one that can actually be written to.");
    }

    [Fact]
    public void BothRegistryViewsOfBothHivesAreSearched()
    {
        // A 32-bit installer writes to WOW6432Node. The previous NSIS script was a 32-bit program,
        // so an install made by it is in the 32-bit view while this 64-bit app reads the 64-bit one.
        var views = App.SearchedHives.Select(h => h.View).ToHashSet();

        foreach (var hive in new[] { Microsoft.Win32.RegistryHive.CurrentUser, Microsoft.Win32.RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { Microsoft.Win32.RegistryView.Registry64, Microsoft.Win32.RegistryView.Registry32 })
            {
                Assert.Contains((hive, view), App.SearchedHives);
            }
        }

        Assert.Contains(Microsoft.Win32.RegistryView.Registry64, views);
        Assert.Contains(Microsoft.Win32.RegistryView.Registry32, views);
    }

    [Fact]
    public void TheInstallSizeThreadIsABackgroundThread()
    {
        // THE REGRESSION. Thread.IsBackground defaults to false, and the work is a full walk of the
        // install directory, 642 files and roughly 300 MB on a real install. As a foreground thread it
        // kept the process alive, so closing Kronos waited for the walk to finish and appeared to
        // hang. Nothing else in the codebase catches this, which is why the thread is built by a
        // method that can be reached from a test.
        var thread = App.CreateInstallSizeThread(() => { });

        Assert.True(thread.IsBackground,
            "The install size thread must be a background thread, or it holds the process open at shutdown.");
    }

    [Fact]
    public void TheInstallSizeThreadIsNamedForDiagnostics()
    {
        // Cosmetic, but a thread with no name shows up as an unnamed entry in a debugger, which makes
        // it hard to tell what is holding the process open.
        Assert.False(string.IsNullOrWhiteSpace(App.CreateInstallSizeThread(() => { }).Name));
    }

    [Fact]
    public void TheInstallSizeThreadRunsTheWorkItWasGiven()
    {
        // Confirms the ThreadStart is wired through rather than dropped, which a test that only
        // checked IsBackground would not catch.
        using var started = new ManualResetEventSlim(false);
        var thread = App.CreateInstallSizeThread(() => started.Set());

        thread.Start();

        Assert.True(started.Wait(TimeSpan.FromSeconds(10)), "The install size thread did not run its work.");
    }

    [Fact]
    public void TheUninstallRootIsTheOneAppsAndFeaturesReads()
    {
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", App.UninstallKeyRoot);
    }

    [Fact]
    public void AnUnregisteredInstallIsFoundOnThisMachineOrNotAtAll()
    {
        // Not asserting that Kronos is installed, since the test machine may not have it. Asserting
        // the negative would be equally wrong: it would fail the moment someone does install it.
        // What matters is that the call is safe and does not throw when nothing is registered.
        var entry = App.FindInstallUninstallEntry();

        if (entry is null)
        {
            return;
        }

        // If it did find something, it must actually be our entry and the key must open.
        using var key = App.OpenUninstallEntry(entry.Value, false);
        Assert.NotNull(key);
        Assert.True(App.IsThisProduct(key!.GetValue("DisplayName") as string));
    }

    // ---- Hive preference, kept as a rule of its own ----

    [Fact]
    public void APowerUserInstallIsFoundInThePerUserHive()
    {
        Assert.Equal(
            Microsoft.Win32.RegistryHive.CurrentUser,
            App.SelectInstallRegistryHive(perUserExists: true, allUsersExists: false));
    }

    [Fact]
    public void AMachineWideInstallIsFoundInTheMachineHive()
    {
        Assert.Equal(
            Microsoft.Win32.RegistryHive.LocalMachine,
            App.SelectInstallRegistryHive(perUserExists: false, allUsersExists: true));
    }

    [Fact]
    public void NeitherHiveMeansThereIsNothingToUpdate()
    {
        // The portable build is not registered at all, and a user may have deleted the entry.
        Assert.Null(App.SelectInstallRegistryHive(perUserExists: false, allUsersExists: false));
    }

    [Fact]
    public void ThePerUserHiveWinsIfBothExist()
    {
        // Possible after a user switches from a machine wide install to a per user one and the old
        // entry survives. HKCU is the entry describing the install actually running.
        Assert.Equal(
            Microsoft.Win32.RegistryHive.CurrentUser,
            App.SelectInstallRegistryHive(perUserExists: true, allUsersExists: true));
    }

    // ---- When to write the size back ----

    [Fact]
    public void AChangedSizeIsWrittenWhenTheHiveIsWritable()
    {
        Assert.True(App.ShouldWriteInstallSize(existingSizeKB: 100, newSizeKB: 250, hiveIsWritable: true));
    }

    [Fact]
    public void AMissingSizeIsWrittenWhenTheHiveIsWritable()
    {
        // A fresh install, before anything has recorded a size.
        Assert.True(App.ShouldWriteInstallSize(existingSizeKB: null, newSizeKB: 250, hiveIsWritable: true));
    }

    [Fact]
    public void AnUnchangedSizeIsNotWrittenAgain()
    {
        // Writing on every launch made the value in Apps & features churn on every start and
        // produced a registry write for a number that almost never moved.
        Assert.False(App.ShouldWriteInstallSize(existingSizeKB: 250, newSizeKB: 250, hiveIsWritable: true));
    }

    [Fact]
    public void NothingIsWrittenToAHiveTheProcessCannotWrite()
    {
        // HKLM is writable only by an elevated process, and this app deliberately runs unelevated.
        Assert.False(App.ShouldWriteInstallSize(existingSizeKB: 100, newSizeKB: 250, hiveIsWritable: false));
    }

    [Fact]
    public void AnUnchangedSizeInAnUnwritableHiveIsAlsoNotWritten()
    {
        // The two reasons to skip are independent, and both together still mean skip.
        Assert.False(App.ShouldWriteInstallSize(existingSizeKB: 250, newSizeKB: 250, hiveIsWritable: false));
    }
}
