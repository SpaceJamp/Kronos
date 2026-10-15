using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace Kronos.Helpers;

internal class FileSystemHelper
{
    internal struct FileFilter
    {
        public string Name { get; }
        public string Spec { get; }

        public FileFilter(string name, string spec)
        {
            Name = name;
            Spec = spec;
        }
    }

    const int ERROR_CANCELLED = unchecked((int)0x800704c7);

    /// <summary>
    /// Reads an item's file system path and frees the string the shell allocated for it.
    /// </summary>
    /// <remarks>
    /// GetDisplayName hands back a CoTaskMem string that the caller owns. It was previously copied
    /// with ToString() and never freed, so every path picked through a dialog leaked.
    /// </remarks>
    static unsafe string GetFileSystemPath(IShellItem item)
    {
        PWSTR filename = default;
        try
        {
            item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, &filename);
            return filename.ToString();
        }
        finally
        {
            if (filename.Value is not null)
            {
                Marshal.FreeCoTaskMem((nint)filename.Value);
            }
        }
    }

    /// <summary>
    /// Builds the native filter specs for SetFileTypes. Release with <see cref="FreeFilterSpecs"/>.
    /// </summary>
    static unsafe COMDLG_FILTERSPEC[] CreateFilterSpecs(IReadOnlyList<FileFilter>? filters)
    {
        if (filters is null || filters.Count == 0)
        {
            filters = new List<FileFilter>()
            {
                new FileFilter("All files", "*.*")
            }.AsReadOnly();
        }

        var extensions = new COMDLG_FILTERSPEC[filters.Count];
        for (var i = 0; i < filters.Count; ++i)
        {
            extensions[i].pszSpec = (char*)Marshal.StringToHGlobalUni(filters[i].Spec);
            extensions[i].pszName = (char*)Marshal.StringToHGlobalUni(filters[i].Name);
        }
        return extensions;
    }

    /// <summary>
    /// Frees the HGlobal strings allocated by <see cref="CreateFilterSpecs"/>. These were previously
    /// never freed, leaking two strings per filter every time a file dialog opened.
    /// </summary>
    static unsafe void FreeFilterSpecs(COMDLG_FILTERSPEC[]? extensions)
    {
        if (extensions is null)
        {
            return;
        }

        for (var i = 0; i < extensions.Length; ++i)
        {
            if (extensions[i].pszSpec.Value is not null)
            {
                Marshal.FreeHGlobal((nint)extensions[i].pszSpec.Value);
            }
            if (extensions[i].pszName.Value is not null)
            {
                Marshal.FreeHGlobal((nint)extensions[i].pszName.Value);
            }
        }
    }

    internal static string OpenFolder(nint hWnd, string? defaultPath = null, string? okButtonLabel = null)
    {
        try
        {
            var hResult = PInvoke.CoCreateInstance<IFileOpenDialog>(typeof(FileOpenDialog).GUID, null, CLSCTX.CLSCTX_INPROC_SERVER, out var folderOpenDialog);
            if (hResult < 0)
            {
                Marshal.ThrowExceptionForHR(hResult);
            }

            // Set options to pick a folder
            folderOpenDialog.SetOptions(FILEOPENDIALOGOPTIONS.FOS_PICKFOLDERS | FILEOPENDIALOGOPTIONS.FOS_FORCEFILESYSTEM);

            // If no default is provided (or doesn't exist) use My Computer.
            // I hope users don't just click C:\ and call it a day -_-
            if (string.IsNullOrWhiteSpace(defaultPath) || Directory.Exists(defaultPath) == false)
            { 
                defaultPath = Environment.GetFolderPath(Environment.SpecialFolder.MyComputer);
            }

            // Set the default folder.
            hResult = PInvoke.SHCreateItemFromParsingName<IShellItem>(defaultPath, null, out var directoryShellItem);
            if (hResult < 0)
            {
                Marshal.ThrowExceptionForHR(hResult);
            }

            folderOpenDialog.SetFolder((IShellItem)directoryShellItem);
            folderOpenDialog.SetDefaultFolder((IShellItem)directoryShellItem);
            if (string.IsNullOrWhiteSpace(okButtonLabel) == false)
            {
                folderOpenDialog.SetOkButtonLabel(okButtonLabel);
            }
            folderOpenDialog.Show(new HWND(hWnd));

            folderOpenDialog.GetResult(out var ppsi);

            return GetFileSystemPath(ppsi);
        }
        catch (COMException ex) when (ex.HResult == ERROR_CANCELLED)
        {
            // NOOP
            return string.Empty;
        }
    }

    internal static string OpenFile(nint hWnd, IReadOnlyList<FileFilter>? filters, string? defaultPath = null, string? defaultExtension = null, string? okButtonLabel = null)
    {
        var openedFile = OpenFileInternal(hWnd, false, filters, defaultPath, defaultExtension, okButtonLabel);
        if (openedFile.Count == 1)
        {
            return openedFile[0];
        }
        return string.Empty;
    }

    internal static List<string> OpenMultipleFiles(nint hWnd, IReadOnlyList<FileFilter>? filters, string? defaultPath = null, string? defaultExtension = null, string? okButtonLabel = null)
    {
        return OpenFileInternal(hWnd, true, filters, defaultPath, defaultExtension, okButtonLabel);
    }

    static List<string> OpenFileInternal(nint hWnd, bool pickMultiple, IReadOnlyList<FileFilter>? filters, string? defaultPath = null, string? defaultExtension = null, string? okButtonLabel = null)
    {
        COMDLG_FILTERSPEC[]? extensions = null;
        try
        {
            var hResult = PInvoke.CoCreateInstance<IFileOpenDialog>(typeof(FileOpenDialog).GUID, null, CLSCTX.CLSCTX_INPROC_SERVER, out var fileOpenDialog);
            if (hResult < 0)
            {
                Marshal.ThrowExceptionForHR(hResult);
            }

            extensions = CreateFilterSpecs(filters);
            fileOpenDialog.SetFileTypes(extensions);


            // If no default is provided (or doesn't exist) use My Computer.
            // I hope users don't just click C:\ and call it a day -_-
            if (string.IsNullOrWhiteSpace(defaultPath) || Directory.Exists(defaultPath) == false)
            {
                defaultPath = Environment.GetFolderPath(Environment.SpecialFolder.MyComputer);
            }


            // Set the default folder.
            hResult = PInvoke.SHCreateItemFromParsingName<IShellItem>(defaultPath, null, out var directoryShellItem);
            if (hResult < 0)
            {
                Marshal.ThrowExceptionForHR(hResult);
            }

            fileOpenDialog.SetFolder((IShellItem)directoryShellItem);
            fileOpenDialog.SetDefaultFolder((IShellItem)directoryShellItem);

            if (string.IsNullOrWhiteSpace(okButtonLabel) == false)
            {
                fileOpenDialog.SetOkButtonLabel(okButtonLabel);
            }

            // This does not seem to do anything.
            if (string.IsNullOrWhiteSpace(defaultExtension) == false)
            {
                fileOpenDialog.SetDefaultExtension(defaultExtension);
            }

            if (pickMultiple)
            {
                fileOpenDialog.SetOptions(FILEOPENDIALOGOPTIONS.FOS_ALLOWMULTISELECT | FILEOPENDIALOGOPTIONS.FOS_FORCEFILESYSTEM);
            }
            else
            {
                fileOpenDialog.SetOptions(FILEOPENDIALOGOPTIONS.FOS_FORCEFILESYSTEM);
            }

            fileOpenDialog.Show(new HWND(hWnd));

            var results = new List<string>();

            if (pickMultiple)
            {
                fileOpenDialog.GetResults(out var ppsiArray);

                ppsiArray.GetCount(out uint count);
                for (uint i = 0; i < count; ++i)
                {
                    ppsiArray.GetItemAt(i, out var ppsi);

                    if (ppsi is null)
                    {
                        throw new Exception("Failed to get result from file open dialog.");
                    }

                    results.Add(GetFileSystemPath(ppsi));
                }
            }
            else
            {
                fileOpenDialog.GetResult(out var ppsi);

                if (ppsi is null)
                {
                    throw new Exception("Failed to get result from file open dialog.");
                }

                results.Add(GetFileSystemPath(ppsi));
            }

            return results;
        }
        catch (COMException ex) when (ex.HResult == ERROR_CANCELLED)
        {
            // NOOP
            return new List<string>();
        }
        finally
        {
            FreeFilterSpecs(extensions);
        }
    }


    internal static string SaveFile(nint hWnd, IReadOnlyList<FileFilter>? filters, string? defaultPath = null, string? defaultFileName = null, string? defaultExtension = null, string? okButtonLabel = null)
    {
        COMDLG_FILTERSPEC[]? extensions = null;
        try
        {
            var hResult = PInvoke.CoCreateInstance<IFileSaveDialog>(typeof(FileSaveDialog).GUID, null, CLSCTX.CLSCTX_INPROC_SERVER, out var fileSaveDialog);
            if (hResult < 0)
            {
                Marshal.ThrowExceptionForHR(hResult);
            }

            extensions = CreateFilterSpecs(filters);
            fileSaveDialog.SetFileTypes(extensions);


            // If no default is provided (or doesn't exist) use My Computer.
            // I hope users don't just click C:\ and call it a day -_-
            if (string.IsNullOrWhiteSpace(defaultPath) || Directory.Exists(defaultPath) == false)
            {
                defaultPath = Environment.GetFolderPath(Environment.SpecialFolder.MyComputer);
            }


            // Set the default folder.
            hResult = PInvoke.SHCreateItemFromParsingName<IShellItem>(defaultPath, null, out var directoryShellItem);
            if (hResult < 0)
            {
                Marshal.ThrowExceptionForHR(hResult);
            }

            fileSaveDialog.SetFolder((IShellItem)directoryShellItem);
            fileSaveDialog.SetDefaultFolder((IShellItem)directoryShellItem);

            if (string.IsNullOrWhiteSpace(okButtonLabel) == false)
            {
                fileSaveDialog.SetOkButtonLabel(okButtonLabel);
            }

            if (string.IsNullOrWhiteSpace(defaultFileName) == false)
            {
                fileSaveDialog.SetFileName(defaultFileName);
            }


            // This does not seem to do anything. Disabled for now.
            if (string.IsNullOrWhiteSpace(defaultExtension) == false)
            {
                fileSaveDialog.SetDefaultExtension(defaultExtension);
            }

            fileSaveDialog.Show(new HWND(hWnd));

            fileSaveDialog.GetResult(out var ppsi);

            return GetFileSystemPath(ppsi);
        }
        catch (COMException ex) when (ex.HResult == ERROR_CANCELLED)
        {
            // NOOP
            return string.Empty;
        }
        finally
        {
            FreeFilterSpecs(extensions);
        }
    }

    internal static void OpenFolderInExplorer(string path)
    {
        Process.Start("explorer.exe", path);
    }

    internal static void OpenFolderInExplorerSelectFile(string path)
    {
        Process.Start("explorer.exe", $"/select,{path}");
    }
}
