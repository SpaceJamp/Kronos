using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Kronos.Helpers;

/// <summary>
/// A secret kept in Windows Credential Manager, through the Win32 Cred* functions in advapi32.
/// </summary>
/// <remarks>
/// <para>
/// The proxy settings used WinRT's <c>Windows.Security.Credentials.PasswordVault</c>. Uno's projection of
/// it is not implemented, so loading threw NotImplementedException on every launch and - worse, and not
/// visible in the log as anything but an error - saving did too: a proxy could not be configured at all.
/// </para>
/// <para>
/// A generic credential holds the same thing, protected the same way (per user, by Windows), and shows up
/// under Windows Credentials in Credential Manager. It is a different store from PasswordVault's, so a
/// proxy saved by a WinUI build is not carried over and has to be entered again once.
/// </para>
/// </remarks>
internal static class CredentialStore
{
    const int CRED_TYPE_GENERIC = 1;
    const int CRED_PERSIST_LOCAL_MACHINE = 2;
    const int ERROR_NOT_FOUND = 1168;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredReadW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredWriteW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CredWrite(ref CREDENTIAL credential, int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredDeleteW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    static extern void CredFree(IntPtr buffer);

    /// <summary>The secret stored under a target name, or null if there is none.</summary>
    internal static string? Read(string target)
    {
        if (OperatingSystem.IsWindows() == false)
        {
            return null;
        }

        if (CredRead(target, CRED_TYPE_GENERIC, 0, out var pointer) == false)
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ERROR_NOT_FOUND)
            {
                Logger.Warning($"Could not read the credential {target}: error {error}.");
            }
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<CREDENTIAL>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return string.Empty;
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    /// <summary>Stores a secret under a target name, replacing any existing one.</summary>
    internal static bool Write(string target, string userName, string secret)
    {
        if (OperatingSystem.IsWindows() == false)
        {
            return false;
        }

        var bytes = Encoding.Unicode.GetBytes(secret);
        var blob = Marshal.AllocHGlobal(Math.Max(bytes.Length, 1));
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = target,
                UserName = userName,
                CredentialBlob = blob,
                CredentialBlobSize = bytes.Length,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
            };

            if (CredWrite(ref credential, 0) == false)
            {
                Logger.Warning($"Could not save the credential {target}: error {Marshal.GetLastWin32Error()}.");
                return false;
            }
            return true;
        }
        finally
        {
            // The secret was copied in; clear it before the memory goes back.
            for (var i = 0; i < bytes.Length; i++)
            {
                Marshal.WriteByte(blob, i, 0);
            }
            Marshal.FreeHGlobal(blob);
        }
    }

    /// <summary>Removes a stored secret. Removing one that does not exist is not an error.</summary>
    internal static void Delete(string target)
    {
        if (OperatingSystem.IsWindows() == false)
        {
            return;
        }

        if (CredDelete(target, CRED_TYPE_GENERIC, 0) == false)
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ERROR_NOT_FOUND)
            {
                Logger.Warning($"Could not delete the credential {target}: error {error}.");
            }
        }
    }
}
