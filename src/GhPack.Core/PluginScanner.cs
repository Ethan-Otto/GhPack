namespace GhPack.Core;

/// <summary>
/// Finds .gha files and reads/writes Grasshopper's own no-load markers.
/// Grasshopper skips "Foo.gha" when "Foo.no9" (or "Foo.no9-SchemeName") exists beside it.
/// </summary>
public sealed class PluginScanner
{
    private readonly IReadOnlyList<string> _markerExtensions;

    /// <param name="markerExtensions">
    /// Marker extensions that Grasshopper honours, e.g. ".no9". The first one is written on disable;
    /// all of them are removed on enable.
    /// </param>
    public PluginScanner(IReadOnlyList<string> markerExtensions)
    {
        if (markerExtensions.Count == 0) throw new ArgumentException("At least one marker extension is required.", nameof(markerExtensions));
        _markerExtensions = markerExtensions;
    }

    public IReadOnlyList<PluginEntry> Scan(IEnumerable<ScanFolder> folders)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<PluginEntry>();
        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder.Path)) continue;
            string[] files;
            try
            {
                files = Directory.GetFiles(folder.Path, "*.gha",
                    folder.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var file in files)
            {
                var full = Path.GetFullPath(file);
                if (seen.Add(full)) result.Add(new PluginEntry(full, IsEnabled(full)));
            }
        }
        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    public bool IsEnabled(string ghaPath) => !MarkerPaths(ghaPath).Any(File.Exists);

    public void SetEnabled(string ghaPath, bool enabled)
    {
        if (enabled)
        {
            foreach (var marker in MarkerPaths(ghaPath))
                if (File.Exists(marker)) File.Delete(marker);
        }
        else if (IsEnabled(ghaPath))
        {
            File.WriteAllText(MarkerPaths(ghaPath).First(),
                "This file prevents the GHA file of the same name from being loaded into Grasshopper. Written by GhPack.");
        }
    }

    private IEnumerable<string> MarkerPaths(string ghaPath) =>
        _markerExtensions.Select(ext => Path.ChangeExtension(ghaPath, ext)).Distinct(StringComparer.Ordinal);
}
