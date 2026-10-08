using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kronos.Data;

namespace Kronos.Models;

public record GameInfo
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string InstallPath { get; init; } = string.Empty;
    public string? CoverImage { get; init; }
    public bool IsHidden { get; init; }

    // DLL versions and status
    public string? DlssVersion { get; init; }
    public DllStatus DlssStatus { get; init; }
    public string? DlssGVersion { get; init; }
    public DllStatus DlssGStatus { get; init; }
    public string? DlssDVersion { get; init; }
    public DllStatus DlssDStatus { get; init; }
    public string? Fsr31Dx12Version { get; init; }
    public DllStatus Fsr31Dx12Status { get; init; }
    public string? Fsr31VkVersion { get; init; }
    public DllStatus Fsr31VkStatus { get; init; }
    public string? XessVersion { get; init; }
    public DllStatus XessStatus { get; init; }
    public string? XessFgVersion { get; init; }
    public DllStatus XessFgStatus { get; init; }
    public string? XessDx11Version { get; init; }
    public DllStatus XessDx11Status { get; init; }
    public string? XellVersion { get; init; }
    public DllStatus XellStatus { get; init; }
}

public enum DllStatus
{
    Original,
    Swapped,
    Unknown,
    Missing
}

public enum DllType
{
    DLSS,
    DLSS_G,
    DLSS_D,
    FSR_31_DX12,
    FSR_31_VK,
    XeSS,
    XeSS_FG,
    XeSS_DX11,
    XeLL
}

public record UpdateInfo
{
    public string Version { get; init; } = string.Empty;
    public string ReleaseNotes { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public string AssetName { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public long FileSize { get; init; }
}

public record SwapResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}

public record SettingsModel
{
    public string DownloadPath { get; init; } = string.Empty;
    public bool AllowUntrusted { get; init; } = false;
    public bool CheckSignatures { get; init; } = true;
    public bool AutoCheckUpdates { get; init; } = true;
    public int MaxConcurrentDownloads { get; init; } = 4;
    public List<string> IgnoredPaths { get; init; } = new();
}