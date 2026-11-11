namespace GhPack.Core;

/// <summary>One .gha file that Grasshopper can load.</summary>
/// <param name="FilePath">Full path of the .gha file.</param>
/// <param name="Enabled">False when a no-load marker file sits beside the .gha.</param>
public sealed record PluginEntry(string FilePath, bool Enabled)
{
    /// <summary>File name with extension. Profiles identify plugins by this name.</summary>
    public string Name => Path.GetFileName(FilePath);

    public string Folder => Path.GetDirectoryName(FilePath) ?? string.Empty;
}

/// <summary>A folder that Grasshopper scans for .gha files.</summary>
public sealed record ScanFolder(string Path, bool Recursive);
