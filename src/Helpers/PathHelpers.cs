using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace Chronos.Helpers;

internal static class PathHelpers
{
    public static char[] InvalidFileNamePathChars { get; private set; }

    static PathHelpers()
    {
        var invalidChars = new List<char>();
        invalidChars.AddRange(Path.GetInvalidFileNameChars());
        invalidChars.AddRange(Path.GetInvalidPathChars());
        invalidChars.Add('.');
        InvalidFileNamePathChars = invalidChars.Distinct().ToArray();
    }


    /// <summary>
    /// Tries to format path on disk so any and all paths will match after they have gone through this method.  
    /// </summary>
    /// <param name="path">Path on local disk</param>
    /// <returns>Formatted path on disk</returns>
    internal static string NormalizePath(string path)
    {
        // Via https://stackoverflow.com/a/21058152
        //new Uri(path).LocalPath
        var fullPath = Path.GetFullPath(path);

        // Never trim the trailing separator off a drive root. "C:\" trimmed becomes "C:", which
        // Windows treats as *drive relative* (relative to the current directory on that drive)
        // rather than the root. Directory.Exists("C:") is false, so a game installed at a drive
        // root was treated as missing.
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root) == false &&
            string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

}
