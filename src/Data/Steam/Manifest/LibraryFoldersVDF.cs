using System.Collections.Generic;

namespace Chronos.Data.Steam.Manifest;

internal class LibraryFoldersVDF
{
    public string Path { get; set; } = string.Empty;
    public Dictionary<string, string> Apps { get; set; } = new Dictionary<string, string>();
}
