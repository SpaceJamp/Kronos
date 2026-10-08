using System;
using System.Runtime.InteropServices;

namespace Kronos;

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

    public static bool VerifyEmbeddedSignature(string fileName)
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

        return validSignature;
    }
}
#else
// Stub implementation for non-Windows platforms (Linux)
// On Linux, Authenticode signatures are not applicable. 
// We return true to allow DLLs, but log a warning.
internal static class WinTrust
{
    public static bool VerifyEmbeddedSignature(string filePath)
    {
        Kronos.Logger.Warning($"Signature verification not supported on this platform. Skipping check for: {filePath}");
        return true; // Allow on non-Windows
    }
}
#endif