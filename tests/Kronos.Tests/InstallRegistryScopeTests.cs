using Kronos;

namespace Kronos.Tests;

/// <summary>
/// Tests for the two decisions behind the "EstimatedSize" value shown in Apps &amp; features: which
/// registry hive the install is registered in, and whether the freshly measured size is worth
/// writing back.
/// </summary>
/// <remarks>
/// Both were extracted out of <c>CalculateInstallSize</c> so they can be pinned here. The hive
/// choice became a real bug when the installer gained a machine wide option: a per user install is
/// registered under HKCU and a machine wide one under HKLM, and the code looked only in HKCU, so
/// every all users install silently stopped tracking its size.
/// </remarks>
public class InstallRegistryScopeTests
{
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
        // THE REGRESSION. Reading only HKCU found nothing here and the install size was never
        // updated again for anyone who installed for all users.
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
        // Possible after a user upgrades from a machine wide install to a per user one, since the
        // machine wide entry is left behind by the old installer style. HKCU is the entry describing
        // the install actually running, and the only one an unelevated process can write to.
        Assert.Equal(
            Microsoft.Win32.RegistryHive.CurrentUser,
            App.SelectInstallRegistryHive(perUserExists: true, allUsersExists: true));
    }

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
        // A machine wide install keeps whatever the installer recorded rather than throwing on
        // every launch.
        Assert.False(App.ShouldWriteInstallSize(existingSizeKB: 100, newSizeKB: 250, hiveIsWritable: false));
    }

    [Fact]
    public void AnUnchangedSizeInAnUnwritableHiveIsAlsoNotWritten()
    {
        // The two reasons to skip are independent, and both together still mean skip.
        Assert.False(App.ShouldWriteInstallSize(existingSizeKB: 250, newSizeKB: 250, hiveIsWritable: false));
    }
}
