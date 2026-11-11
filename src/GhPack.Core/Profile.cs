using System.Text.Json;
using System.Text.Json.Serialization;

namespace GhPack.Core;

/// <summary>A named group of plugins. Plugins not in <see cref="Enabled"/> do not load.</summary>
public sealed class Profile
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

    /// <summary>.gha file names (e.g. "Karamba.gha") that load when this profile is active.</summary>
    [JsonPropertyName("enabled")] public List<string> Enabled { get; set; } = new();

    public bool Includes(string pluginName) =>
        Enabled.Contains(pluginName, StringComparer.OrdinalIgnoreCase);
}

public sealed class ProfileFile
{
    [JsonPropertyName("activeProfile")] public string? ActiveProfile { get; set; }
    [JsonPropertyName("profiles")] public List<Profile> Profiles { get; set; } = new();

    /// <summary>Extra folders to scan (e.g. folders added in GrasshopperDeveloperSettings).</summary>
    [JsonPropertyName("extraFolders")] public List<string> ExtraFolders { get; set; } = new();

    public Profile? Find(string name) =>
        Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
}

public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string FilePath { get; }

    public ProfileStore(string filePath) => FilePath = filePath;

    public ProfileFile Load()
    {
        if (!File.Exists(FilePath)) return new ProfileFile();
        return JsonSerializer.Deserialize<ProfileFile>(File.ReadAllText(FilePath), Options) ?? new ProfileFile();
    }

    public void Save(ProfileFile file)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(file, Options));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
