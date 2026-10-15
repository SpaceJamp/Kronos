using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;

namespace Kronos.Helpers;

/// <summary>
/// Installed MSIX/Appx packages, through the Win32 packaging API in kernel32.
/// </summary>
/// <remarks>
/// <para>
/// The Xbox library used WinRT's <c>Windows.Management.Deployment.PackageManager</c>. Under Uno that name
/// resolves to Uno's own projection, whose FindPackagesForUser throws NotImplementedException, so the Xbox
/// library failed to load on every launch and only games already in the database were shown. The real
/// WinRT projection cannot simply be added back beside Uno: its types collide with Uno's of the same name.
/// </para>
/// <para>
/// These functions are plain Win32 (appmodel.h, Windows 8 and later) and need no projection at all. They
/// also answer the question the library actually has, which is narrower than "list every package": is
/// this one package family installed for the current user, and where.
/// </para>
/// </remarks>
internal static class AppxPackages
{
    const int ERROR_SUCCESS = 0;
    const int ERROR_INSUFFICIENT_BUFFER = 122;

    /// <summary>Main packages only: no frameworks, resource packages or optional packages.</summary>
    const uint PACKAGE_FILTER_HEAD = 0x00000010;

    /// <summary>Packages registered for the current user directly, not only as dependencies.</summary>
    const uint PACKAGE_FILTER_DIRECT = 0x00000020;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PACKAGE_ID
    {
        public uint reserved;
        public uint processorArchitecture;
        public ulong version;
        public string name;
        public string publisher;
        public string? resourceId;
        public string? publisherId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int PackageFamilyNameFromId(ref PACKAGE_ID packageId, ref uint packageFamilyNameLength, StringBuilder? packageFamilyName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int FindPackagesByPackageFamily(
        string packageFamilyName,
        uint packageFilters,
        ref uint count,
        [Out] IntPtr[]? packageFullNames,
        ref uint bufferLength,
        [Out] char[]? buffer,
        [Out] uint[]? packageProperties);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetPackagePathByFullName(string packageFullName, ref uint pathLength, StringBuilder? path);

    /// <summary>
    /// The family name ("Name_publisherhash") for a package name and publisher, as Windows computes it.
    /// </summary>
    internal static string? FamilyNameFromIdentity(string name, string publisher)
    {
        if (OperatingSystem.IsWindows() == false || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(publisher))
        {
            return null;
        }

        var id = new PACKAGE_ID { name = name, publisher = publisher };
        uint length = 0;
        var result = PackageFamilyNameFromId(ref id, ref length, null);
        if (result != ERROR_INSUFFICIENT_BUFFER || length == 0)
        {
            return null;
        }

        var builder = new StringBuilder((int)length);
        result = PackageFamilyNameFromId(ref id, ref length, builder);
        return result == ERROR_SUCCESS ? builder.ToString() : null;
    }

    /// <summary>
    /// The full names of the main packages in a family that are installed for the current user.
    /// </summary>
    internal static IReadOnlyList<string> FindInstalledPackages(string familyName)
    {
        if (OperatingSystem.IsWindows() == false || string.IsNullOrWhiteSpace(familyName))
        {
            return [];
        }

        const uint filters = PACKAGE_FILTER_HEAD | PACKAGE_FILTER_DIRECT;
        uint count = 0;
        uint bufferLength = 0;
        var result = FindPackagesByPackageFamily(familyName, filters, ref count, null, ref bufferLength, null, null);
        if (result == ERROR_SUCCESS || count == 0)
        {
            // Success with nothing to return: the family is not installed.
            return [];
        }
        if (result != ERROR_INSUFFICIENT_BUFFER)
        {
            Logger.Warning($"FindPackagesByPackageFamily({familyName}) failed with error {result}.");
            return [];
        }

        var fullNamePointers = new IntPtr[count];
        var buffer = new char[bufferLength];
        var properties = new uint[count];
        // The pointers point into the char[] buffer, so it has to stay where the call left it while they
        // are read.
        var pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            result = FindPackagesByPackageFamily(familyName, filters, ref count, fullNamePointers, ref bufferLength, buffer, properties);
            if (result != ERROR_SUCCESS)
            {
                Logger.Warning($"FindPackagesByPackageFamily({familyName}) failed with error {result}.");
                return [];
            }

            var fullNames = new List<string>((int)count);
            for (var i = 0; i < count; i++)
            {
                var fullName = Marshal.PtrToStringUni(fullNamePointers[i]);
                if (string.IsNullOrEmpty(fullName) == false)
                {
                    fullNames.Add(fullName);
                }
            }
            return fullNames;
        }
        finally
        {
            pin.Free();
        }
    }

    /// <summary>Where a package is installed, or null.</summary>
    internal static string? GetInstalledPath(string packageFullName)
    {
        if (OperatingSystem.IsWindows() == false || string.IsNullOrWhiteSpace(packageFullName))
        {
            return null;
        }

        uint length = 0;
        var result = GetPackagePathByFullName(packageFullName, ref length, null);
        if (result != ERROR_INSUFFICIENT_BUFFER || length == 0)
        {
            return null;
        }

        var builder = new StringBuilder((int)length);
        result = GetPackagePathByFullName(packageFullName, ref length, builder);
        return result == ERROR_SUCCESS ? builder.ToString() : null;
    }

    /// <summary>
    /// Whether the package's manifest declares an application that appears in the app list. This is what
    /// PackageManager's GetAppListEntriesAsync().Count &gt; 0 was used to establish.
    /// </summary>
    internal static bool HasAppListEntry(string installedPath)
    {
        var manifest = Path.Combine(installedPath, "AppxManifest.xml");
        if (File.Exists(manifest) == false)
        {
            return false;
        }

        try
        {
            var document = new XmlDocument();
            document.Load(manifest);

            foreach (XmlNode node in document.GetElementsByTagName("*"))
            {
                if (node.LocalName != "Application")
                {
                    continue;
                }

                // AppListEntry="none" on VisualElements hides an application from the app list.
                var hidden = false;
                foreach (XmlNode child in node.ChildNodes)
                {
                    if (child.LocalName == "VisualElements" &&
                        string.Equals(child.Attributes?["AppListEntry"]?.Value, "none", StringComparison.OrdinalIgnoreCase))
                    {
                        hidden = true;
                    }
                }

                if (hidden == false)
                {
                    return true;
                }
            }
        }
        catch (Exception err) when (err is XmlException or IOException or UnauthorizedAccessException)
        {
            Logger.Error(err, $"Could not read {manifest}.");
        }

        return false;
    }
}
