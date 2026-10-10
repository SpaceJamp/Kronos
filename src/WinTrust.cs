using System;
using System.Runtime.InteropServices;

namespace Kronos;

/// <summary>
/// The outcome of an Authenticode signature check.
/// </summary>
/// <remarks>
/// Declared outside the #if so both platforms return the same type and the callers are not
/// duplicated per platform.
///
/// The distinction between <see cref="Invalid"/> and <see cref="Unavailable"/> is the point of it.
/// A plain bool could not express it: returning <c>true</c> where nothing was checked says
/// "verified", which is false, and returning <c>false</c> says "untrusted", which is also false and
/// would block a feature that has no alternative. Both are worse than saying which of the three
/// things actually happened.
/// </remarks>
public enum SignatureCheckResult
{
    /// <summary>The file carries a valid Authenticode signature from a trusted signer.</summary>
    Valid,

    /// <summary>The file was checked and did not verify - missing, malformed, or untrusted signer.</summary>
    Invalid,

    /// <summary>
    /// The platform has no Authenticode implementation, so nothing was checked.
    /// </summary>
    /// <remarks>
    /// Every call returns this on Linux. It is not a pass. Callers decide separately what to do, and
    /// must not present it to the user as a verification that took place.
    /// </remarks>
    Unavailable
}

/// <summary>
/// Wording shown whenever a file was accepted without its signature being checked.
/// </summary>
/// <remarks>
/// Declared outside the #if because the callers that surface it are shared code - the import flow
/// lives in LibraryPageModel, which compiles for both targets. On Windows it is never displayed,
/// because there <see cref="SignatureCheckResult.Unavailable"/> cannot be returned; it exists so
/// that branch has something to say if that ever changes.
/// </remarks>
internal static class SignatureWarning
{
    internal const string UnsupportedPlatform =
        "WARNING: This platform cannot verify Authenticode signatures, so this file was NOT checked. " +
        "It was accepted because it is a file you already had on disk, not because it was verified. " +
        "On Windows this step would reject an untrusted DLL.";
}

// Full implementation taken from here: https://docs.microsoft.com/en-us/windows/win32/seccrypto/example-c-program--verifying-the-signature-of-a-pe-file
// Help also from the comments in here: https://www.pinvoke.net/default.aspx/wintrust.winverifytrust

#if WINDOWS
internal static class WinTrust
{
    // GUID of the action to perform
    internal const string WINTRUST_ACTION_GENERIC_VERIFY_V2 = "00AAC56B-CD44-11d0-8CC2-00C04FC295EE";

    internal enum WinVerifyTrustResult : uint
    {
        TRUST_E_NOSIGNATURE = 0x800B0100,
        TRUST_E_SUBJECT_FORM_UNKNOWN = 0x800B0003,
        TRUST_E_PROVIDER_UNKNOWN = 0x800B0001,
        TRUST_E_EXPLICIT_DISTRUST = 0x800B0111,
        ERROR_SUCCESS = 0x0,
        TRUST_E_SUBJECT_NOT_TRUSTED = 0x800B0004,
        CRYPT_E_SECURITY_SETTINGS = 0x80092026,
        CRYPT_E_FILE_ERROR = 0x80092003,
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern WinVerifyTrustResult WinVerifyTrust(
        [In] IntPtr hwnd,
        [In][MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID,
        ref WinTrustData pWVTData
    );

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WinTrustFileInfo
    {
        public UInt32 cbStruct { get; private set; }
        public IntPtr pcwszFilePath { get; private set; }
        public IntPtr hFile { get; private set; }
        public IntPtr pgKnownSubject { get; private set; }

        public WinTrustFileInfo(string filePath)
        {
            cbStruct = (UInt32)Marshal.SizeOf<WinTrustFileInfo>();
            pcwszFilePath = Marshal.StringToCoTaskMemAuto(filePath);
            hFile = IntPtr.Zero;
            pgKnownSubject = IntPtr.Zero;
        }

        public void Dispose()
        {
            if (pcwszFilePath != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pcwszFilePath);
                pcwszFilePath = IntPtr.Zero;
            }
        }
    }

    internal enum WinTrustDataUIChoice : uint
    {
        All = 1,
        None = 2,
        NoBad = 3,
        NoGood = 4
    }

    internal enum WinTrustDataRevocationChecks : uint
    {
        None = 0x00000000,
        WholeChain = 0x00000001
    }

