using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Kronos.Helpers;

namespace Kronos.Data;

public partial class LocalRecord : ObservableObject, IEquatable<LocalRecord>
{
    public string ExpectedPath { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsDownloaded { get; set; } = false;

    [ObservableProperty]
    public partial bool IsImported { get; set; } = false;

    /// <summary>
    /// The downloader tracking this record's fetch, so the UI can show progress.
    /// </summary>
    /// <remarks>
    /// Cross-platform because the download itself has to be: the Linux CLI fetches DLLs too, and
    /// it reads this to drive the transfer. Nothing binds to it there, so the progress properties
    /// simply go unread - which is a different thing from the object not existing.
    /// </remarks>
    [ObservableProperty]
    public partial FileDownloader? FileDownloader { get; set; } = null;

    [ObservableProperty]
    public partial bool HasDownloadError { get; set; } = false;

    [ObservableProperty]
    public partial string DownloadErrorMessage { get; set; } = string.Empty;


    private LocalRecord()
    {

    }

    public static LocalRecord FromExpectedPath(string expectedPath, bool isImported = false)
    {
        var localRecord = new LocalRecord()
        {
            ExpectedPath = expectedPath,
        };

        if (File.Exists(expectedPath))
        {
            localRecord.IsDownloaded = true;
            localRecord.IsImported = isImported;
        }

        return localRecord;
    }


    internal bool Delete()
    {
        try
        {
            if (File.Exists(ExpectedPath))
            {
                File.Delete(ExpectedPath);
            }

            var path = Path.GetDirectoryName(ExpectedPath) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path) == false)
            {
                if (Directory.GetFiles(path).Length == 0 && Directory.GetDirectories(path).Length == 0)
                {
                    Directory.Delete(path);
                }
            }



            IsDownloaded = false;
            HasDownloadError = false;
            DownloadErrorMessage = string.Empty;
            // We don't update IsImported here as that wont change.
            return true;
        }
        catch (Exception err)
        {
            Logger.Error(err);
            return false;
        }
    }

    /*
    internal async Task<bool> DeleteAsync()
    {
        var storageFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
        try
        {
            var dlssFile = await storageFolder.GetFileAsync(ExpectedPath);
            await dlssFile.DeleteAsync(Windows.Storage.StorageDeleteOption.PermanentDelete);
            
            IsDownloaded = false;
            IsDownloading = false;
            DownloadProgress = 0;
            HasDownloadError = false;
            DownloadErrorMessage = string.Empty;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
    */

    internal void UpdateFromNewLocalRecord(LocalRecord localRecord)
    {
        // First make sure expected path matches on both.
        if (Equals(localRecord) == false)
        {
            return;
        }

        ExpectedPath = localRecord.ExpectedPath;
        IsDownloaded = localRecord.IsDownloaded;
        FileDownloader = localRecord.FileDownloader;
        HasDownloadError = localRecord.HasDownloadError;
        DownloadErrorMessage = localRecord.DownloadErrorMessage;
        IsImported = localRecord.IsImported;
    }

    public bool Equals(LocalRecord? other)
    {
        if (other is null)
        {
            return false;
        }

        // Make sure other and other.ExpectedPath are not null or empty. This also means
        // that this.ExpectedPath can't be null or empty and have this return true.
        if (string.IsNullOrWhiteSpace(other.ExpectedPath))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(ExpectedPath))
        {
            return false;
        }

        // Ordinal, not InvariantCulture: this is a file path, so only case should be ignored, and
        // culture-sensitive comparison is both slower and wrong for paths containing non-ASCII
        // characters.
        return string.Equals(ExpectedPath, other.ExpectedPath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Overridden so it agrees with <see cref="Equals(LocalRecord?)"/>, using the same Ordinal
    /// case-insensitive comparison. Without it, two records that compare equal could land in
    /// different buckets in a HashSet.
    /// </summary>
    public override int GetHashCode()
    {
        var path = ExpectedPath ?? string.Empty;
        return StringComparer.OrdinalIgnoreCase.GetHashCode(path);
    }
}
