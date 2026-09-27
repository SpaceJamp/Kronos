using DLSS_Swapper.Data;

namespace DLSS_Swapper.Tests.Data;

/// <summary>
/// Tests for the DLL asset type registry. The registry replaced nine hand-maintained
/// "// NOTE: DLL type" if/else chains, so these guard the mapping that all of them depended on.
/// </summary>
public class DLLAssetTypesTests
{
    [Fact]
    public void All_ContainsEveryNonBackupTypeExactlyOnce()
    {
        var seen = new HashSet<GameAssetType>();

        foreach (var info in DLLAssetTypes.All)
        {
            Assert.True(seen.Add(info.AssetType), $"{info.AssetType} appears twice in DLLAssetTypes.All.");
        }
    }

    [Fact]
    public void All_DoesNotContainBackupTypesAsPrimaryEntries()
    {
        // A backup type must only ever be reachable via BackupAssetType, never as its own entry.
        // Otherwise the two dictionary lookups would collide and one would silently win.
        foreach (var info in DLLAssetTypes.All)
        {
            Assert.DoesNotContain(DLLAssetTypes.All, x => x.AssetType == info.BackupAssetType);
        }
    }

    [Fact]
    public void All_NoTwoTypesShareADllName()
    {
        // FindByDllName is a dictionary keyed on DllName; duplicates would throw at type init.
        var names = DLLAssetTypes.All.Select(x => x.DllName).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void All_NoTwoTypesShareABackupType()
    {
        var backups = DLLAssetTypes.All.Select(x => x.BackupAssetType).ToList();
        Assert.Equal(backups.Count, backups.Distinct().Count());
    }

    [Fact]
    public void All_DllNamesAreLowercaseDllExtensions()
    {
        foreach (var info in DLLAssetTypes.All)
        {
            Assert.EndsWith(".dll", info.DllName, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(info.DllName.ToLowerInvariant(), info.DllName);
        }
    }

    [Theory]
    [InlineData("nvngx_dlss.dll", GameAssetType.DLSS)]
    [InlineData("nvngx_dlssg.dll", GameAssetType.DLSS_G)]
    [InlineData("nvngx_dlssd.dll", GameAssetType.DLSS_D)]
    [InlineData("amd_fidelityfx_dx12.dll", GameAssetType.FSR_31_DX12)]
    [InlineData("amd_fidelityfx_vk.dll", GameAssetType.FSR_31_VK)]
    [InlineData("libxess.dll", GameAssetType.XeSS)]
    [InlineData("libxell.dll", GameAssetType.XeLL)]
    [InlineData("libxess_fg.dll", GameAssetType.XeSS_FG)]
    [InlineData("libxess_dx11.dll", GameAssetType.XeSS_DX11)]
    public void FindByDllName_ResolvesKnownNames(string dllName, GameAssetType expected)
    {
        Assert.Equal(expected, DLLAssetTypes.FindByDllName(dllName)?.AssetType);
    }

    [Theory]
    [InlineData("NVNGX_DLSS.DLL")]
    [InlineData("NvNgX_DlSs.DlL")]
    public void FindByDllName_IsCaseInsensitive(string dllName)
    {
        // DLLManager.GetExpectedDllFileName compares extracted zip entries with OrdinalIgnoreCase,
        // so the registry lookup has to agree with that.
        Assert.Equal(GameAssetType.DLSS, DLLAssetTypes.FindByDllName(dllName)?.AssetType);
    }

    [Theory]
    [InlineData("")]
    [InlineData("notadll.dll")]
    [InlineData("nvngx_dlss.so")]
    [InlineData("nvngx_dlss.dll.bak")]
    public void FindByDllName_ReturnsNullForUnrecognisedNames(string dllName)
    {
        Assert.Null(DLLAssetTypes.FindByDllName(dllName));
    }

    [Fact]
    public void FindByDllName_ReturnsNullForNull()
    {
        Assert.Null(DLLAssetTypes.FindByDllName(null));
    }

    [Theory]
    [InlineData(GameAssetType.DLSS)]
    [InlineData(GameAssetType.DLSS_G)]
    [InlineData(GameAssetType.DLSS_D)]
    [InlineData(GameAssetType.FSR_31_DX12)]
    [InlineData(GameAssetType.FSR_31_VK)]
    [InlineData(GameAssetType.XeSS)]
    [InlineData(GameAssetType.XeLL)]
    [InlineData(GameAssetType.XeSS_FG)]
    [InlineData(GameAssetType.XeSS_DX11)]
    public void Find_ResolvesPrimaryTypeToItself(GameAssetType assetType)
    {
        var info = DLLAssetTypes.Find(assetType);

        Assert.NotNull(info);
        Assert.Equal(assetType, info!.AssetType);
    }

    [Theory]
    [InlineData(GameAssetType.DLSS_BACKUP, GameAssetType.DLSS)]
    [InlineData(GameAssetType.DLSS_G_BACKUP, GameAssetType.DLSS_G)]
    [InlineData(GameAssetType.DLSS_D_BACKUP, GameAssetType.DLSS_D)]
    [InlineData(GameAssetType.FSR_31_DX12_BACKUP, GameAssetType.FSR_31_DX12)]
    [InlineData(GameAssetType.FSR_31_VK_BACKUP, GameAssetType.FSR_31_VK)]
    [InlineData(GameAssetType.XeSS_BACKUP, GameAssetType.XeSS)]
    [InlineData(GameAssetType.XeLL_BACKUP, GameAssetType.XeLL)]
    [InlineData(GameAssetType.XeSS_FG_BACKUP, GameAssetType.XeSS_FG)]
    [InlineData(GameAssetType.XeSS_DX11_BACKUP, GameAssetType.XeSS_DX11)]
    public void Find_ResolvesBackupTypeToItsParent(GameAssetType backupType, GameAssetType expectedParent)
    {
        // IsInKnownGameAsset is called for backup assets too, so backup types have to resolve.
        Assert.Equal(expectedParent, DLLAssetTypes.Find(backupType)?.AssetType);
    }

    [Theory]
    [InlineData(GameAssetType.Unknown)]
    [InlineData(GameAssetType.DirectStorage)]
    [InlineData(GameAssetType.Streamline_DLSS)]
    [InlineData(GameAssetType.DLSS_NR)]
    [InlineData(GameAssetType.DeepDVC)]
    public void Find_ReturnsNullForTypesWeCannotSwap(GameAssetType assetType)
    {
        Assert.Null(DLLAssetTypes.Find(assetType));
    }

    [Fact]
    public void Get_ThrowsForUnknownType()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DLLAssetTypes.Get(GameAssetType.Unknown));
    }