    internal enum WinTrustDataChoice : uint
    {
        File = 1,
        Catalog = 2,
        Blob = 3,
        Signer = 4,
        Certificate = 5
    }

    internal enum WinTrustDataStateAction : uint
    {
        Ignore = 0x00000000,
        Verify = 0x00000001,
        Close = 0x00000002,
        AutoCache = 0x00000003,
        AutoCacheFlush = 0x00000004
    }

    [FlagsAttribute]
    internal enum WinTrustDataProvFlags : uint
    {
        ProvFlagsMask = 0x0000FFFF,
        UseIe4TrustFlag = 0x00000001,
        NoIe4ChainFlag = 0x00000002,
        NoPolicyUsageFlag = 0x00000004,
        RevocationCheckNone = 0x00000010,
        RevocationCheckEndCert = 0x00000020,
        RevocationCheckChain = 0x00000040,
        RevocationCheckChainExcludeRoot = 0x00000080,
        SaferFlag = 0x00000100,
        HashOnlyFlag = 0x00000200,
        UseDefaultOsverCheck = 0x00000400,
        LifetimeSigningFlag = 0x00000800,
        CacheOnlyUrlRetrieval = 0x00001000,
        DisableMD2andMD4 = 0x00002000,
        MarkOfTheWeb = 0x00004000,
        CodeIntegrityDriverMode = 0x00008000,
    }

    internal enum WinTrustDataUIContext : uint
    {
        Execute = 0,
        Install = 1
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WinTrustData
    {
        public UInt32 cbStruct { get; set; }
        public IntPtr pPolicyCallbackData { get; set; }
        public IntPtr pSIPClientData { get; set; }
        public WinTrustDataUIChoice dwUIChoice { get; set; }
        public WinTrustDataRevocationChecks fdwRevocationChecks { get; set; }
        public WinTrustDataChoice dwUnionChoice { get; set; }
        public IntPtr pFile { get; set; }
        public WinTrustDataStateAction dwStateAction { get; set; }
        public IntPtr hWVTStateData { get; set; }
        public string? pwszURLReference { get; set; }
        public WinTrustDataProvFlags dwProvFlags { get; set; }
        public WinTrustDataUIContext dwUIContext { get; set; }

        public WinTrustData(WinTrustFileInfo fileInfo)
        {
            cbStruct = (UInt32)Marshal.SizeOf<WinTrustData>();
            pPolicyCallbackData = IntPtr.Zero;
            pSIPClientData = IntPtr.Zero;
            dwUIChoice = WinTrustDataUIChoice.None;
            fdwRevocationChecks = WinTrustDataRevocationChecks.None;
            dwUnionChoice = WinTrustDataChoice.File;
            pFile = IntPtr.Zero;
            dwStateAction = WinTrustDataStateAction.Ignore;
            hWVTStateData = IntPtr.Zero;
            pwszURLReference = null;
            dwProvFlags = WinTrustDataProvFlags.RevocationCheckChainExcludeRoot;
            dwUIContext = WinTrustDataUIContext.Execute;

            if ((Environment.OSVersion.Version.Major > 6) ||
                ((Environment.OSVersion.Version.Major == 6) && (Environment.OSVersion.Version.Minor > 1)) ||
                ((Environment.OSVersion.Version.Major == 6) && (Environment.OSVersion.Version.Minor == 1) && !string.IsNullOrEmpty(Environment.OSVersion.ServicePack)))
            {
                dwProvFlags |= WinTrustDataProvFlags.DisableMD2andMD4;
            }

            WinTrustFileInfo wtfiData = fileInfo;
            pFile = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(wtfiData, pFile, false);
        }

        public void Dispose()
        {
            if (pFile != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pFile);
                pFile = IntPtr.Zero;
            }
        }
    }

