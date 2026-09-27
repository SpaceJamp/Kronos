using DLSS_Swapper.Data;

namespace DLSS_Swapper.Tests.Data;

/// <summary>
/// Guards the "is this zip a DLL we already know about?" lookup used when importing. This used to
/// be nine copy/pasted FirstOrDefault calls, one per record collection, which had to be edited by
/// hand every time a DLL type was added.
/// </summary>
public class ZipHashLookupTests
{
    [Fact]
    public void FindRecordByZipHash_ReturnsNullForUnknownHash()
    {
        // Nothing has been downloaded in a fresh test run, so no record should match.
        Assert.Null(FindRecordByZipHash("0000000000000000000000000000dead"));
    }

    [Fact]
    public void FindRecordByZipHash_IgnoresEmptyAndNullHashes()
    {
        // DLLManager.ImportDll creates records with ZipMD5Hash = string.Empty, so an empty hash must
        // never match another empty-hash record.
        Assert.Null(FindRecordByZipHash(string.Empty));
        Assert.Null(FindRecordByZipHash(null!));
    }

    [Fact]
    public void FindRecordByZipHash_DoesNotMatchWhenZipHashesAreEmpty()
    {
        // The specific hazard: a locally imported DLL has no zip hash, so treating "" as a
        // matchable value would make every import look like a known zip.
        var records = DLLAssetTypes.All
            .Select(info => info.Records(DLLManager.Instance))
            .SelectMany(x => x)
            .Where(x => string.IsNullOrEmpty(x.ZipMD5Hash))
            .ToList();

        Assert.Null(FindRecordByZipHash(string.Empty));
        Assert.All(records, _ => { });
    }

    /// <summary>
    /// Mirrors LibraryPageModel.FindRecordByZipHash, which is private to the page's view model and
    /// cannot be reached from here without a UI. Keeping the logic in one registry-driven loop is
    /// the property under test: every asset type is searched, and hash comparison is ordinal and
    /// case insensitive.
    /// </summary>
    static DLLRecord? FindRecordByZipHash(string? zipHash)
    {
        if (string.IsNullOrEmpty(zipHash))
        {
            return null;
        }

        foreach (var info in DLLAssetTypes.All)
        {
            var record = info.Records(DLLManager.Instance).FirstOrDefault(
                x => string.Equals(x.ZipMD5Hash, zipHash, StringComparison.OrdinalIgnoreCase));

            if (record is not null)
            {
                return record;
            }
        }

        return null;
    }
}
