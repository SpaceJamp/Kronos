using CommunityToolkit.Mvvm.ComponentModel;
using Chronos.Extensions;
using Chronos.Helpers;
using Chronos.Interfaces;
using Chronos.UserControls;
using Microsoft.UI.Xaml.Controls;
using NvAPIWrapper.DRS;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SQLite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Chronos.Data;

public abstract partial class Game : ObservableObject, IComparable<Game>, IEquatable<Game> //, INotifyPropertyChanged
{
    [PrimaryKey]
    [Column("id")]
    public string ID { get; set; } = string.Empty;

    [Column("platform_id")]
    public string PlatformId { get; set; } = string.Empty;

    [ObservableProperty]
    [Column("title")]
    public partial string Title { get; set; } = string.Empty;

    // Used to cache the title as a base64 string. The cache is keyed on the title it was
    // generated from so it invalidates itself when Title changes (e.g. via ParentUpdateFromGame).
    // Previously the cache was never invalidated, so known-DLL lookups compared a stale title
    // and renamed games were wrongly reported as having unknown DLLs.
    string? _titleBase64;
    string? _titleBase64Source;
    [Ignore]
    public string TitleBase64
    {
        get
        {
            if (_titleBase64 is null || _titleBase64Source != Title)
            {
                _titleBase64Source = Title;
                _titleBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(Title));
            }

            return _titleBase64;
        }
    }

    [Column("install_path")]
    public string InstallPath { get; set; } = string.Empty;

    [ObservableProperty]
    [Column("cover_image")]
    public partial string? CoverImage { get; set; } = null;

    [ObservableProperty]
    [Ignore]
    public partial uint? DlssPreset { get; set; }

    [ObservableProperty]
    [Ignore]
    public partial uint? DlssDPreset { get; set; }


    [ObservableProperty]
    [Ignore]
    public partial uint? DlssGPreset { get; set; }

    [Ignore]
    public DriverSettingsProfile? DriverSettingsProfile { get; set; }

    /*
    [ObservableProperty]
    [property: Column("base_dlss_version")]
    string baseDLSSVersion = string.Empty;

    [ObservableProperty]
    [property: Column("current_dlss_version")]
    string currentDLSSVersion = string.Empty;

    [ObservableProperty]
    [property: Column("current_dlss_hash")]
    string currentDLSSHash = string.Empty;

    [ObservableProperty]
    [property: Column("base_dlss_hash")]
    string baseDLSSHash = string.Empty;

    [ObservableProperty]
    [property: Column("has_dlss")]
    bool hasDLSS = false;
    */

    [ObservableProperty]
    [Column("has_swappable_items")]
    public partial bool HasSwappableItems { get; set; } = false;

    [ObservableProperty]
    [Column("notes")]
    public partial string Notes { get; set; } = string.Empty;

    [ObservableProperty]
    [Column("is_favourite")]
    public partial bool IsFavourite { get; set; } = false;

    /// <summary>
    /// If the game is hidden from the main list or not. All hidden games are still processed.
    /// If the value is null the user has not set the value and this should be considered as not hidden.
    /// </summary>
    [ObservableProperty]
    [Column("is_hidden")]
    public partial bool? IsHidden { get; set; } = null;

    /// <summary>
    /// True when the user has flagged this install as a repack / "scene release" rather than a
    /// normal store install.
    /// </summary>
    /// <remarks>
    /// In practice only games in the Manually Added library can be repacks, because store
    /// libraries report their own installed titles. The column lives on the base class anyway so
    /// that the badge can be bound uniformly for every game, and so the value has somewhere to
    /// live for all of the per-library tables.
    ///
    /// The stored value is the source of truth. RepackDetector only ever *suggests* this flag when
    /// a game is added, because repack tooling varies too much for detection to be reliable.
    /// </remarks>
    [ObservableProperty]
    [Column("is_repack")]
    public partial bool IsRepack { get; set; } = false;

    [ObservableProperty]
    [Ignore]
    public partial bool Processing { get; set; } = false;

    [Ignore]
    public abstract GameLibrary GameLibrary { get; }

    [Ignore]
    //public string ExpectedCoverImage => Path.Combine(Storage.GetImageCachePath(), $"{ID}_600_900.jpg");
    //public string ExpectedCoverImage => Path.Combine(Storage.GetImageCachePath(), $"{ID}_600_900.png");
    public string ExpectedCoverImage => Path.Combine(Storage.GetImageCachePath(), $"{ID}_400_600.png");
    //public string ExpectedCoverImage => Path.Combine(Storage.GetImageCachePath(), $"{ID}_600_900.webp");

    [Ignore]
    //public string ExpectedCustomCoverImage => Path.Combine(Storage.GetImageCachePath(), $"{ID}_custom_600_900.jpg");
    //public string ExpectedCustomCoverImage => Path.Combine(Storage.GetImageCachePath(), $"{ID}_custom_600_900.png");
    public string ExpectedCustomCoverImage => Path.Combine(Storage.GetImageCachePath(), $"{ID}_custom_400_600.png");
    //public string ExpectedCustomCoverImage => Path.Combine(Storage.GetImageCachePath(), $"{ID}_custom_600_900.webp");

    [Ignore]
    public List<GameAsset> GameAssets { get; } = new List<GameAsset>();

    [Ignore]
    public bool NeedsProcessing { get; set; } = false;

    bool _isLoadingCoverImage;

    // NOTE: DLL type
    [ObservableProperty]
    [Ignore]
    public partial GameAsset? CurrentDLSS { get; set; } = null;

    [ObservableProperty]
    [Ignore]
    public partial bool MultipleDLSSFound { get; set; } = false;

    [ObservableProperty]
    [Ignore]
    public partial GameAsset? CurrentDLSS_G { get; set; } = null;

    [ObservableProperty]
    [Ignore]
    public partial bool MultipleDLSSGFound { get; set; } = false;

    [ObservableProperty]
    [Ignore]
    public partial GameAsset? CurrentDLSS_D { get; set; } = null;

    [ObservableProperty]
    [Ignore]
    public partial bool MultipleDLSSDFound { get; set; } = false;

    [ObservableProperty]
    [Ignore]
    public partial GameAsset? CurrentFSR_31_DX12 { get; set; } = null;

    [ObservableProperty]
    [Ignore]
    public partial bool MultipleFSR31DX12Found { get; set; } = false;

    [ObservableProperty]
    [Ignore]
    public partial GameAsset? CurrentFSR_31_VK { get; set; } = null;

    [ObservableProperty]
    [Ignore]
    public partial bool MultipleFSR31VKFound { get; set; } = false;

    [ObservableProperty]
    [Ignore]
    public partial GameAsset? CurrentXeSS { get; set; } = null;

    [ObservableProperty]
    [Ignore]
    public partial bool MultipleXeSSFound { get; set; } = false;

    [ObservableProperty]
    [Ignore]
    public partial GameAsset? CurrentXeLL { get; set; } = null;

    [ObservableProperty]
    [Ignore]
    public partial bool MultipleXeLLFound { get; set; } = false;

    [ObservableProperty]
    [Ignore]
    public partial GameAsset? CurrentXeSS_FG { get; set; } = null;

    [ObservableProperty]
    [Ignore]
    public partial bool MultipleXeSSFGFound { get; set; } = false;

    [ObservableProperty]
    [Ignore]
    public partial GameAsset? CurrentXeSS_DX11 { get; set; } = null;

    [ObservableProperty]
    [Ignore]
    public partial bool MultipleXeSSDX11Found { get; set; } = false;
    

    [Ignore]
    public abstract bool IsReadyToPlay { get; }

    protected void SetID()
    {
        // Seeing as we use ID, it sure would be a shame if a PlatformId was set to "C:\Program Files\"
        // So try to remove all funky characters before

        var platformId = PlatformId;
        foreach (var invalidPathChar in PathHelpers.InvalidFileNamePathChars)
        {
            if (platformId.Contains(invalidPathChar))
            {
                platformId = platformId.Replace(invalidPathChar, '_');
            }
        }

        ID = GameLibrary switch
        {
            GameLibrary.Steam => $"steam_{platformId}",
            GameLibrary.GOG => $"gog_{platformId}",
            GameLibrary.EpicGamesStore => $"epicgamesstore_{platformId}",
            GameLibrary.UbisoftConnect => $"ubisoftconnect_{platformId}",
            GameLibrary.XboxApp => $"xboxapp_{platformId}",
            GameLibrary.ManuallyAdded => $"manuallyadded_{platformId}",
            GameLibrary.BattleNet => $"battlenet_{platformId}",
            GameLibrary.EAApp => $"eaapp_{platformId}",
            _ => throw new Exception($"Unknown GameLibrary {GameLibrary} while setting ID"),
        };
    }

    // Bounds how many games can be scanned for DLLs/covers at once. Every game with no previously known
    // DLLs is re-queued for processing on every launch, so without a limit a large library fires off
    // hundreds of concurrent recursive directory scans and UI-thread updates at startup.
    static readonly SemaphoreSlim processGameSemaphore = new SemaphoreSlim(4);

    /// <summary>
    /// Detects DLSS and updates cover image.
    /// </summary>
    public void ProcessGame(bool autoSave = true, bool forceNeedsProcessing = false)
    {
        // If we are alreayd procssing we don't need to process again
        if (Processing == true)
        {
            return;
        }

        App.CurrentApp.RunOnUIThread(() =>
        {
            NeedsProcessing = false;
        });

        if (string.IsNullOrEmpty(InstallPath))
        {
            return;
        }

        if (Directory.Exists(InstallPath) == false)
        {
            return;
        }

        App.CurrentApp.RunOnUIThread(() =>
        {
            Processing = true;
            HasSwappableItems = false;
        });

        // NOTE: this used to be ThreadPool.QueueUserWorkItem(async lambda), which binds the lambda to
        // WaitCallback and therefore runs it as `async void`. Anything thrown from the finally block
        // below escaped to the thread pool as an unhandled exception and terminated the process.
        // Task.Run gives us a real Task, so the finally is covered and nothing can crash the app.
        _ = Task.Run(async () =>
        {
            await processGameSemaphore.WaitAsync().ConfigureAwait(false);

            var newHasSwappableItems = false;

            try
            {
                var shouldUpdatedCover = true;

                if (forceNeedsProcessing == true && File.Exists(ExpectedCustomCoverImage) == false)
                {
                    // If we are forcing game load and custom cover image doesnt exist we will force load the cover no matter what.
                }
                else
                {
                    // This shouldn't crash, bit if it does lets not take down the entire processing.
                    try
                    {
                        FileInfo? fileInfo = null;
                        if (File.Exists(ExpectedCustomCoverImage))
                        {
                            // If we are using a custom cover we don't want to try reloading any cover so we don't set fileInfo.
                            shouldUpdatedCover = false;
                        }
                        else if (File.Exists(ExpectedCoverImage))
                        {
                            fileInfo = new FileInfo(ExpectedCoverImage);
                        }

                        if (fileInfo is not null)
                        {
                            var daysSinceLastModified = (DateTime.Now - fileInfo.LastWriteTime).TotalDays;

                            // Add +/- 2 days so not all will process at the same time.
                            daysSinceLastModified += ((new Random()).NextDouble() - 0.5) * 4.0;

                            // If its less than 7 days lets not try refresh.
                            if (daysSinceLastModified < 7)
                            {
                                shouldUpdatedCover = false;
                            }
                        }
                    }
                    catch (Exception err)
                    {
                        Logger.Error(err);
                        Debugger.Break();
                    }
                }

                Task? coverImageTask = null;
                if (shouldUpdatedCover)
                {
                    coverImageTask = UpdateCacheImageAsync();
                }
                else
                {
                    Logger.Verbose($"Skipping updating cover for {Title}");
                }

                var enumerationOptions = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    // The default is FileAttributes.Hidden. Assigning (instead of |=) keeps
                    // ReparsePoint skipping while still skipping hidden files.
                    AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden,
                    // Skip directories we cannot read instead of throwing. Without this a single
                    // ACL protected folder inside a game install aborts the whole scan.
                    IgnoreInaccessible = true,
                };

                var oldGameAssets = GameAssets.ToList();

                // The new records are built up separately and only swapped into GameAssets once the
                // scan has completed. Previously GameAssets was cleared and the game_asset rows were
                // deleted *before* Directory.GetFiles ran, so any failure (an inaccessible folder, an
                // uninstall mid-scan, a path that is too long) silently wiped everything we knew
                // about the game's DLLs and left the game showing no swappable items forever.
                var newGameAssets = new List<GameAsset>();

                // TODO: See if changing these to filter specific files, or getting very *.dll and looking for our specific ones is faster
                // EnumerateFiles (not GetFiles) so the walk streams instead of building a string[] of
                // every DLL in the install tree up front.
                var dllPaths = Directory.EnumerateFiles(InstallPath, "*.dll", enumerationOptions);

                /*
                var dlssDllPaths = Directory.GetFiles(InstallPath, "nvngx_dlss.dll", enumerationOptions);
                var dlssgDllPaths = Directory.GetFiles(InstallPath, "nvngx_dlssg.dll", enumerationOptions);
                var dlssdDllPaths = Directory.GetFiles(InstallPath, "nvngx_dlssd.dll", enumerationOptions);
                var xessDllPaths = Directory.GetFiles(InstallPath, "libxess.dll", enumerationOptions);
                */

                var dllHistory = new List<GameHistory>();
                var unknownGameAssets = new List<GameAsset>();

                void ProcessGame_ProcessGameAsset(GameAsset gameAsset)
                {
                    gameAsset.LoadVersionAndHash();

                    var oldGameAsset = oldGameAssets.FirstOrDefault(x => x.Path.Equals(gameAsset.Path, StringComparison.OrdinalIgnoreCase));

                    if (oldGameAsset is not null) // DLL existed previously
                    {
                        if (gameAsset.Version == oldGameAsset.Version)
                        {
                            // NOOP
                        }
                        else
                        {
                            dllHistory.Add(new GameHistory()
                            {
                                GameId = ID,
                                EventType = GameHistoryEventType.DLLChangedExternally,
                                EventTime = DateTime.Now,
                                AssetType = gameAsset.AssetType,
                                AssetPath = gameAsset.Path,
                                AssetVersion = gameAsset.DisplayName,
                            });

                            // If the DLL was changed externally (eg. game update) we delete the backup.
                            // This fixes the issue where looking at your game it may appear to be downgraded but
                            // in reality it is because the game updated to a newer version than you had swapped to.
                            var expectedBackupPath = $"{gameAsset.Path}.dlsss";
                            if (File.Exists(expectedBackupPath))
                            {
                                var tempBackupGameAsset = new GameAsset()
                                {
                                    Id = ID,
                                    AssetType = DLLManager.Instance.GetAssetBackupType(gameAsset.AssetType),
                                    Path = expectedBackupPath,
                                };
                                tempBackupGameAsset.LoadVersionAndHash();

                                dllHistory.Add(new GameHistory()
                                {
                                    GameId = ID,
                                    EventType = GameHistoryEventType.DLLBackupRemoved,
                                    EventTime = DateTime.Now,
                                    AssetType = tempBackupGameAsset.AssetType,
                                    AssetPath = tempBackupGameAsset.Path,
                                    AssetVersion = tempBackupGameAsset.DisplayName,
                                });

                                File.Delete(expectedBackupPath);
                            }
                        }
                    }
                    else // DLL is new
                    {
                        dllHistory.Add(new GameHistory()
                        {
                            GameId = ID,
                            EventType = GameHistoryEventType.DLLDetected,
                            EventTime = DateTime.Now,
                            AssetType = gameAsset.AssetType,
                            AssetPath = gameAsset.Path,
                            AssetVersion = gameAsset.DisplayName,
                        });
                    }

                    if (DLLManager.Instance.IsInKnownGameAsset(gameAsset, this) == false)
                    {
                        unknownGameAssets.Add(gameAsset);
                    }

                    LoadBackupForGameAsset(gameAsset, newGameAssets);

                }

                // NOTE: DLL type
                // Only the nine DLL names in the registry are of interest. Match against a HashSet of
                // those names while streaming the directory, rather than materialising every .dll in the
                // install tree (tens of thousands of paths on a modern AAA title) and running a chain of
                // string comparisons over each one.
                var trackedDllNames = new HashSet<string>(DLLAssetTypes.All.Select(x => x.DllName), StringComparer.OrdinalIgnoreCase);

                foreach (var dllPath in dllPaths)
                {
                    var dllName = Path.GetFileName(dllPath);
                    if (trackedDllNames.Contains(dllName) == false)
                    {
                        continue;
                    }

                    // NOTE: DLL type
                    var info = DLLAssetTypes.FindByDllName(dllName);
                    if (info is null)
                    {
                        continue;
                    }

                    var gameAsset = new GameAsset()
                    {
                        Id = ID,
                        AssetType = info.AssetType,
                        Path = dllPath,
                    };

                    ProcessGame_ProcessGameAsset(gameAsset);
                    newGameAssets.Add(gameAsset);
                }

                // The scan completed successfully, so it is now safe to replace the known records.
                GameAssets.Clear();
                GameAssets.AddRange(newGameAssets);

                App.CurrentApp.RunOnUIThread(() =>
                {
                    UpdateCurrentDLLsFromGameAssets();
                });

                // Delete the old rows before re-inserting, now that we know we have replacements.
                using (await Database.Instance.Mutex.LockAsync())
                {
                    await Database.Instance.Connection.ExecuteAsync("DELETE FROM game_asset WHERE id = ?", ID).ConfigureAwait(false);
                }

                if (GameAssets.Any())
                {
                    newHasSwappableItems = true;

                    //App.CurrentApp.Database.ExecuteAsync
                    //savePoint is not valid, and should be the result of a call to SaveTransactionPoint.
                    using (await Database.Instance.Mutex.LockAsync())
                    {
                        await Database.Instance.Connection.InsertAllAsync(dllHistory, false).ConfigureAwait(false);
                        await Database.Instance.Connection.InsertAllAsync(GameAssets, false).ConfigureAwait(false);
                    }

                    if (unknownGameAssets.Any())
                    {
                        GameManager.Instance.AddUnknownGameAssets(GameLibrary, Title, unknownGameAssets);
                    }
                }

                if (coverImageTask is not null)
                {
                    await coverImageTask;
                }
            }
            catch (Exception err)
            {
                // No Debugger.Break() here. An unreadable subdirectory or a game that was
                // uninstalled mid-scan is an expected condition, not a developer error.
                Logger.Error(err);
            }
            finally
            {
                processGameSemaphore.Release();

                // Now update all the data on the UI thread.
                try
                {
                    await App.CurrentApp.RunOnUIThreadAsync(async () =>
                    {
                        HasSwappableItems = newHasSwappableItems;

                        if (autoSave)
                        {
                            await SaveToDatabaseAsync();
                        }
                    });
                }
                catch (Exception err)
                {
                    Logger.Error(err, "Failed to update the game on the UI thread after processing.");
                }
                finally
                {
                    // Processing has to be reset unconditionally. If it is left true the game
                    // refuses to open (see GameGridPage.GridAndListView_ItemClick) forever.
                    App.CurrentApp.RunOnUIThread(() =>
                    {
                        Processing = false;
                    });
                }
            }
        });
    }

    void LoadBackupForGameAsset(GameAsset gameAsset, List<GameAsset> targetCollection)
    {
        var backupPath = $"{gameAsset.Path}.dlsss";
        if (File.Exists(backupPath))
        {
            var gameAssetBackup = new GameAsset()
            {
                Id = ID,
                AssetType = DLLManager.Instance.GetAssetBackupType(gameAsset.AssetType),
                Path = backupPath,
            };
            gameAssetBackup.LoadVersionAndHash();
            targetCollection.Add(gameAssetBackup);
        }
    }


    public async Task LoadCoverImageAsync()
    {
        if (_isLoadingCoverImage == true)
        {
            return;
        }

        _isLoadingCoverImage = true;

        // TODO: Update if the image last write is > 1 week old or something

        if (File.Exists(ExpectedCustomCoverImage))
        {
            // If a custom cover exists use it.
            App.CurrentApp.RunOnUIThread(() =>
            {
                CoverImage = ExpectedCustomCoverImage;
            });
        }
        else if (File.Exists(ExpectedCoverImage))
        {
            // If a standard cover exists use it.
            App.CurrentApp.RunOnUIThread(() =>
            {
                CoverImage = ExpectedCoverImage;
            });
        }
        else
        {
            // If no cover exists use the abstracted method to get the game as expect for this library.
            await UpdateCacheImageAsync();
        }

        _isLoadingCoverImage = false;
    }

    protected abstract Task UpdateCacheImageAsync();

    internal async Task<(bool Success, string Message, bool PromptToRelaunchAsAdmin)> ResetDllAsync(GameAssetType gameAssetType)
    {
        var backupRecordType = DLLManager.Instance.GetAssetBackupType(gameAssetType);
        var existingBackupRecords = this.GameAssets.Where(x => x.AssetType == backupRecordType).ToList();

        if (existingBackupRecords.Count == 0)
        {
            Logger.Info("No backup records found.");
            return (false, "Unable to reset to default. Please repair your game manually.", false);
        }
        else
        {
            var dllHistory = new List<GameHistory>();
            foreach (var existingBackupRecord in existingBackupRecords)
            {
                var primaryRecordName = existingBackupRecord.Path.Replace(".dlsss", string.Empty);
                var existingRecords = this.GameAssets.Where(x => x.AssetType == gameAssetType && x.Path.Equals(primaryRecordName)).ToList();

                if (existingRecords.Count != 1)
                {
                    Logger.Info("Backup record was found, existing records were not.");
                    return (false, "Unable to reset to default. Please repair your game manually.", false);
                }

                var existingRecord = existingRecords[0];

                try
                {
                    File.Move(existingBackupRecord.Path, existingRecord.Path, true);
                }
                catch (UnauthorizedAccessException err)
                {
                    Logger.Error(err);
                    if (App.CurrentApp.IsAdminUser() is false)
                    {
                        return (false, "Unable to reset to default. Running Chronos as administrator may fix this.", true);
                    }
                    else
                    {
                        return (false, "Unable to reset to default. Please repair your game manually.", false);
                    }
                }
                catch (Exception err)
                {
                    Logger.Error(err);
                    return (false, "Unable to reset to default. Please repair your game manually.", false);
                }

                var newGameAsset = new GameAsset()
                {
                    Id = ID,
                    AssetType = gameAssetType,
                    Path = existingRecord.Path,
                    Version = existingBackupRecord.Version,
                    Hash = existingBackupRecord.Hash,
                };


                dllHistory.Add(new GameHistory()
                {
                    GameId = ID,
                    EventType = GameHistoryEventType.DLLReset,
                    EventTime = DateTime.Now,
                    AssetType = gameAssetType,
                    AssetPath = existingRecord.Path,
                    AssetVersion = existingBackupRecord.DisplayName,
                });

                UpdateCurrentAsset(newGameAsset, gameAssetType);

                GameAssets.Remove(existingRecord);
                GameAssets.Remove(existingBackupRecord);
                GameAssets.Add(newGameAsset);
            }

            using (await Database.Instance.Mutex.LockAsync())
            {
                await Database.Instance.Connection.InsertAllAsync(dllHistory, false);

                // Update game assets list by deleting and re-adding.
                await Database.Instance.Connection.ExecuteAsync("DELETE FROM game_asset WHERE id = ?", ID).ConfigureAwait(false);
                await Database.Instance.Connection.InsertAllAsync(GameAssets, false).ConfigureAwait(false);
            }

            return (true, string.Empty, false);
        }
    }

    /// <summary>
    /// Works out which of the dlls we are about to overwrite still need their original preserved as
    /// a backup.
    /// </summary>
    /// <remarks>
    /// Extracted from <see cref="UpdateDllAsync"/> so the decision can be tested without a live
    /// WinUI Application, a database, or real game files.
    ///
    /// The bug this replaces: the caller only backed anything up when *no* backup existed at all,
    /// so a game with several copies of the same dll where only one had a backup would overwrite
    /// the rest without preserving them.
    /// </remarks>
    /// <param name="existingRecords">The dlls about to be overwritten.</param>
    /// <param name="allGameAssets">Every asset currently tracked for this game, backups included.</param>
    /// <param name="backupAssetType">The backup asset type, e.g. DLSS_BACKUP.</param>
    /// <returns>The paths, one per dll, that need a backup file created. Never null.</returns>
    internal static HashSet<string> GetPathsNeedingBackup(
        IEnumerable<GameAsset> existingRecords,
        IEnumerable<GameAsset> allGameAssets,
        GameAssetType backupAssetType)
    {
        var trackedBackupPaths = new HashSet<string>(
            allGameAssets
                .Where(x => x.AssetType == backupAssetType && string.IsNullOrWhiteSpace(x.Path) == false)
                .Select(x => x.Path),
            StringComparer.OrdinalIgnoreCase);

        var needsBackup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in existingRecords)
        {
            if (string.IsNullOrWhiteSpace(record.Path))
            {
                continue;
            }

            // Backup records store the ".dlsss" path, not the dll path, so compare against that.
            var backupPath = record.Path + ".dlsss";

            // Only skip when the original is both stored on disk *and* recorded. Either one on its
            // own is not enough: a file with no record gets orphaned by the database rewrite, and a
            // record with no file cannot be used to reset.
            if (trackedBackupPaths.Contains(backupPath) && File.Exists(backupPath))
            {
                continue;
            }

            needsBackup.Add(record.Path);
        }

        return needsBackup;
    }

    /// <summary>
    /// Attempts to update a DLSS dll in a given game.
    /// </summary>
    /// <param name="dlssRecord"></param>
    /// <returns>Tuple containing a boolean of Success, if this is false there will be an error message in the Message response.</returns>
    internal async Task<(bool Success, string Message, bool PromptToRelaunchAsAdmin)> UpdateDllAsync(DLLRecord dllRecord)
    {
        if (dllRecord is null)
        {
            return (false, "Unable to swap dll as your dll record was not found.", false);
        }

        if (dllRecord.LocalRecord is null)
        {
            return (false, "Unable to swap dll as your local dll record was not found.", false);
        }

        if (File.Exists(dllRecord.LocalRecord.ExpectedPath) == false)
        {
            return (false, "Downloaded dll not found.", false);
        }

        var existingRecords = this.GameAssets.Where(x => x.AssetType == dllRecord.AssetType).ToList();
        if (existingRecords.Count == 0)
        {
            return (false, "Unable to swap dll as there were no dll records to update.", false);
        }

        var backupRecordType = DLLManager.Instance.GetAssetBackupType(dllRecord.AssetType);

        var versionInfo = FileVersionInfo.GetVersionInfo(dllRecord.LocalRecord.ExpectedPath);
        var dllVersion = versionInfo.GetFormattedFileVersion();
        var md5Hash = versionInfo.GetMD5Hash();
        if (dllRecord.MD5Hash != md5Hash)
        {
            return (false, "Unable to swap dll because dll hash was invalid.", false);
        }


        // Validate new DLL
        if (Settings.Instance.AllowUntrusted == false)
        {
            var isTrusted = WinTrust.VerifyEmbeddedSignature(dllRecord.LocalRecord.ExpectedPath);
            if (isTrusted == false)
            {
                return (false, "Unable to swap dll as we are unable to verify the signature of the version you are trying to use.\nIf you wish to override this decision please enable 'Allow Untrusted' in settings.", false);
            }
        }

        var newGameAssets = new List<GameAsset>();

        // Back up every dll we are about to overwrite, checking each one individually rather than
        // treating "do we have any backups" as a single yes/no question. See GetPathsNeedingBackup
        // for why that distinction matters.
        var pathsNeedingBackup = GetPathsNeedingBackup(existingRecords, GameAssets, backupRecordType);

        foreach (var existingRecord in existingRecords)
        {
            var dllPath = Path.GetDirectoryName(existingRecord.Path);
            if (string.IsNullOrEmpty(dllPath))
            {
                Logger.Error("dllPath was null or empty.");
                return (false, "Unable to swap dll. Please check your error log for more information.", false);
            }

            var backupDllPath = $"{existingRecord.Path}.dlsss";

            if (pathsNeedingBackup.Contains(existingRecord.Path) == false)
            {
                // The original for this dll is already stored and recorded, leave it alone.
                continue;
            }

            try
            {
                if (File.Exists(backupDllPath) == false)
                {
                    // Never clobber an existing backup, it holds the game's original dll.
                    File.Copy(existingRecord.Path, backupDllPath);
                }

                // The file may already have been on disk with no record pointing at it, which used to
                // orphan it during the database rewrite below. Record it either way.
                var isAlreadyTracked = GameAssets.Any(x =>
                    x.AssetType == backupRecordType &&
                    string.Equals(x.Path, backupDllPath, StringComparison.OrdinalIgnoreCase));

                if (isAlreadyTracked == false)
                {
                    newGameAssets.Add(new GameAsset()
                    {
                        Id = ID,
                        AssetType = backupRecordType,
                        Path = backupDllPath,
                        Version = existingRecord.Version,
                        Hash = existingRecord.Hash,
                    });
                }
            }
            catch (UnauthorizedAccessException err)
            {
                Logger.Error(err);
                if (App.CurrentApp.IsAdminUser() is false)
                {
                    return (false, "Unable to swap dll as we are unable to write to the target directory. Running Chronos as administrator may fix this.", true);

                }
                else
                {
                    return (false, "Unable to swap dll as we are unable to write to the target directory.", false);
                }
            }
            catch (Exception err)
            {
                Logger.Error(err);
                return (false, "Unable to swap dll. Please check your error log for more information.", false);
            }
        }

        var dllHistory = new List<GameHistory>();

        foreach (var existingRecord in existingRecords)
        {
            try
            {
                // Copy the DLL
                File.Copy(dllRecord.LocalRecord.ExpectedPath, existingRecord.Path, true);

                var newGameAsset = new GameAsset()
                {
                    Id = ID,
                    AssetType = dllRecord.AssetType,
                    Path = existingRecord.Path,
                    Version = dllVersion,
                    Hash = dllRecord.MD5Hash,
                };
                // No need to call LoadVersionAndHash, the data is already here.
                newGameAssets.Add(newGameAsset);

                dllHistory.Add(new GameHistory()
                {
                    GameId = ID,
                    EventType = GameHistoryEventType.DLLSwapped,
                    EventTime = DateTime.Now,
                    AssetType = dllRecord.AssetType,
                    AssetPath = existingRecord.Path,
                    AssetVersion = dllRecord.DisplayName,
                });
            }
            catch (UnauthorizedAccessException err)
            {
                Logger.Error(err);
                if (App.CurrentApp.IsAdminUser() is false)
                {
                    return (false, "Unable to swap dll as we are unable to write to the target directory. Running Chronos as administrator may fix this.", true);
                }
                else
                {
                    return (false, "Unable to DLSS dll as we are unable to write to the target directory.", false);
                }
            }
            catch (IOException err) when (err.HResult == -2147024864)
            {
                Logger.Error(err);
                return (false, "Unable to swap dll. It appears to be in use by another program. Is your game currently running?", false);
            }
            catch (Exception err)
            {
                Logger.Error(err);
                return (false, "Unable to swap dll. Please check your error log for more information.", false);
            }
        }

        foreach (var existingRecrod in existingRecords)
        {
            GameAssets.Remove(existingRecrod);
        }
        GameAssets.AddRange(newGameAssets);

        // This should never be null.
        // Using FirstOrDefault as there may be multiple, but we only care about using the information of the first.
        var firstNewGameAsset = newGameAssets.FirstOrDefault(x => x.AssetType == dllRecord.AssetType);
        if (firstNewGameAsset is not null)
        {
            UpdateCurrentAsset(firstNewGameAsset, dllRecord.AssetType);
        }

        // Update game assets list by deleting and re-adding.
        using (await Database.Instance.Mutex.LockAsync())
        {
            await Database.Instance.Connection.InsertAllAsync(dllHistory, false);
            await Database.Instance.Connection.ExecuteAsync("DELETE FROM game_asset WHERE id = ?", ID).ConfigureAwait(false);
            await Database.Instance.Connection.InsertAllAsync(GameAssets, false).ConfigureAwait(false);
        }

        return (true, string.Empty, false);
    }

    void UpdateCurrentAsset(GameAsset newGameAsset, GameAssetType gameAssetType)
    {
        App.CurrentApp.RunOnUIThread(() =>
        {
            // NOTE: DLL type
            if (gameAssetType == GameAssetType.DLSS)
            {
                CurrentDLSS = null;
                CurrentDLSS = newGameAsset;
            }
            else if (gameAssetType == GameAssetType.DLSS_G)
            {
                CurrentDLSS_G = null;
                CurrentDLSS_G = newGameAsset;
            }
            else if (gameAssetType == GameAssetType.DLSS_D)
            {
                CurrentDLSS_D = null;
                CurrentDLSS_D = newGameAsset;
            }
            else if (gameAssetType == GameAssetType.FSR_31_DX12)
            {
                CurrentFSR_31_DX12 = null;
                CurrentFSR_31_DX12 = newGameAsset;
            }
            else if (gameAssetType == GameAssetType.FSR_31_VK)
            {
                CurrentFSR_31_VK = null;
                CurrentFSR_31_VK = newGameAsset;
            }
            else if (gameAssetType == GameAssetType.XeSS)
            {
                CurrentXeSS = null;
                CurrentXeSS = newGameAsset;
            }
            else if (gameAssetType == GameAssetType.XeSS_FG)
            {
                CurrentXeSS_FG = null;
                CurrentXeSS_FG = newGameAsset;
            }
            else if (gameAssetType == GameAssetType.XeSS_DX11)
            {
                CurrentXeSS_DX11 = null;
                CurrentXeSS_DX11 = newGameAsset;
            }
            else if (gameAssetType == GameAssetType.XeLL)
            {
                CurrentXeLL = null;
                CurrentXeLL = newGameAsset;
            }
            else
            {
                Logger.Error($"Unknown AssetType: {gameAssetType}");
            }
        });
    }

    #region IComparable<Game>
    /// <summary>
    /// Orders games by their <see cref="ID"/>.
    /// </summary>
    /// <remarks>
    /// This used to order by Title, which means two entirely different games that happened to
    /// share a title (or both had none) compared as 0, i.e. equal. Anything routing through
    /// IComparable - List&lt;Game&gt;.Sort, Comparer&lt;Game&gt;.Default, BinarySearch - treats a 0 as
    /// "same element" and can silently drop one of them. CompareTo has to agree with
    /// <see cref="Equals(Game?)"/>, and identity here is the ID.
    ///
    /// If you want games ordered by name, ask for it explicitly with OrderBy(g =&gt; g.Title)
    /// rather than relying on this.
    /// </remarks>
    public int CompareTo(Game? other)
    {
        if (other is null)
        {
            return 1;
        }

        return string.Compare(ID, other.ID, StringComparison.OrdinalIgnoreCase);
    }
    #endregion

    /*
    #region INotifyPropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged = null;
    void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    #endregion
    */


    protected async Task ResizeCoverAsync(Stream imageStream)
    {
        // TODO:
        // - find optimal format (eg, is displaying 100 webp images more intense than 100 png images)
        // - load image based on scale
        try
        {
            using (var image = await SixLabors.ImageSharp.Image.LoadAsync(imageStream).ConfigureAwait(false))
            {
                // If images are really big we resize to at least 2x the 200x300 we display as.
                // In future this should be updated to resize to display scale.
                // If the image is smaller than this we are just saving as png.
                var resizeOptions = new ResizeOptions()
                {
                    Size = new Size(200 * 2, 300 * 2),
                    Sampler = KnownResamplers.Lanczos5,
                    Mode = ResizeMode.Min, // If image is smaller it won't be resized up.
                };
                image.Mutate(x => x.Resize(resizeOptions));
                image.SaveAsPng(ExpectedCoverImage);
                //image.SaveAsWebp(ExpectedCoverImage);
                //image.SaveAsJpeg(ExpectedCoverImage);
            }

            App.CurrentApp.RunOnUIThread(() =>
            {
                CoverImage = null;
                CoverImage = ExpectedCoverImage;
            });
        }
        catch (Exception err)
        {
            Logger.Error(err);
        }
    }


    public void AddCustomCover(string imageSource)
    {
        using (var fileStream = File.OpenRead(imageSource))
        {
            AddCustomCover(fileStream);
        }
    }

    public void AddCustomCover(Stream stream)
    {
        // TODO:
        // - find optimal format (eg, is displaying 100 webp images more intense than 100 png images)
        // - load image based on scale
        try
        {
            using (var image = SixLabors.ImageSharp.Image.Load(stream))
            {
                // If images are really big we resize to at least 3x the 200x300 we display as.
                // In future this should be updated to resize to display scale.
                // If the image is smaller than this we are just saving as png.
                var resizeOptions = new ResizeOptions()
                {
                    Size = new Size(200 * 3, 300 * 3),
                    Sampler = KnownResamplers.Lanczos5,
                    Mode = ResizeMode.Min, // If image is smaller it won't be resized up.
                };
                image.Mutate(x => x.Resize(resizeOptions));
                image.SaveAsPng(ExpectedCustomCoverImage);
                //image.SaveAsWebp(ExpectedCustomCoverImage);
                //image.SaveAsJpeg(ExpectedCustomCoverImage);
            }

            App.CurrentApp.RunOnUIThread(() =>
            {
                CoverImage = ExpectedCustomCoverImage;
            });
        }
        catch (Exception err)
        {
            Logger.Error(err);
        }
    }

    protected async Task<bool> DownloadCoverAsync(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            Logger.Error($"Tried to download cover image but url was null or empty. Game: {Title}, Library: {GameLibrary}");
            return false;
        }

        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) == false &&
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) == false)
        {
            Logger.Error($"Tried to download cover image but url was not valid. Game: {Title}, Library: {GameLibrary}, Url: {url}");
            return false;
        }


        var extension = Path.GetExtension(url);

        // Path.GetExtension retains query arguments, so ths will remove them if they exist.
        if (extension.Contains('?'))
        {
            extension = extension.Substring(0, extension.IndexOf("?"));
        }
        var tempFile = Path.Combine(Storage.GetTemp(), $"{ID}{extension}");


        try
        {
            using (var memoryStream = new MemoryStream())
            {
                var fileDownloader = new FileDownloader(url, 0);
                await fileDownloader.DownloadFileToStreamAsync(memoryStream).ConfigureAwait(false);
                memoryStream.Position = 0;

                // Now if the image is downloaded lets resize it,
                await ResizeCoverAsync(memoryStream).ConfigureAwait(false);
            }
            return true;
        }
        catch (Exception err)
        {
            Logger.Error(err, $"For url: {url}");
            //Debugger.Break();
            return false;
        }
        finally
        {
            // Cleanup temp file.
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    public async Task SaveToDatabaseAsync()
    {
        try
        {
            var rowsChanged = -1;
            using (await Database.Instance.Mutex.LockAsync())
            {
                rowsChanged = await Database.Instance.Connection.InsertOrReplaceAsync(this);
                // tODO: Configure await
            }
            if (rowsChanged == 0)
            {
                // TODO: Fix why this happens occasionally to reandom games.
                // This appears to change to different games in different libraries.
                Logger.Error($"Tried to save game to database but rowsChanged was 0.");
                //Debugger.Break();
            }
        }
        catch (Exception err)
        {
            Logger.Error(err);
            Debugger.Break();
        }
    }

    public async Task DeleteAsync()
    {
        try
        {
            // Sometimes when a game is uninstalled the backup files are not removed, so ensure they are.
            // https://github.com/beeradmoore/dlss-swapper/issues/236

            List<GameAsset> gameAssets;
            using (await Database.Instance.Mutex.LockAsync())
            {
                gameAssets = await Database.Instance.Connection.Table<GameAsset>().Where(ga => ga.Id == ID).ToListAsync();
            }
            foreach (var cachedGameAsset in gameAssets)
            {
                // NOTE: DLL type
                // If its a file we made we should attempt to delete it.
                if (cachedGameAsset.AssetType == GameAssetType.DLSS_BACKUP ||
                    cachedGameAsset.AssetType == GameAssetType.DLSS_G_BACKUP ||
                    cachedGameAsset.AssetType == GameAssetType.DLSS_D_BACKUP ||
                    cachedGameAsset.AssetType == GameAssetType.FSR_31_DX12_BACKUP ||
                    cachedGameAsset.AssetType == GameAssetType.FSR_31_VK_BACKUP ||
                    cachedGameAsset.AssetType == GameAssetType.XeSS_BACKUP ||
                    cachedGameAsset.AssetType == GameAssetType.XeSS_FG_BACKUP ||
                    cachedGameAsset.AssetType == GameAssetType.XeSS_DX11_BACKUP ||
                    cachedGameAsset.AssetType == GameAssetType.XeLL_BACKUP)
                {
                    if (File.Exists(cachedGameAsset.Path))
                    {
                        Logger.Info($"Deleting {cachedGameAsset.Path}");
                        try
                        {
                            File.Delete(cachedGameAsset.Path);
                        }
                        catch (Exception err)
                        {
                            Logger.Error(err, $"Could not delete {cachedGameAsset.Path}");
                        }
                    }
                }
            }
            using (await Database.Instance.Mutex.LockAsync())
            {
                await Database.Instance.Connection.Table<GameAsset>().DeleteAsync(ga => ga.Id == ID).ConfigureAwait(false);
            }

            // Delete the thumbnails.
            // This used to use Directory.GetFiles(..., SearchOption.AllDirectories) with a "{ID}_*"
            // pattern, which walked (and could throw on) the entire shared image cache, and an
            // exception here aborted DeleteAsync before the game row was removed. Both cover paths
            // are deterministic, so just delete them directly.
            var thumbnailImages = new[] { ExpectedCoverImage, ExpectedCustomCoverImage };
            foreach (var thumbnailImage in thumbnailImages)
            {
                try
                {
                    if (File.Exists(thumbnailImage) == false)
                    {
                        continue;
                    }

                    Logger.Info($"Deleting {thumbnailImage}");
                    File.Delete(thumbnailImage);
                }
                catch (Exception err)
                {
                    Logger.Error(err, $"Could not delete {thumbnailImage}");
                }
            }

            // Delete the game itself.
            using (await Database.Instance.Mutex.LockAsync())
            {
                await Database.Instance.Connection.DeleteAsync(this).ConfigureAwait(false);
            }

            // Remove the game from the list.
            GameManager.Instance.RemoveGame(this);
        }
        catch (Exception err)
        {
            Logger.Error(err);
        }
    }

    public async Task PromptToRemoveCustomCover()
    {
        var dialog = new EasyContentDialog(App.CurrentApp.MainWindow.Content.XamlRoot)
        {
            Title = ResourceHelper.GetString("Game_CustomCoverRemove"),
            PrimaryButtonText = ResourceHelper.GetString("General_Remove"),
            CloseButtonText = ResourceHelper.GetString("General_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            Content = ResourceHelper.GetString("Game_AreYouSureRemoveCustomCover"),
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            CoverImage = null;

            if (File.Exists(ExpectedCustomCoverImage))
            {
                File.Delete(ExpectedCustomCoverImage);
            }

            if (this.GameLibrary == GameLibrary.ManuallyAdded)
            {
                await SaveToDatabaseAsync();
            }

            // Will load default or attempt to fetch fresh.
            await LoadCoverImageAsync();
        }
    }

    public void PromptToBrowseCustomCover()
    {
        try
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentApp.MainWindow);

            var fileFilters = new List<FileSystemHelper.FileFilter>()
            {
                new FileSystemHelper.FileFilter("Image files", "*.jpg; *.jpeg; *.png; *.webp"),
            };

            var coverImageFile = FileSystemHelper.OpenFile(hWnd, fileFilters, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));

            //                    ViewMode = PickerViewMode.Thumbnail,


            if (string.IsNullOrWhiteSpace(coverImageFile))
            {
                return;
            }

            AddCustomCover(coverImageFile);
        }
        catch (Exception err)
        {
            Logger.Error(err);
        }
    }

    /// <summary>
    /// Two games are the same game when they have the same <see cref="ID"/>.
    /// </summary>
    /// <remarks>
    /// ID is the database primary key and is always built as "{library}_{platformId}" by
    /// SetID(), for every library. That makes it the only field that identifies a game
    /// unambiguously.
    ///
    /// This previously also returned true whenever two games shared a PlatformId, ignoring which
    /// library they came from. PlatformId is only unique within a library, so a numeric id that
    /// appeared in two of them made one game compare equal to a different game, and
    /// GameManager.AddGame silently dropped one of them from the list.
    ///
    /// Identity is deliberately ID-only so that GetHashCode can agree with Equals. Mixing in
    /// PlatformId as a second criterion would break the contract, because SetID() sanitises
    /// PlatformId, so two different PlatformIds can produce the same ID.
    /// </remarks>
    public bool Equals(Game? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return string.Equals(ID, other.ID, StringComparison.OrdinalIgnoreCase);
    }

    public override int GetHashCode()
    {
        return StringComparer.OrdinalIgnoreCase.GetHashCode(ID ?? string.Empty);
    }

    protected bool ParentUpdateFromGame(Game game)
    {
        var didChange = false;

        if (Title != game.Title)
        {
            Title = game.Title;
            didChange = true;
        }

        if (InstallPath != game.InstallPath)
        {
            InstallPath = PathHelpers.NormalizePath(game.InstallPath);
            didChange = true;
        }

        if (CoverImage != game.CoverImage)
        {
            CoverImage = game.CoverImage;
            didChange = true;
        }

        if (HasSwappableItems != game.HasSwappableItems)
        {
            HasSwappableItems = game.HasSwappableItems;
            didChange = true;
        }

        // NOTE: DLL type
        if (CurrentDLSS != game.CurrentDLSS)
        {
            CurrentDLSS = game.CurrentDLSS;
            didChange = true;
        }

        if (DlssPreset != game.DlssPreset)
        {
            DlssPreset = game.DlssPreset;
            didChange = true;
        }

        if (DlssDPreset != game.DlssDPreset)
        {
            DlssDPreset = game.DlssDPreset;
            didChange = true;
        }

        if (CurrentDLSS_G != game.CurrentDLSS_G)
        {
            CurrentDLSS_G = game.CurrentDLSS_G;
            didChange = true;
        }

        if (CurrentDLSS_D != game.CurrentDLSS_D)
        {
            CurrentDLSS_D = game.CurrentDLSS_D;
            didChange = true;
        }

        if (CurrentFSR_31_DX12 != game.CurrentFSR_31_DX12)
        {
            CurrentFSR_31_DX12 = game.CurrentFSR_31_DX12;
            didChange = true;
        }

        if (CurrentFSR_31_VK != game.CurrentFSR_31_VK)
        {
            CurrentFSR_31_VK = game.CurrentFSR_31_VK;
            didChange = true;
        }

        if (CurrentXeSS != game.CurrentXeSS)
        {
            CurrentXeSS = game.CurrentXeSS;
            didChange = true;
        }

        if (CurrentXeSS_FG != game.CurrentXeSS_FG)
        {
            CurrentXeSS_FG = game.CurrentXeSS_FG;
            didChange = true;
        }

        if (CurrentXeSS_DX11 != game.CurrentXeSS_DX11)
        {
            CurrentXeSS_DX11 = game.CurrentXeSS_DX11;
            didChange = true;
        }

        if (CurrentXeLL != game.CurrentXeLL)
        {
            CurrentXeLL = game.CurrentXeLL;
            didChange = true;
        }

        // We don't copy across the following properties as it is assume this object has the latest revisions:
        // - Notes
        // - IsFavourite

        return didChange;
    }

    public abstract bool UpdateFromGame(Game game);

    void UpdateCurrentDLLsFromGameAssets()
    {
        CurrentDLSS = null;
        CurrentDLSS_G = null;
        CurrentDLSS_D = null;
        CurrentFSR_31_DX12 = null;
        CurrentFSR_31_VK = null;
        CurrentXeSS = null;
        CurrentXeSS_FG = null;
        CurrentXeSS_DX11 = null;
        CurrentXeLL = null;

        // NOTE: DLL type
        MultipleDLSSFound = GameAssets.Count(x => x.AssetType == GameAssetType.DLSS) > 1;
        MultipleDLSSGFound = GameAssets.Count(x => x.AssetType == GameAssetType.DLSS_G) > 1;
        MultipleDLSSDFound = GameAssets.Count(x => x.AssetType == GameAssetType.DLSS_D) > 1;
        MultipleFSR31DX12Found = GameAssets.Count(x => x.AssetType == GameAssetType.FSR_31_DX12) > 1;
        MultipleFSR31VKFound = GameAssets.Count(x => x.AssetType == GameAssetType.FSR_31_VK) > 1;
        MultipleXeSSFound = GameAssets.Count(x => x.AssetType == GameAssetType.XeSS) > 1;
        MultipleXeSSFGFound = GameAssets.Count(x => x.AssetType == GameAssetType.XeSS_FG) > 1;
        MultipleXeSSDX11Found = GameAssets.Count(x => x.AssetType == GameAssetType.XeSS_DX11) > 1;
        MultipleXeLLFound = GameAssets.Count(x => x.AssetType == GameAssetType.XeLL) > 1;

        // NOTE: DLL type
        foreach (var gameAsset in GameAssets)
        {
            if (gameAsset.AssetType == GameAssetType.DLSS)
            {
                CurrentDLSS = gameAsset;
            }
            else if (gameAsset.AssetType == GameAssetType.DLSS_G)
            {
                CurrentDLSS_G = gameAsset;
            }
            else if (gameAsset.AssetType == GameAssetType.DLSS_D)
            {
                CurrentDLSS_D = gameAsset;
            }
            else if (gameAsset.AssetType == GameAssetType.FSR_31_DX12)
            {
                CurrentFSR_31_DX12 = gameAsset;
            }
            else if (gameAsset.AssetType == GameAssetType.FSR_31_VK)
            {
                CurrentFSR_31_VK = gameAsset;
            }
            else if (gameAsset.AssetType == GameAssetType.XeSS)
            {
                CurrentXeSS = gameAsset;
            }
            else if (gameAsset.AssetType == GameAssetType.XeSS_FG)
            {
                CurrentXeSS_FG = gameAsset;
            }
            else if (gameAsset.AssetType == GameAssetType.XeSS_DX11)
            {
                CurrentXeSS_DX11 = gameAsset;
            }
            else if (gameAsset.AssetType == GameAssetType.XeLL)
            {
                CurrentXeLL = gameAsset;
            }
        }
    }

    public async Task RemoveGameAssetsFromCacheAsync()
    {
        using (await Database.Instance.Mutex.LockAsync())
        {
            await Database.Instance.Connection.ExecuteAsync("DELETE FROM game_asset WHERE id = ?", ID).ConfigureAwait(false);
        }
    }

    public async Task LoadGameAssetsFromCacheAsync()
    {
        await LoadCoverImageAsync();

        GameAssets.Clear();
        using (await Database.Instance.Mutex.LockAsync())
        {
            var gameAssets = await Database.Instance.Connection.Table<GameAsset>().Where(ga => ga.Id == ID).ToListAsync().ConfigureAwait(false);
            if (gameAssets?.Any() == true)
            {
                GameAssets.AddRange(gameAssets);
            }
        }

        UpdateCurrentDLLsFromGameAssets();

        // TODO: Add auto reload by storing last full reload time on game

        if (GameAssets.Any())
        {
            foreach (var gameAsset in GameAssets)
            {
                // Check that each of the game assets exist, after we will check if they are what we expect them to be
                if (File.Exists(gameAsset.Path) == false)
                {
                    NeedsProcessing = true;
                    break;
                }
            }

            if (NeedsProcessing == false)
            {
                var unknownGameAssets = new List<GameAsset>();
                foreach (var gameAsset in GameAssets)
                {
                    if (DLLManager.Instance.IsInKnownGameAsset(gameAsset, this) == false)
                    {
                        unknownGameAssets.Add(gameAsset);
                    }
                }
                if (unknownGameAssets.Any())
                {
                    GameManager.Instance.AddUnknownGameAssets(GameLibrary, Title, unknownGameAssets);
                }

                foreach (var gameAsset in GameAssets)
                {
                    var fileVersionInfo = FileVersionInfo.GetVersionInfo(gameAsset.Path);
                    var freshVersion = fileVersionInfo.GetFormattedFileVersion();

                    if (gameAsset.Version != freshVersion)
                    {
                        NeedsProcessing = true;
                        break;
                    }
                }
            }
        }
        else
        {
            // If there is no known current DLLs then we likely want to do a full reload in case the game got updated.
            // TODO: Also add a time last reloaded here.
            NeedsProcessing = true;
            return;
        }
    }

    public bool IsInIgnoredPath()
    {
        // If there are no ignored paths we can skip this altogether.
        if (Settings.Instance.IgnoredPaths.Length == 0)
        {
            return false;
        }

        // If installed path is empty we should consider it ignored.
        if (string.IsNullOrWhiteSpace(InstallPath))
        {
            return true;
        }

        foreach (var ignoredPath in Settings.Instance.IgnoredPaths)
        {
            // Because we make IgnoredPaths have a / on the end it will fail the below check.
            // In the cases where the path could be off by one we will do a manual check.
            if (ignoredPath.Length - 1 == InstallPath.Length)
            {
                var tempInstallPath = InstallPath + Path.DirectorySeparatorChar;
                if (tempInstallPath.Equals(ignoredPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }


            if (InstallPath.StartsWith(ignoredPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