    public static SignatureCheckResult VerifyEmbeddedSignature(string fileName)
    {
        WinVerifyTrustResult lStatus;
        uint dwLastError;

        WinTrustFileInfo FileData = default;
        WinTrustData WinTrustData = default;

        var validSignature = false;
        try
        {
            FileData = new WinTrustFileInfo(fileName);

            var WVTPolicyGUID = new Guid(WINTRUST_ACTION_GENERIC_VERIFY_V2);

            WinTrustData = new WinTrustData(FileData)
            {
                cbStruct = (UInt32)Marshal.SizeOf<WinTrustData>(),
                pPolicyCallbackData = IntPtr.Zero,
                pSIPClientData = IntPtr.Zero,
                dwUIChoice = WinTrustDataUIChoice.None,
                fdwRevocationChecks = WinTrustDataRevocationChecks.None,
                dwUnionChoice = WinTrustDataChoice.File,
                dwStateAction = WinTrustDataStateAction.Verify,
                hWVTStateData = IntPtr.Zero,
                pwszURLReference = null,
                dwUIContext = 0,
            };

            lStatus = WinVerifyTrust(IntPtr.Zero, WVTPolicyGUID, ref WinTrustData);

            switch (lStatus)
            {
                case WinVerifyTrustResult.ERROR_SUCCESS:
                    validSignature = true;
                    Logger.Info($"The file \"{fileName}\" is signed and the signature was verified.");
                    break;

                case WinVerifyTrustResult.TRUST_E_NOSIGNATURE:
                    dwLastError = (uint)Marshal.GetLastWin32Error();
                    if ((uint)WinVerifyTrustResult.TRUST_E_NOSIGNATURE == dwLastError ||
                        (uint)WinVerifyTrustResult.TRUST_E_SUBJECT_FORM_UNKNOWN == dwLastError ||
                        (uint)WinVerifyTrustResult.TRUST_E_PROVIDER_UNKNOWN == dwLastError)
                    {
                        Logger.Warning($"The file \"{fileName}\" is not signed.");
                    }
                    else
                    {
                        Logger.Error($"An unknown error occurred trying to verify the signature of the \"{fileName}\" file.");
                    }
                    break;

                case WinVerifyTrustResult.TRUST_E_EXPLICIT_DISTRUST:
                    Logger.Warning("The signature is present, but specifically disallowed.");
                    break;

                case WinVerifyTrustResult.TRUST_E_SUBJECT_NOT_TRUSTED:
                    Logger.Error("The signature is present, but not trusted.");
                    break;

                case WinVerifyTrustResult.CRYPT_E_SECURITY_SETTINGS:
                    Logger.Error("CRYPT_E_SECURITY_SETTINGS - The hash representing the subject or the publisher wasn't explicitly trusted by the admin and admin policy has disabled user trust. No signature, publisher or timestamp errors.");
                    break;

                case WinVerifyTrustResult.CRYPT_E_FILE_ERROR:
                    Logger.Error("CRYPT_E_FILE_ERROR - An error occurred while reading or writing to a file.");
                    break;

                default:
                    Logger.Error($"Error is: 0x{lStatus}.");
                    break;
            }

            WinTrustData.dwStateAction = WinTrustDataStateAction.Close;
            lStatus = WinVerifyTrust(IntPtr.Zero, WVTPolicyGUID, ref WinTrustData);
        }
        catch (Exception err)
        {
            Logger.Error(err);
        }
        finally
        {
            FileData.Dispose();
            WinTrustData.Dispose();
        }

        return validSignature ? SignatureCheckResult.Valid : SignatureCheckResult.Invalid;
    }

    /// <summary>Windows can verify Authenticode.</summary>
    internal static bool SignatureCheckingSupported => true;
}
#else

/// <summary>
/// Signature verification on platforms without Authenticode.
/// </summary>
/// <remarks>
/// Windows uses WinVerifyTrust through the P/Invoke above. There is no Linux equivalent - the
/// format is a Microsoft PE certificate table - so nothing can be checked here.
///
/// This used to return <c>true</c>, on the reasoning that the files reaching this point are ones the
/// user already had on disk and picked. That reasoning holds, but the return value was doing double
/// duty as "this passed a check", and callers reported success without saying that no check had run.
/// A silent pass is worse than an honest gap: the user has no way to learn that the one protection
/// Windows has is simply absent.
///
/// Returning <see cref="SignatureCheckResult.Unavailable"/> lets callers keep the feature working
/// while making the gap visible. See <see cref="UnsupportedPlatformWarning"/> for the wording.
/// </remarks>
internal static class WinTrust
{
    /// <summary>Whether this platform can verify Authenticode signatures at all.</summary>
    internal static bool SignatureCheckingSupported => false;

    public static SignatureCheckResult VerifyEmbeddedSignature(string filePath)
    {
        Logger.Warning(
            $"Signature verification is not supported on this platform; {filePath} was NOT verified. " +
            "Accepted because it is a local file the user selected.");

        return SignatureCheckResult.Unavailable;
    }
}
#endif