using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DLSS_Swapper.Data;

/// <summary>
/// Describes a single swappable asset type (e.g. DLSS, FSR 3.1 DX12, XeSS) and how to reach the
/// various lists that are keyed off of it.
/// </summary>
/// <remarks>
/// This exists so that adding a new DLL type is a single entry in <see cref="DLLAssetTypes"/> rather
/// than editing a dozen "// NOTE: DLL type" if/else chains spread across DLLManager. Those chains had
/// already drifted out of sync with each other.
/// </remarks>
internal sealed class DLLAssetTypeInfo
{
    internal DLLAssetTypeInfo(
        GameAssetType assetType,
        GameAssetType backupAssetType,
        string dllName,
        string displayNameResourceKey,
        Func<Manifest, List<DLLRecord>> manifestRecords,
        Func<KnownDLLs, List<HashedKnownDLL>> knownDLLRecords,
        Func<DLLManager, ObservableCollection<DLLRecord>> records)
    {
        AssetType = assetType;
        BackupAssetType = backupAssetType;
        DllName = dllName;
        DisplayNameResourceKey = displayNameResourceKey;
        ManifestRecords = manifestRecords;
        KnownDLLRecords = knownDLLRecords;
        Records = records;
    }

    /// <summary>The non-backup asset type, e.g. <see cref="GameAssetType.DLSS"/>.</summary>
    internal GameAssetType AssetType { get; }

    /// <summary>The matching backup asset type, e.g. <see cref="GameAssetType.DLSS_BACKUP"/>.</summary>
    internal GameAssetType BackupAssetType { get; }

    /// <summary>The on-disk DLL name, e.g. "nvngx_dlss.dll".</summary>
    internal string DllName { get; }

    /// <summary>Resource key used to build the localized display name.</summary>
    internal string DisplayNameResourceKey { get; }

    /// <summary>Gets this type's list out of a (dynamic) manifest.</summary>
    internal Func<Manifest, List<DLLRecord>> ManifestRecords { get; }

    /// <summary>Gets this type's list out of a known-DLLs manifest.</summary>
    internal Func<KnownDLLs, List<HashedKnownDLL>> KnownDLLRecords { get; }

    /// <summary>Gets the bindable master record collection for this type.</summary>
    internal Func<DLLManager, ObservableCollection<DLLRecord>> Records { get; }
}

/// <summary>
/// The single source of truth for every swappable DLL type.
/// </summary>
internal static class DLLAssetTypes
{
    private static readonly IReadOnlyList<DLLAssetTypeInfo> _all = new DLLAssetTypeInfo[]
    {
        // NOTE: DLL type -- add new types here and nowhere else.
        new(GameAssetType.DLSS, GameAssetType.DLSS_BACKUP, "nvngx_dlss.dll", "General_Name_DLSS",
            m => m.DLSS, k => k.DLSS, d => d.DLSSRecords),

        new(GameAssetType.DLSS_G, GameAssetType.DLSS_G_BACKUP, "nvngx_dlssg.dll", "General_Name_DLSS_G",
            m => m.DLSS_G, k => k.DLSS_G, d => d.DLSSGRecords),

        new(GameAssetType.DLSS_D, GameAssetType.DLSS_D_BACKUP, "nvngx_dlssd.dll", "General_Name_DLSS_D",
            m => m.DLSS_D, k => k.DLSS_D, d => d.DLSSDRecords),

        new(GameAssetType.FSR_31_DX12, GameAssetType.FSR_31_DX12_BACKUP, "amd_fidelityfx_dx12.dll", "General_Name_FSR_31_DX12",
            m => m.FSR_31_DX12, k => k.FSR_31_DX12, d => d.FSR31DX12Records),

        new(GameAssetType.FSR_31_VK, GameAssetType.FSR_31_VK_BACKUP, "amd_fidelityfx_vk.dll", "General_Name_FSR_31_VK",
            m => m.FSR_31_VK, k => k.FSR_31_VK, d => d.FSR31VKRecords),

        new(GameAssetType.XeSS, GameAssetType.XeSS_BACKUP, "libxess.dll", "General_Name_XeSS",
            m => m.XeSS, k => k.XeSS, d => d.XeSSRecords),

        new(GameAssetType.XeLL, GameAssetType.XeLL_BACKUP, "libxell.dll", "General_Name_XeLL",
            m => m.XeLL, k => k.XeLL, d => d.XeLLRecords),

        new(GameAssetType.XeSS_FG, GameAssetType.XeSS_FG_BACKUP, "libxess_fg.dll", "General_Name_XeSS_FG",
            m => m.XeSS_FG, k => k.XeSS_FG, d => d.XeSSFGRecords),

        new(GameAssetType.XeSS_DX11, GameAssetType.XeSS_DX11_BACKUP, "libxess_dx11.dll", "General_Name_XeSS_DX11",
            m => m.XeSS_DX11, k => k.XeSS_DX11, d => d.XeSSDX11Records),
    };

    /// <summary>Every supported asset type.</summary>
    internal static IReadOnlyList<DLLAssetTypeInfo> All => _all;

    /// <summary>
    /// Maps a non-backup or backup asset type to its info. Backup types resolve to the same entry
    /// as their non-backup counterpart so callers do not have to strip the suffix themselves.
    /// </summary>
    private static readonly Dictionary<GameAssetType, DLLAssetTypeInfo> _byAssetOrBackupType =
        _all.SelectMany(x => new[] { (x.AssetType, x), (x.BackupAssetType, x) })
            .ToDictionary(x => x.Item1, x => x.Item2);

    private static readonly Dictionary<string, DLLAssetTypeInfo> _byDllName =
        _all.ToDictionary(x => x.DllName, StringComparer.OrdinalIgnoreCase);

    /// <summary>Looks up an asset type, or returns null if it is not a type we can swap.</summary>
    internal static DLLAssetTypeInfo? Find(GameAssetType assetType)
        => _byAssetOrBackupType.TryGetValue(assetType, out var info) ? info : null;

    /// <summary>Looks up an asset type by the DLL file name, or returns null if unrecognised.</summary>
    internal static DLLAssetTypeInfo? FindByDllName(string? dllName)
        => dllName is not null && _byDllName.TryGetValue(dllName, out var info) ? info : null;

    /// <summary>
    /// Gets the info for an asset type, throwing if it is not a type we can swap. Use
    /// <see cref="Find"/> when the type is expected to possibly be unknown.
    /// </summary>
    internal static DLLAssetTypeInfo Get(GameAssetType assetType)
        => Find(assetType) ?? throw new ArgumentOutOfRangeException(
            nameof(assetType), assetType, $"Unknown or unswappable AssetType.");
}