    [Fact]
    public void Get_ReturnsBackupTypeForEveryKnownType()
    {
        foreach (var info in DLLAssetTypes.All)
        {
            Assert.Equal(info.BackupAssetType, DLLAssetTypes.Get(info.AssetType).BackupAssetType);
        }
    }

    [Fact]
    public void DllNameForGameAssetType_ReturnsNameForPrimaryType()
    {
        foreach (var info in DLLAssetTypes.All)
        {
            Assert.Equal(info.DllName, DLLManager.DllNameForGameAssetType(info.AssetType));
        }
    }

    [Fact]
    public void DllNameForGameAssetType_ReturnsEmptyForBackupType()
    {
        // Preserved deliberately: a backup record has no file name of its own, and callers such as
        // GetExpectedDllFileName rely on the empty string to bail out.
        foreach (var info in DLLAssetTypes.All)
        {
            Assert.Equal(string.Empty, DLLManager.DllNameForGameAssetType(info.BackupAssetType));
        }
    }

    [Theory]
    [InlineData(GameAssetType.Unknown)]
    [InlineData(GameAssetType.DirectStorage)]
    public void DllNameForGameAssetType_ReturnsEmptyForUnswappableType(GameAssetType assetType)
    {
        Assert.Equal(string.Empty, DLLManager.DllNameForGameAssetType(assetType));
    }
}
