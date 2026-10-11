using Xunit;

// Everything that touches Storage's statics belongs here.
//
// Storage.StoragePath is a lazily computed static. OverrideStoragePath resets it, and xunit runs test
// classes in parallel by default, so a class that redirects storage while others are reading it moves the
// ground under them. It happened once already: SettingsPersistenceTests called OverrideStoragePath(null)
// on teardown while DllMigration117Tests was mid-run, and that test failed on a file that had been written
// to a directory that was no longer the one it expected. It passed on the next run, which is the worst
// possible failure mode for a test.
//
// Serialising these classes against each other is the fix. Disabling parallelisation wholesale was
// rejected: the suite is 475 tests and most of them are pure, so they do not need to wait.
[CollectionDefinition(StorageCollection.Name, DisableParallelization = true)]
public class StorageCollection
{
    public const string Name = "Storage-static-state";
}